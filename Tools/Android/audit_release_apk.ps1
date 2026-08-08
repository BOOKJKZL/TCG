param(
    [string]$ApkPath,
    [string]$ExpectedVersionName,
    [int]$ExpectedVersionCode,
    [string]$ExpectedCertificateSha256,
    [string]$PackageId = "com.personal.universalgacha",
    [string]$UnityVersion = "6000.0.73f1",
    [string]$ReportPath,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$publishedLatestUrl = "https://universal-gacha-content.jiejingleek.chatgpt.site/api/releases/android/latest.json"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Resolve-AndroidBuildTool {
    param([string]$Name)
    $roots = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools",
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\build-tools")
    )
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }
        $versions = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object `
            @{ Expression = { try { [version]$_.Name } catch { [version]"0.0" } }; Descending = $true })
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName $Name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }
    throw "Android build tool was not found: $Name"
}

function Invoke-ExternalCommand {
    param([string]$Executable, [string[]]$Arguments)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $Executable @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    return [pscustomobject]@{ Output = $output; ExitCode = $exitCode }
}

function Normalize-Fingerprint {
    param([string]$Value)
    return ([regex]::Replace(([string]$Value), '[^0-9A-Fa-f]', '')).ToLowerInvariant()
}

function Get-BadgingMetadata {
    param([string]$Text)
    $package = [regex]::Match(
        $Text,
        "(?m)^package: name='([^']+)' versionCode='(\d+)' versionName='([^']+)'(?:\s|$)")
    if (-not $package.Success) {
        throw "aapt badging did not contain package identity and version metadata."
    }
    $native = [regex]::Match($Text, "(?m)^native-code:\s*(.+)$")
    $abis = @(if ($native.Success) {
        [regex]::Matches($native.Groups[1].Value, "'([^']+)'") | ForEach-Object { $_.Groups[1].Value }
    })
    $permissions = @([regex]::Matches($Text, "(?m)^uses-permission:\s+name='([^']+)'") |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $target = [regex]::Match($Text, "(?m)^targetSdkVersion:'(\d+)'$")
    $minimum = [regex]::Match($Text, "(?m)^sdkVersion:'(\d+)'$")
    return [pscustomobject]@{
        PackageId = $package.Groups[1].Value
        VersionCode = [int]$package.Groups[2].Value
        VersionName = $package.Groups[3].Value
        Abis = $abis
        Permissions = $permissions
        TargetSdk = if ($target.Success) { [int]$target.Groups[1].Value } else { 0 }
        MinSdk = if ($minimum.Success) { [int]$minimum.Groups[1].Value } else { 0 }
    }
}

function Get-ManifestDebuggable {
    param([string]$Text)
    $line = @($Text -split "`r?`n" | Where-Object { $_ -match 'android:debuggable' } | Select-Object -First 1)
    if ($line.Count -eq 0) {
        return $false
    }
    if ($line[0] -match '(?i)(0xffffffff|0x1)\s*$') {
        return $true
    }
    if ($line[0] -match '(?i)0x0\s*$') {
        return $false
    }
    throw "Android manifest contains an unrecognized debuggable value: $($line[0].Trim())"
}

function Get-SignerCertificateSha256Set {
    param([string]$Text)
    $matches = [regex]::Matches(
        $Text,
        '(?im)^Signer #\d+ certificate SHA-256 digest:\s*([0-9a-f: ]+)\s*$')
    if ($matches.Count -eq 0) {
        throw "apksigner did not report a signer certificate SHA-256 digest."
    }
    return @($matches | ForEach-Object { Normalize-Fingerprint $_.Groups[1].Value })
}

function Test-ArchiveContract {
    param([string]$Path)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $result = $null
    try {
        $forbiddenEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(?i)(^|/)(LocalContent|ReleaseSigning)(/|$)' -or
            $_.FullName -match '(?i)(site-publisher-credential|remote-content\.json|catalog-cache)' -or
            $_.FullName -match '(?i)\.(keystore|jks|p12|pfx)$' -or
            $_.FullName -match '(?i)(nunit\.framework|UnityEngine\.TestRunner|UnityEditor\.TestRunner|\.Tests)\.dll$'
        } | ForEach-Object { $_.FullName })

        $developmentMarkers = New-Object System.Collections.Generic.List[string]
        foreach ($entry in @($archive.Entries | Where-Object { $_.FullName -match '(?i)boot\.config$' })) {
            $reader = [IO.StreamReader]::new($entry.Open())
            try {
                $content = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            foreach ($marker in @(
                'player-connection-debug=1',
                'player-connection-mode=Listen',
                'player-connection-ip=',
                'wait-for-managed-debugger=1',
                'wait-for-native-debugger=1',
                'profiler-enable=1',
                'deep-profiling-support=1')) {
                if ($content.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $developmentMarkers.Add("$($entry.FullName):$marker")
                }
            }
        }

        $binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($Path))
        foreach ($marker in @(
            'GACHA_R2_SECRET_ACCESS_KEY',
            'site-publisher-credential',
            'LocalContent/Imports',
            'LocalContent\\Imports')) {
            if ($binaryText.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $developmentMarkers.Add("apk:$marker")
            }
        }

        $result = [pscustomobject]@{
            Passed = $forbiddenEntries.Count -eq 0 -and $developmentMarkers.Count -eq 0
            EntryCount = $archive.Entries.Count
            ForbiddenEntries = $forbiddenEntries
            DevelopmentMarkers = @($developmentMarkers)
        }
    }
    finally {
        $archive.Dispose()
    }
    return $result
}

function New-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    return [pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail }
}

function Invoke-SelfTest {
    $passed = 0
    $validBadging = @"
package: name='com.personal.universalgacha' versionCode='2' versionName='0.1.1'
sdkVersion:'23'
targetSdkVersion:'36'
uses-permission: name='android.permission.INTERNET'
uses-permission: name='android.permission.ACCESS_NETWORK_STATE'
uses-permission: name='android.permission.VIBRATE'
uses-permission: name='com.personal.universalgacha.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION'
native-code: 'arm64-v8a'
"@
    $metadata = Get-BadgingMetadata $validBadging
    if ($metadata.PackageId -ne "com.personal.universalgacha" -or
        $metadata.VersionCode -ne 2 -or $metadata.VersionName -ne "0.1.1" -or
        $metadata.TargetSdk -ne 36 -or $metadata.Abis.Count -ne 1 -or $metadata.Abis[0] -ne "arm64-v8a") {
        throw "Self-test failed: valid aapt badging metadata."
    }
    $passed++

    if ((Get-ManifestDebuggable "A: android:debuggable(0x0101000f)=(type 0x12)0xffffffff") -ne $true -or
        (Get-ManifestDebuggable "A: android:debuggable(0x0101000f)=(type 0x12)0x0") -ne $false -or
        (Get-ManifestDebuggable "A: android:label(0x01010001)=@0x7f010001") -ne $false) {
        throw "Self-test failed: manifest debuggable parsing."
    }
    $passed++

    $singleSigner = @(Get-SignerCertificateSha256Set `
        "Signer #1 certificate SHA-256 digest: AA:BB:CC:DD")
    $multipleSigners = @(Get-SignerCertificateSha256Set @"
Signer #1 certificate SHA-256 digest: AA:BB:CC:DD
Signer #2 certificate SHA-256 digest: 11:22:33:44
"@)
    if ($singleSigner.Count -ne 1 -or $singleSigner[0] -ne "aabbccdd" -or
        $multipleSigners.Count -ne 2 -or $multipleSigners[1] -ne "11223344" -or
        (Normalize-Fingerprint "AA BB:cc-dd") -ne "aabbccdd") {
        throw "Self-test failed: certificate fingerprint parsing."
    }
    $passed++

    $malformedDebuggableRejected = $false
    try {
        Get-ManifestDebuggable "A: android:debuggable(0x0101000f)=maybe" | Out-Null
    }
    catch {
        $malformedDebuggableRejected = $true
    }
    if (-not $malformedDebuggableRejected) {
        throw "Self-test failed: malformed manifest debuggable value was accepted."
    }
    $passed++

    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $fixtureRoot = Join-Path $tempPrefix ("gacha-release-audit-" + [Guid]::NewGuid().ToString("N"))
    try {
        [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
        $validArchive = Join-Path $fixtureRoot "valid.apk"
        $stream = [IO.File]::Open($validArchive, [IO.FileMode]::CreateNew)
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $zip.CreateEntry("assets/bin/Data/boot.config")
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write("player-connection-debug=0") } finally { $writer.Dispose() }
        }
        finally {
            $zip.Dispose()
            $stream.Dispose()
        }
        if (-not (Test-ArchiveContract $validArchive).Passed) {
            throw "Self-test failed: valid archive contract."
        }

        $invalidArchive = Join-Path $fixtureRoot "invalid.apk"
        $stream = [IO.File]::Open($invalidArchive, [IO.FileMode]::CreateNew)
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $zip.CreateEntry("assets/bin/Data/boot.config")
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write("player-connection-debug=1") } finally { $writer.Dispose() }
            $zip.CreateEntry("assets/StreamingAssets/LocalContent/secret.json") | Out-Null
        }
        finally {
            $zip.Dispose()
            $stream.Dispose()
        }
        if ((Test-ArchiveContract $invalidArchive).Passed) {
            throw "Self-test failed: invalid archive was accepted."
        }
    }
    finally {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
        if ($resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedFixture) -like 'gacha-release-audit-*' -and
            (Test-Path -LiteralPath $resolvedFixture)) {
            $writer = $null
            $entry = $null
            $zip = $null
            $stream = $null
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
            Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
        }
    }
    $passed++

    Write-Output "Android release APK audit self-test passed: $passed/5."
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

