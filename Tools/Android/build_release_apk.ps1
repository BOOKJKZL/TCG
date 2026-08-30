param(
    [Parameter(Mandatory = $true)]
    [string]$VersionName,
    [Parameter(Mandatory = $true)]
    [int]$VersionCode,
    [Parameter(Mandatory = $true)]
    [string]$KeystorePath,
    [Parameter(Mandatory = $true)]
    [string]$KeyAlias,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCertificateSha256,
    [string]$UnityVersion = "6000.0.73f1",
    [string]$UnityPath,
    [switch]$NonInteractive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$publishedLatestUrl = "https://universal-gacha-content.jiejingleek.chatgpt.site/api/releases/android/latest.json"

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-ReleaseSecret {
    param([string]$EnvironmentName, [string]$Prompt)
    $existing = [Environment]::GetEnvironmentVariable($EnvironmentName, "Process")
    if (-not [string]::IsNullOrWhiteSpace($existing)) {
        return $existing
    }
    if ($NonInteractive) {
        throw "Required CI secret environment variable is missing: $EnvironmentName"
    }

    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($plain)) {
            throw "$Prompt cannot be empty."
        }
        return $plain
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

if ($VersionName -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
    throw "VersionName must use semantic version form such as 0.1.1 or 0.2.0-rc.1."
}
$fingerprint = ([regex]::Replace($ExpectedCertificateSha256, '[^0-9A-Fa-f]', '')).ToLowerInvariant()
if ($fingerprint -notmatch '^[0-9a-f]{64}$') {
    throw "ExpectedCertificateSha256 must contain exactly 64 hexadecimal characters."
}

try {
    $publishedLatest = Invoke-RestMethod -Uri $publishedLatestUrl -Method Get
}
catch {
    throw "Unable to read the authoritative published Android latest manifest: $($_.Exception.Message)"
}
if ([string]$publishedLatest.productId -ne "universal-gacha-simulator" -or
    [int]$publishedLatest.versionCode -lt 1) {
    throw "The published Android latest manifest is invalid."
}
$publishedVersionCode = [int]$publishedLatest.versionCode
if ($VersionCode -le $publishedVersionCode) {
    throw "VersionCode $VersionCode must be greater than published versionCode $publishedVersionCode."
}

$keystoreFullPath = Resolve-RepoPath $KeystorePath
if (-not (Test-Path -LiteralPath $keystoreFullPath -PathType Leaf)) {
    throw "Release keystore was not found: $keystoreFullPath"
}
$assetsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "Assets")).TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if ($keystoreFullPath.StartsWith($assetsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release keystore must never be stored under Assets."
}

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
}
$unityFullPath = [IO.Path]::GetFullPath($UnityPath)
if (-not (Test-Path -LiteralPath $unityFullPath -PathType Leaf)) {
    throw "Unity Editor was not found: $unityFullPath"
}

$projectUnityProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq "Unity.exe" -and
    -not [string]::IsNullOrWhiteSpace([string]$_.CommandLine) -and
    ([string]$_.CommandLine).IndexOf($repoRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($projectUnityProcesses.Count -gt 0) {
    throw "This project is already open in Unity. Close it before starting the isolated release build."
}

$keystorePassword = Get-ReleaseSecret "TCG_ANDROID_KEYSTORE_PASSWORD" "Release keystore password"
$keyPassword = Get-ReleaseSecret "TCG_ANDROID_KEY_PASSWORD" "Release key alias password"
$environment = [ordered]@{
    TCG_ANDROID_VERSION_NAME = $VersionName
    TCG_ANDROID_VERSION_CODE = $VersionCode.ToString([Globalization.CultureInfo]::InvariantCulture)
    TCG_ANDROID_PUBLISHED_LATEST_VERSION_CODE = $publishedVersionCode.ToString([Globalization.CultureInfo]::InvariantCulture)
    TCG_ANDROID_KEYSTORE_PATH = $keystoreFullPath
    TCG_ANDROID_KEYSTORE_PASSWORD = $keystorePassword
    TCG_ANDROID_KEY_ALIAS = $KeyAlias
    TCG_ANDROID_KEY_PASSWORD = $keyPassword
}
$originalEnvironment = @{}
foreach ($entry in $environment.GetEnumerator()) {
    $originalEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
}

$fileVersion = [regex]::Replace($VersionName, '[^0-9A-Za-z.-]', '-')
$outputPath = Join-Path $repoRoot "Builds\Android\Release\UniversalGachaSimulator-release-$fileVersion+$VersionCode.apk"
$logPath = Join-Path $repoRoot "Builds\Android\Release\build-$fileVersion+$VersionCode.log"
$reportPath = [IO.Path]::ChangeExtension($outputPath, ".release-audit.json")
[IO.Directory]::CreateDirectory((Split-Path -Parent $outputPath)) | Out-Null
$buildStartedUtc = [DateTime]::UtcNow
$projectSettingsPath = Join-Path $repoRoot "ProjectSettings\ProjectSettings.asset"
$projectSettingsSnapshot = [IO.File]::ReadAllBytes($projectSettingsPath)
$transientRelativePaths = @(
    "Assets\AddressableAssetsData\Windows.meta",
    "Assets\AddressableAssetsData\link.xml",
    "Assets\AddressableAssetsData\link.xml.meta",
    "Assets\Resources\PerformanceTestRunInfo.json",
    "Assets\Resources\PerformanceTestRunInfo.json.meta",
    "Assets\Resources\PerformanceTestRunSettings.json",
    "Assets\Resources\PerformanceTestRunSettings.json.meta"
)
$transientSnapshots = @{}
foreach ($relativePath in $transientRelativePaths) {
    $fullPath = Join-Path $repoRoot $relativePath
    $transientSnapshots[$fullPath] = if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        [IO.File]::ReadAllBytes($fullPath)
    }
    else {
        $null
    }
}

try {
    foreach ($entry in $environment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
    }
    $unityArguments = @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", ('"{0}"' -f $repoRoot),
        "-executeMethod", "Gacha.EditorTools.AndroidReleaseBuilder.BuildBatch",
        "-logFile", ('"{0}"' -f $logPath)
    )
    $unityProcess = Start-Process `
        -FilePath $unityFullPath `
        -ArgumentList $unityArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($unityProcess.ExitCode -ne 0) {
        throw "Unity release build failed with exit code $($unityProcess.ExitCode). See $logPath"
    }
}
finally {
    try {
        foreach ($entry in $originalEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
        }
    }
    finally {
        $keystorePassword = $null
        $keyPassword = $null
        [IO.File]::WriteAllBytes($projectSettingsPath, $projectSettingsSnapshot)
        foreach ($snapshot in $transientSnapshots.GetEnumerator()) {
            if ($null -eq $snapshot.Value) {
                if (Test-Path -LiteralPath $snapshot.Key -PathType Leaf) {
                    Remove-Item -LiteralPath $snapshot.Key -Force
                }
                continue
            }

            [IO.Directory]::CreateDirectory((Split-Path -Parent $snapshot.Key)) | Out-Null
            [IO.File]::WriteAllBytes($snapshot.Key, [byte[]]$snapshot.Value)
        }
    }
}

if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw "Unity reported success but the expected release APK is missing: $outputPath"
}
$output = Get-Item -LiteralPath $outputPath
if ($output.LastWriteTimeUtc -lt $buildStartedUtc.AddSeconds(-2)) {
    throw "Unity did not refresh the expected release APK; refusing to audit a stale artifact: $outputPath"
}

& (Join-Path $PSScriptRoot "audit_release_apk.ps1") `
    -ApkPath $outputPath `
    -ExpectedVersionName $VersionName `
    -ExpectedVersionCode $VersionCode `
    -ExpectedCertificateSha256 $fingerprint `
    -UnityVersion $UnityVersion `
    -ReportPath $reportPath
if ($LASTEXITCODE -ne 0) {
    throw "Release APK static audit failed with exit code $LASTEXITCODE."
}

Write-Output "Local signed release candidate: $outputPath"
Write-Output "Static audit report: $reportPath"
Write-Output "No artifact was uploaded or published."
