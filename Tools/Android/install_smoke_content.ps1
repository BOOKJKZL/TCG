param(
    [string]$UnityVersion = "6000.0.73f1",
    [string]$ApkPath = "Builds/Android/UniversalGachaSimulator-smoke.apk",
    [string]$PackageId = "com.personal.universalgacha",
    [ValidateSet("Local", "Remote")]
    [string]$ContentMode = "Local",
    [string]$RemoteConfigPath = "LocalContent/remote-content.json",
    [ValidateSet("Auto", "Default", "OpenGLES3", "Vulkan")]
    [string]$GraphicsApi = "Auto",
    [switch]$SkipInstall,
    [switch]$ResetDownloadedContent,
    [switch]$ValidateOnly,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

function Assert-PackageId {
    param([string]$Value)

    if ($Value -notmatch '^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$') {
        throw "PackageId must be a dotted Android application id containing only letters, digits, and underscores."
    }
}

function Get-UnityGraphicsArgument {
    param(
        [string]$Mode,
        [bool]$IsEmulator
    )

    switch ($Mode) {
        "Auto" {
            if ($IsEmulator) {
                return "-force-gles30"
            }
            return $null
        }
        "Default" { return $null }
        "OpenGLES3" { return "-force-gles30" }
        "Vulkan" { return "-force-vulkan" }
        default { throw "Unsupported graphics API mode '$Mode'." }
    }
}

function Read-RemoteContentConfiguration {
    param(
        [string]$Json,
        [string]$SourceName
    )

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "Remote content configuration '$SourceName' is empty."
    }

    try {
        $config = $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Remote content configuration '$SourceName' is not valid JSON: $($_.Exception.Message)"
    }

    if ($null -eq $config -or $config -is [Array] -or $config -is [string] -or $config -is [ValueType]) {
        throw "Remote content configuration '$SourceName' must contain one JSON object."
    }

    $allowed = @("catalogUrl", "timeoutSeconds", "maxCatalogBytes")
    $properties = @($config.PSObject.Properties.Name)
    $unknown = @($properties | Where-Object { $allowed -notcontains $_ })
    if ($unknown.Count -gt 0) {
        throw "Remote content configuration contains unsupported fields: $($unknown -join ', '). Do not put credentials or secrets in the phone configuration."
    }
    if ($properties -notcontains "catalogUrl" -or [string]::IsNullOrWhiteSpace([string]$config.catalogUrl)) {
        throw "Remote content configuration requires catalogUrl."
    }

    $catalogUri = $null
    if (-not [Uri]::TryCreate(([string]$config.catalogUrl).Trim(), [UriKind]::Absolute, [ref]$catalogUri)) {
        throw "catalogUrl must be an absolute URI."
    }
    if ($catalogUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "Android remote catalogUrl must use HTTPS."
    }
    if (-not [string]::IsNullOrEmpty($catalogUri.UserInfo)) {
        throw "catalogUrl cannot contain embedded credentials."
    }
    if (-not [string]::IsNullOrEmpty($catalogUri.Fragment)) {
        throw "catalogUrl cannot contain a fragment."
    }

    if ($properties -contains "timeoutSeconds") {
        $timeout = 0
        if (-not [int]::TryParse([string]$config.timeoutSeconds, [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 120) {
            throw "timeoutSeconds must be an integer from 1 through 120."
        }
    }
    if ($properties -contains "maxCatalogBytes") {
        $maximumBytes = 0
        if (-not [int]::TryParse([string]$config.maxCatalogBytes, [ref]$maximumBytes) -or
            $maximumBytes -lt 1024 -or $maximumBytes -gt (4 * 1024 * 1024)) {
            throw "maxCatalogBytes must be an integer from 1024 through 4194304."
        }
    }

    return $config
}

function Resolve-AdbPath {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"),
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }
    throw "ADB was not found in Unity $UnityVersion, the local Android SDK, or PATH."
}

function Assert-LastExitCode {
    param([string]$Message)

    if ($LASTEXITCODE -ne 0) {
        throw $Message
    }
}

function Get-ConnectedDeviceLines {
    param([string[]]$Lines)

    return @($Lines | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice(\s|$)" })
}

function Invoke-SelfTest {
    $cases = @(
        [pscustomobject]@{
            Name = "valid public configuration"
            Json = '{"catalogUrl":"https://content.example.test/releases/android/catalog.json","timeoutSeconds":15,"maxCatalogBytes":1048576}'
            ShouldPass = $true
        },
        [pscustomobject]@{
            Name = "reject public HTTP"
            Json = '{"catalogUrl":"http://content.example.test/catalog.json"}'
            ShouldPass = $false
        },
        [pscustomobject]@{
            Name = "reject secret field"
            Json = '{"catalogUrl":"https://content.example.test/catalog.json","accessKey":"must-not-be-on-phone"}'
            ShouldPass = $false
        },
        [pscustomobject]@{
            Name = "reject oversized catalog limit"
            Json = '{"catalogUrl":"https://content.example.test/catalog.json","maxCatalogBytes":4194305}'
            ShouldPass = $false
        }
    )

    $passed = 0
    foreach ($case in $cases) {
        $succeeded = $true
        try {
            $null = Read-RemoteContentConfiguration -Json $case.Json -SourceName $case.Name
        }
        catch {
            $succeeded = $false
        }
        if ($succeeded -ne $case.ShouldPass) {
            throw "Self-test failed: $($case.Name)."
        }
        $passed++
    }

    Assert-PackageId -Value "com.personal.universalgacha"
    $passed++
    $invalidPackageRejected = $false
    try {
        Assert-PackageId -Value "com.personal;remove"
    }
    catch {
        $invalidPackageRejected = $true
    }
    if (-not $invalidPackageRejected) {
        throw "Self-test failed: reject unsafe package id."
    }
    $passed++

    $noDevices = @(Get-ConnectedDeviceLines -Lines @("List of devices attached", ""))
    if ($noDevices.Count -ne 0) {
        throw "Self-test failed: empty device list."
    }
    $passed++
    $oneDevice = @(Get-ConnectedDeviceLines -Lines @(
        "List of devices attached",
        "offline-serial`tunauthorized usb:1-1",
        "ready-serial`tdevice product:fixture model:phone"
    ))
    if ($oneDevice.Count -ne 1 -or $oneDevice[0] -notmatch "^ready-serial") {
        throw "Self-test failed: authorized device filtering."
    }
    $passed++

    if ((Get-UnityGraphicsArgument -Mode "Auto" -IsEmulator $true) -ne "-force-gles30") {
        throw "Self-test failed: emulator Auto mode must avoid the SwiftShader Vulkan UI Toolkit path."
    }
    $passed++
    if ($null -ne (Get-UnityGraphicsArgument -Mode "Auto" -IsEmulator $false)) {
        throw "Self-test failed: physical devices must keep the PlayerSettings graphics API order."
    }
    $passed++
    if ((Get-UnityGraphicsArgument -Mode "OpenGLES3" -IsEmulator $false) -ne "-force-gles30") {
        throw "Self-test failed: explicit OpenGLES3 mode."
    }
    $passed++
    if ((Get-UnityGraphicsArgument -Mode "Vulkan" -IsEmulator $true) -ne "-force-vulkan") {
        throw "Self-test failed: explicit Vulkan mode."
    }
    $passed++

    $total = $cases.Count + 8
    Write-Output "Android smoke installer self-test passed: $passed/$total."
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

Assert-PackageId -Value $PackageId
$resolvedApk = $null
if (-not $SkipInstall) {
    $resolvedApk = (Resolve-Path (Join-Path $repoRoot $ApkPath)).Path
}
$contentSource = $null
$remoteConfiguration = $null

if ($ContentMode -eq "Local") {
    $contentSource = (Resolve-Path (Join-Path $repoRoot "LocalContent\Imports")).Path
}
else {
    $remoteConfiguration = (Resolve-Path (Join-Path $repoRoot $RemoteConfigPath)).Path
    $remoteJson = Get-Content -LiteralPath $remoteConfiguration -Raw -Encoding utf8
    $null = Read-RemoteContentConfiguration -Json $remoteJson -SourceName $remoteConfiguration
}

if ($ValidateOnly) {
    $apkSummary = if ($SkipInstall) { "APK reuse selected" } else { "APK and selected content source exist" }
    Write-Output "Validation succeeded for $ContentMode mode. $apkSummary; no device changes were made."
    exit 0
}

$adb = Resolve-AdbPath
$devices = & $adb devices -l
Assert-LastExitCode -Message "ADB could not enumerate Android devices."
$connected = @(Get-ConnectedDeviceLines -Lines $devices)
if ($connected.Count -ne 1) {
    throw "Connect exactly one authorized Android device before running this smoke installer. Found $($connected.Count)."
}

if ($SkipInstall) {
    $installedPackage = & $adb shell pm path $PackageId
    Assert-LastExitCode -Message "Could not query the installed Android package."
    if (@($installedPackage | Where-Object { $_ -match '^package:' }).Count -eq 0) {
        throw "SkipInstall requires $PackageId to be installed on the selected Android target."
    }
}
else {
    & $adb install -r $resolvedApk
    Assert-LastExitCode -Message "APK installation failed."
}

$deviceRoot = "/sdcard/Android/data/$PackageId/files"
& $adb shell mkdir -p $deviceRoot
Assert-LastExitCode -Message "Could not create the app persistent data directory."

if ($ResetDownloadedContent) {
    & $adb shell rm -rf "$deviceRoot/Content" "$deviceRoot/ContentDownloads"
    Assert-LastExitCode -Message "Could not reset the app's downloaded content directories."
}

if ($ContentMode -eq "Local") {
    $deviceContent = "$deviceRoot/Content"
    & $adb shell mkdir -p $deviceContent
    Assert-LastExitCode -Message "Could not create the app content directory."
    & $adb push (Join-Path $contentSource ".") $deviceContent
    Assert-LastExitCode -Message "Private content transfer failed."
}
else {
    & $adb push $remoteConfiguration "$deviceRoot/remote-content.json"
    Assert-LastExitCode -Message "Remote content configuration transfer failed."
}

& $adb shell am force-stop $PackageId
$isEmulator = ((& $adb shell getprop ro.kernel.qemu).Trim() -eq "1")
Assert-LastExitCode -Message "Could not identify whether the Android target is an emulator."
$graphicsArgument = Get-UnityGraphicsArgument -Mode $GraphicsApi -IsEmulator $isEmulator
if ($null -ne $graphicsArgument) {
    $activity = "$PackageId/com.unity3d.player.UnityPlayerGameActivity"
    & $adb shell am start -n $activity --es unity $graphicsArgument
}
else {
    & $adb shell monkey -p $PackageId -c android.intent.category.LAUNCHER 1
}
Assert-LastExitCode -Message "The smoke app could not be launched."

$modeSummary = if ($ContentMode -eq "Local") {
    "copied private local content"
}
else {
    "installed the public remote catalog configuration"
}
$graphicsSummary = if ($null -eq $graphicsArgument) { "default graphics API" } else { $graphicsArgument }
$installSummary = if ($SkipInstall) { "Reused the installed APK" } else { "Installed the APK" }
Write-Output "$installSummary, $modeSummary, and launched $PackageId with $graphicsSummary."