foreach ($required in @(
    @{ Name = "ApkPath"; Value = $ApkPath },
    @{ Name = "ExpectedVersionName"; Value = $ExpectedVersionName },
    @{ Name = "ExpectedCertificateSha256"; Value = $ExpectedCertificateSha256 })) {
    if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
        throw "$($required.Name) is required."
    }
}
$expectedFingerprint = Normalize-Fingerprint $ExpectedCertificateSha256
if ($expectedFingerprint -notmatch '^[0-9a-f]{64}$') {
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
if ($ExpectedVersionCode -le $publishedVersionCode) {
    throw "ExpectedVersionCode $ExpectedVersionCode must be greater than published versionCode $publishedVersionCode."
}

$apkFullPath = Resolve-RepoPath $ApkPath
if (-not (Test-Path -LiteralPath $apkFullPath -PathType Leaf)) {
    throw "Release APK was not found: $apkFullPath"
}
$reportFullPath = if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    [IO.Path]::ChangeExtension($apkFullPath, ".release-audit.json")
}
else { Resolve-RepoPath $ReportPath }

$aapt = Resolve-AndroidBuildTool "aapt.exe"
$apksigner = Resolve-AndroidBuildTool "apksigner.bat"
$zipalign = Resolve-AndroidBuildTool "zipalign.exe"
$badgingResult = Invoke-ExternalCommand $aapt @("dump", "badging", $apkFullPath)
$manifestResult = Invoke-ExternalCommand $aapt @("dump", "xmltree", $apkFullPath, "AndroidManifest.xml")
$signatureResult = Invoke-ExternalCommand $apksigner @("verify", "--verbose", "--print-certs", $apkFullPath)
$alignmentResult = Invoke-ExternalCommand $zipalign @("-c", "-v", "4", $apkFullPath)
if ($badgingResult.ExitCode -ne 0 -or $manifestResult.ExitCode -ne 0) {
    throw "aapt failed: badging exit=$($badgingResult.ExitCode), manifest exit=$($manifestResult.ExitCode)."
}

$metadata = Get-BadgingMetadata ($badgingResult.Output -join "`n")
$debuggable = Get-ManifestDebuggable ($manifestResult.Output -join "`n")
$actualFingerprints = @(if ($signatureResult.ExitCode -eq 0) {
    Get-SignerCertificateSha256Set ($signatureResult.Output -join "`n")
})
$actualFingerprint = if ($actualFingerprints.Count -eq 1) { $actualFingerprints[0] } else { "" }
$archiveContract = Test-ArchiveContract $apkFullPath
$allowedPermissions = @(
    "android.permission.ACCESS_NETWORK_STATE",
    "android.permission.INTERNET",
    "android.permission.VIBRATE",
    "$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
)
$unexpectedPermissions = @($metadata.Permissions | Where-Object { $allowedPermissions -notcontains $_ })

$checks = @(
    (New-Check "package identity" ($metadata.PackageId -eq $PackageId) `
        "actual=$($metadata.PackageId); expected=$PackageId"),
    (New-Check "release version" `
        ($metadata.VersionName -eq $ExpectedVersionName -and
            $metadata.VersionCode -eq $ExpectedVersionCode -and
            $metadata.VersionCode -gt $publishedVersionCode) `
        "actual=$($metadata.VersionName)+$($metadata.VersionCode); published=$publishedVersionCode"),
    (New-Check "ARM64 ABI" `
        ($metadata.Abis.Count -eq 1 -and $metadata.Abis[0] -eq "arm64-v8a") `
        "native-code=$($metadata.Abis -join ',')"),
    (New-Check "SDK and permissions" `
        ($metadata.TargetSdk -ge 34 -and $unexpectedPermissions.Count -eq 0) `
        "minSdk=$($metadata.MinSdk); targetSdk=$($metadata.TargetSdk); unexpected=$($unexpectedPermissions -join ',')"),
    (New-Check "non-debuggable manifest" (-not $debuggable) "debuggable=$debuggable"),
    (New-Check "release signature" `
        ($signatureResult.ExitCode -eq 0 -and
            $actualFingerprints.Count -eq 1 -and
            $actualFingerprint -eq $expectedFingerprint) `
        "apksignerExit=$($signatureResult.ExitCode); signers=$($actualFingerprints.Count); certificateSha256=$($actualFingerprints -join ',')"),
    (New-Check "zipalign" ($alignmentResult.ExitCode -eq 0) "zipalignExit=$($alignmentResult.ExitCode)"),
    (New-Check "release payload boundary" $archiveContract.Passed `
        "entries=$($archiveContract.EntryCount); forbidden=$($archiveContract.ForbiddenEntries -join ','); markers=$($archiveContract.DevelopmentMarkers -join ',')")
)
$valid = @($checks | Where-Object { -not $_.passed }).Count -eq 0
$apk = Get-Item -LiteralPath $apkFullPath
$apkSha256 = (Get-FileHash -LiteralPath $apkFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
$report = [ordered]@{
    schemaVersion = 1
    channel = "stable-candidate"
    valid = $valid
    auditedAtUtc = [DateTime]::UtcNow.ToString("o")
    artifact = [ordered]@{
        fileName = $apk.Name
        downloadBytes = $apk.Length
        sha256 = $apkSha256
        packageId = $metadata.PackageId
        versionName = $metadata.VersionName
        versionCode = $metadata.VersionCode
        publishedVersionCode = $publishedVersionCode
        minSdk = $metadata.MinSdk
        targetSdk = $metadata.TargetSdk
        abis = @($metadata.Abis)
        permissions = @($metadata.Permissions)
        debuggable = $debuggable
        certificateSha256 = $actualFingerprint
        signerCount = $actualFingerprints.Count
    }
    checks = $checks
}
$reportDirectory = Split-Path -Parent $reportFullPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    [IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
}
[IO.File]::WriteAllText(
    $reportFullPath,
    ($report | ConvertTo-Json -Depth 7),
    [Text.UTF8Encoding]::new($false))

foreach ($check in $checks) {
    $state = if ($check.passed) { "PASS" } else { "FAIL" }
    Write-Output "[$state] $($check.name) - $($check.detail)"
}
Write-Output "Audit report: $reportFullPath"
if (-not $valid) {
    exit 2
}
Write-Output "SIGNED ANDROID RELEASE CANDIDATE VERIFIED."
