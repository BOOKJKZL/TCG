param(
    [string]$UnityVersion = "6000.0.73f1",
    [string]$ApkPath = "Builds/Android/UniversalGachaSimulator-smoke.apk",
    [string]$EditModeResults = "TestResults/final-editmode.xml",
    [string]$PlayModeResults = "TestResults/final-playmode.xml",
    [string]$ReleaseCatalog = "LocalContent/Releases/android/catalog.json",
    [string]$RemoteConfig = "LocalContent/remote-content.json",
    [string]$AndroidReceipt = "LocalContent/FinalAcceptance/android-device.json",
    [string]$PackageId = "com.personal.universalgacha",
    [switch]$RequireComplete,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$requiredDeviceChecks = @(
    "installAndLaunch",
    "touchNavigation",
    "localContentLoad",
    "remoteFirstDownload",
    "interruptedDownloadResume",
    "offlineRestart",
    "wifiMobileSwitch",
    "storageFailureSafety",
    "speakerAudio",
    "audioFocusAndBackgroundResume",
    "haptics",
    "reduceMotion",
    "collectionPreservedAfterReinstall",
    "cloudConflictResolution"
)

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function New-AuditResult {
    param(
        [string]$Name,
        [string]$Scope,
        [bool]$Passed,
        [string]$Detail,
        [bool]$RequiredFor100 = $true
    )
    return [pscustomobject]@{
        Name = $Name
        Scope = $Scope
        Passed = $Passed
        Detail = $Detail
        RequiredFor100 = $RequiredFor100
    }
}

function Test-UnityResultFile {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        return New-AuditResult $Label "Local" $false "Missing test result: $Path"
    }
    try {
        [xml]$document = Get-Content -LiteralPath $Path -Raw -Encoding utf8
        $run = $document.'test-run'
        $passed = $null -ne $run -and $run.result -eq "Passed" -and [int]$run.failed -eq 0
        $detail = if ($null -eq $run) {
            "Missing test-run root."
        }
        else {
            "$($run.passed)/$($run.total) passed; failed=$($run.failed)."
        }
        return New-AuditResult $Label "Local" $passed $detail
    }
    catch {
        return New-AuditResult $Label "Local" $false ("Invalid test XML: " + $_.Exception.Message)
    }
}

function Get-AuthorizedDeviceSerials {
    param([string[]]$Lines)
    return @($Lines | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice(\s|$)" } |
        ForEach-Object { ($_ -split "\s+")[0] })
}

function Resolve-AdbPath {
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe",
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe")
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
    return $null
}

function Invoke-AdbCommand {
    param([string]$AdbPath, [string[]]$Arguments)
    $previousPreference = $ErrorActionPreference
    $output = @()
    $exitCode = -1
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $AdbPath @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    return [pscustomobject]@{ Output = $output; ExitCode = $exitCode }
}

function Invoke-ExternalCommand {
    param([string]$Executable, [string[]]$Arguments)
    $previousPreference = $ErrorActionPreference
    $output = @()
    $exitCode = -1
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

function Resolve-AndroidBuildTool {
    param([string]$Name)
    $roots = @(
        "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools",
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\build-tools")
    )
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }
        $versions = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object `
            @{ Expression = { try { [version]$_.Name } catch { [version]"0.0" } }; Descending = $true })
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName $Name
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }
    return $null
}

function Test-AaptBadgingContract {
    param([string]$Text)
    $nativeMatch = [regex]::Match($Text, "(?m)^native-code:\s*(.+)$")
    $abis = @()
    if ($nativeMatch.Success) {
        $abis = @([regex]::Matches($nativeMatch.Groups[1].Value, "'([^']+)'") |
            ForEach-Object { $_.Groups[1].Value })
    }
    $abiPassed = $abis.Count -eq 1 -and $abis[0] -eq "arm64-v8a"

    $permissions = @([regex]::Matches($Text, "(?m)^uses-permission:\s+name='([^']+)'") |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $allowedPermissions = @(
        "android.permission.INTERNET",
        "android.permission.VIBRATE",
        "$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
    )
    $unexpectedPermissions = @($permissions | Where-Object { $allowedPermissions -notcontains $_ })
    $targetMatch = [regex]::Match($Text, "(?m)^targetSdkVersion:'(\d+)'$")
    $targetSdk = if ($targetMatch.Success) { [int]$targetMatch.Groups[1].Value } else { 0 }
    $permissionsPassed = $unexpectedPermissions.Count -eq 0 -and $targetSdk -ge 34

    return [pscustomobject]@{
        AbiPassed = $abiPassed
        AbiDetail = "native-code=$($abis -join ','); expected arm64-v8a only."
        PermissionsPassed = $permissionsPassed
        PermissionDetail = "targetSdk=$targetSdk; permissions=$($permissions -join ','); unexpected=$($unexpectedPermissions.Count)."
    }
}

function Test-AndroidStaticPackage {
    param([string]$ApkPath)
    $aapt = Resolve-AndroidBuildTool "aapt.exe"
    $apksigner = Resolve-AndroidBuildTool "apksigner.bat"
    $zipalign = Resolve-AndroidBuildTool "zipalign.exe"
    if ($null -eq $aapt -or $null -eq $apksigner -or $null -eq $zipalign) {
        $missing = @(
            if ($null -eq $aapt) { "aapt" }
            if ($null -eq $apksigner) { "apksigner" }
            if ($null -eq $zipalign) { "zipalign" }
        ) -join ", "
        return @(
            (New-AuditResult "APK ARM64 ABI" "Local" $false "Missing Android build tools: $missing."),
            (New-AuditResult "APK permission boundary" "Local" $false "APK metadata cannot be audited."),
            (New-AuditResult "APK signature and alignment" "Local" $false "APK signature cannot be audited.")
        )
    }

    $badging = Invoke-ExternalCommand $aapt @("dump", "badging", $ApkPath)
    if ($badging.ExitCode -ne 0) {
        return @(
            (New-AuditResult "APK ARM64 ABI" "Local" $false "aapt failed with exit code $($badging.ExitCode)."),
            (New-AuditResult "APK permission boundary" "Local" $false "aapt metadata is unavailable."),
            (New-AuditResult "APK signature and alignment" "Local" $false "Static package audit stopped after aapt failure.")
        )
    }

    $contract = Test-AaptBadgingContract ($badging.Output -join "`n")
    $signature = Invoke-ExternalCommand $apksigner @("verify", "--verbose", $ApkPath)
    $alignment = Invoke-ExternalCommand $zipalign @("-c", "-v", "4", $ApkPath)
    return @(
        (New-AuditResult "APK ARM64 ABI" "Local" $contract.AbiPassed $contract.AbiDetail),
        (New-AuditResult "APK permission boundary" "Local" $contract.PermissionsPassed $contract.PermissionDetail),
        (New-AuditResult "APK signature and alignment" "Local" `
            ($signature.ExitCode -eq 0 -and $alignment.ExitCode -eq 0) `
            "apksigner exit=$($signature.ExitCode); zipalign exit=$($alignment.ExitCode).")
    )
}

function Test-RemoteConfigurationJson {
    param([string]$Json)
    try {
        $config = $Json | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $config -or $config -is [Array] -or $config -is [ValueType] -or $config -is [string]) {
            throw "Configuration must contain one JSON object."
        }
        $allowed = @("catalogUrl", "timeoutSeconds", "maxCatalogBytes")
        $unknown = @($config.PSObject.Properties.Name | Where-Object { $allowed -notcontains $_ })
        if ($unknown.Count -gt 0) {
            throw "Unsupported or secret-bearing fields: $($unknown -join ', ')."
        }
        $uri = $null
        if (-not [Uri]::TryCreate([string]$config.catalogUrl, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -ne [Uri]::UriSchemeHttps -or
            -not [string]::IsNullOrEmpty($uri.UserInfo) -or
            -not [string]::IsNullOrEmpty($uri.Fragment)) {
            throw "catalogUrl must be public HTTPS without credentials or a fragment."
        }
        return [pscustomobject]@{ Passed = $true; Detail = "Public catalog: $($uri.AbsoluteUri)" }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Detail = $_.Exception.Message }
    }
}

function Test-AndroidReceiptJson {
    param(
        [string]$Json,
        [string]$ExpectedSerial = "",
        [string]$ExpectedEnvironmentType = ""
    )
    try {
        $receipt = $Json | ConvertFrom-Json -ErrorAction Stop
        if ([int]$receipt.schemaVersion -ne 2) {
            throw "Android acceptance receipt schemaVersion must be 2."
        }
        if ([string]$receipt.packageId -ne $PackageId) {
            throw "Android acceptance receipt packageId does not match $PackageId."
        }
        $environmentType = ([string]$receipt.environmentType).Trim().ToLowerInvariant()
        if ($environmentType -notin @("emulator", "physical")) {
            throw "Android acceptance receipt environmentType must be emulator or physical."
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedEnvironmentType) -and
            $environmentType -ne $ExpectedEnvironmentType.Trim().ToLowerInvariant()) {
            throw "Android acceptance receipt environmentType does not match the connected target."
        }
        foreach ($name in @("serial", "manufacturer", "model", "androidVersion", "apiLevel")) {
            if ([string]::IsNullOrWhiteSpace([string]$receipt.device.$name)) {
                throw "Android acceptance receipt device.$name is required."
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedSerial) -and
            [string]$receipt.device.serial -ne $ExpectedSerial) {
            throw "Android acceptance receipt device serial does not match the connected target."
        }
        $declaredEmulator = $receipt.device.isEmulator -eq $true
        if (($environmentType -eq "emulator") -ne $declaredEmulator) {
            throw "Android acceptance receipt emulator declaration is inconsistent."
        }
        $date = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$receipt.testedAtUtc, [ref]$date)) {
            throw "Android acceptance receipt requires testedAtUtc."
        }
        foreach ($name in $requiredDeviceChecks) {
            $property = $receipt.checks.PSObject.Properties[$name]
            if ($null -eq $property -or $property.Value -ne $true) {
                throw "Android manual acceptance is incomplete: $name."
            }
            $evidence = [string]$receipt.evidence.$name
            if ([string]::IsNullOrWhiteSpace($evidence) -or $evidence.Trim().Length -lt 8) {
                throw "Android acceptance evidence is missing or too short: $name."
            }
        }
        if ($environmentType -eq "emulator") {
            $limitations = @($receipt.limitations | ForEach-Object { [string]$_ })
            foreach ($required in @("physicalHaptics", "physicalSpeakerQuality", "cellularHandover")) {
                if ($limitations -notcontains $required) {
                    throw "Emulator acceptance must record hardware limitation: $required."
                }
            }
        }
        $scope = if ($environmentType -eq "emulator") { "emulator software" } else { "physical-device" }
        return [pscustomobject]@{
            Passed = $true
            Detail = "All $($requiredDeviceChecks.Count) $scope checks recorded with evidence at $date."
            EnvironmentType = $environmentType
        }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Detail = $_.Exception.Message; EnvironmentType = $null }
    }
}

function Test-ReleaseCatalogFiles {
    param([string]$CatalogPath)
    if (-not (Test-Path -LiteralPath $CatalogPath)) {
        return New-AuditResult "Deterministic release fixtures" "Local" $false "Missing catalog: $CatalogPath"
    }
    try {
        $catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ([int]$catalog.schemaVersion -ne 1) {
            throw "Release catalog schemaVersion must be 1."
        }
        $packages = @($catalog.packages)
        $ids = @($packages | ForEach-Object { [string]$_.packageId })
        foreach ($required in @("en.base1", "en.neo1")) {
            if ($ids -notcontains $required) {
                throw "Release catalog is missing $required."
            }
        }
        $catalogRoot = Split-Path -Parent $CatalogPath
        foreach ($package in $packages) {
            $archive = [IO.Path]::GetFullPath((Join-Path $catalogRoot ([string]$package.archiveUrl)))
            if (-not $archive.StartsWith([IO.Path]::GetFullPath($catalogRoot), [StringComparison]::OrdinalIgnoreCase)) {
                throw "Release archive escapes its catalog directory: $($package.packageId)."
            }
            if (-not (Test-Path -LiteralPath $archive)) {
                throw "Release archive is missing: $archive"
            }
            $file = Get-Item -LiteralPath $archive
            if ($file.Length -ne [long]$package.downloadBytes) {
                throw "Release archive length mismatch: $($package.packageId)."
            }
            $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne ([string]$package.sha256).ToLowerInvariant()) {
                throw "Release archive hash mismatch: $($package.packageId)."
            }
        }
        return New-AuditResult "Deterministic release fixtures" "Local" $true "$($packages.Count) archives match catalog size and SHA-256."
    }
    catch {
        return New-AuditResult "Deterministic release fixtures" "Local" $false $_.Exception.Message
    }
}

function Invoke-SelfTest {
    $passed = 0
    $devices = @(Get-AuthorizedDeviceSerials @(
        "List of devices attached",
        "offline-1`toffline usb:1-1",
        "ready-1`tdevice product:test model:phone",
        "unauthorized-1`tunauthorized usb:1-2"
    ))
    if ($devices.Count -ne 1 -or $devices[0] -ne "ready-1") {
        throw "Self-test failed: authorized device filtering."
    }
    $passed++

    $validConfig = Test-RemoteConfigurationJson '{"catalogUrl":"https://content.example.test/releases/catalog.json","timeoutSeconds":15}'
    $secretConfig = Test-RemoteConfigurationJson '{"catalogUrl":"https://content.example.test/catalog.json","secretAccessKey":"no"}'
    if (-not $validConfig.Passed -or $secretConfig.Passed) {
        throw "Self-test failed: remote configuration validation."
    }
    $passed++

    $checks = @{}
    foreach ($name in $requiredDeviceChecks) {
        $checks[$name] = $true
    }
    $evidence = @{}
    foreach ($name in $requiredDeviceChecks) {
        $evidence[$name] = "Verified evidence for $name."
    }
    $receipt = [ordered]@{
        schemaVersion = 2
        packageId = $PackageId
        testedAtUtc = "2026-07-27T00:00:00Z"
        environmentType = "emulator"
        device = [ordered]@{
            serial = "emulator-5554"
            manufacturer = "Google"
            model = "sdk_gphone64_x86_64"
            androidVersion = "14"
            apiLevel = "34"
            isEmulator = $true
        }
        checks = $checks
        evidence = $evidence
        limitations = @("physicalHaptics", "physicalSpeakerQuality", "cellularHandover")
    } | ConvertTo-Json -Depth 4
    if (-not (Test-AndroidReceiptJson -Json $receipt -ExpectedSerial "emulator-5554" `
        -ExpectedEnvironmentType "emulator").Passed) {
        throw "Self-test failed: valid Android receipt."
    }
    $checks.haptics = $false
    $invalidReceipt = [ordered]@{
        schemaVersion = 2
        packageId = $PackageId
        testedAtUtc = "2026-07-27T00:00:00Z"
        environmentType = "emulator"
        device = [ordered]@{
            serial = "emulator-5554"
            manufacturer = "Google"
            model = "sdk_gphone64_x86_64"
            androidVersion = "14"
            apiLevel = "34"
            isEmulator = $true
        }
        checks = $checks
        evidence = $evidence
        limitations = @("physicalHaptics", "physicalSpeakerQuality", "cellularHandover")
    } | ConvertTo-Json -Depth 4
    if ((Test-AndroidReceiptJson $invalidReceipt).Passed) {
        throw "Self-test failed: incomplete Android receipt was accepted."
    }
    $passed++

    $validBadging = @"
targetSdkVersion:'36'
uses-permission: name='android.permission.INTERNET'
uses-permission: name='android.permission.VIBRATE'
uses-permission: name='$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION'
native-code: 'arm64-v8a'
"@
    $invalidAbiBadging = @"
targetSdkVersion:'36'
uses-permission: name='android.permission.INTERNET'
uses-permission: name='android.permission.VIBRATE'
uses-permission: name='$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION'
native-code: 'x86_64'
"@
    $invalidPermissionBadging = @"
targetSdkVersion:'36'
uses-permission: name='android.permission.READ_EXTERNAL_STORAGE'
native-code: 'arm64-v8a'
"@
    $validContract = Test-AaptBadgingContract $validBadging
    $invalidAbiContract = Test-AaptBadgingContract $invalidAbiBadging
    $invalidPermissionContract = Test-AaptBadgingContract $invalidPermissionBadging
    if (-not $validContract.AbiPassed -or -not $validContract.PermissionsPassed -or
        $invalidAbiContract.AbiPassed -or -not $invalidAbiContract.PermissionsPassed -or
        -not $invalidPermissionContract.AbiPassed -or $invalidPermissionContract.PermissionsPassed) {
        throw "Self-test failed: Android APK static contract validation."
    }
    $passed++
    Write-Output "Project completion audit self-test passed: $passed/4."
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$results = New-Object System.Collections.Generic.List[object]
$editPath = Resolve-RepoPath $EditModeResults
$playPath = Resolve-RepoPath $PlayModeResults
$apkFullPath = Resolve-RepoPath $ApkPath
$catalogFullPath = Resolve-RepoPath $ReleaseCatalog
$remoteFullPath = Resolve-RepoPath $RemoteConfig
$receiptFullPath = Resolve-RepoPath $AndroidReceipt

$results.Add((Test-UnityResultFile $editPath "Full EditMode tests"))
$results.Add((Test-UnityResultFile $playPath "Full PlayMode tests"))

if (Test-Path -LiteralPath $apkFullPath) {
    $apk = Get-Item -LiteralPath $apkFullPath
    $runtimeFiles = @(
        Get-ChildItem (Join-Path $repoRoot "Assets\Scripts") -Recurse -File -Filter "*.cs"
        Get-ChildItem (Join-Path $repoRoot "Assets\Resources") -Recurse -File |
            Where-Object { $_.Extension -in @(".asset", ".wav", ".png", ".json") }
        Get-ChildItem (Join-Path $repoRoot "Assets\UI") -Recurse -File |
            Where-Object { $_.Extension -in @(".uxml", ".uss") }
    )
    $latestRuntime = ($runtimeFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    $fresh = $apk.Length -gt 1MB -and $apk.LastWriteTimeUtc -ge $latestRuntime
    $apkDetail = "$($apk.Length) bytes; built=$($apk.LastWriteTimeUtc.ToString('o')); latest-runtime=$($latestRuntime.ToString('o'))."
    $results.Add((New-AuditResult -Name "Fresh Android APK" -Scope "Local" -Passed $fresh -Detail $apkDetail))

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($apkFullPath)
    try {
        $matches = @($archive.Entries | Where-Object {
            $_.FullName -match "remote-content|catalog-cache|pokemon-tcg|en\.base1|en\.neo1|tcgdex|private"
        })
        $privacyDetail = "$($archive.Entries.Count) entries; private-name matches=$($matches.Count)."
        $results.Add((New-AuditResult -Name "APK private-content boundary" -Scope "Local" `
            -Passed ($matches.Count -eq 0) -Detail $privacyDetail))
    }
    finally {
        $archive.Dispose()
    }
}
else {
    $results.Add((New-AuditResult "Fresh Android APK" "Local" $false "Missing APK: $apkFullPath"))
    $results.Add((New-AuditResult "APK private-content boundary" "Local" $false "APK cannot be scanned."))
}

if (Test-Path -LiteralPath $apkFullPath) {
    foreach ($staticResult in @(Test-AndroidStaticPackage $apkFullPath)) {
        $results.Add($staticResult)
    }
}
else {
    $results.Add((New-AuditResult "APK ARM64 ABI" "Local" $false "APK cannot be inspected."))
    $results.Add((New-AuditResult "APK permission boundary" "Local" $false "APK cannot be inspected."))
    $results.Add((New-AuditResult "APK signature and alignment" "Local" $false "APK cannot be inspected."))
}

$audioFiles = @(Get-ChildItem (Join-Path $repoRoot "Assets\Resources\Audio\GachaThemes") -File -Filter "*.wav" -ErrorAction SilentlyContinue)
$audioConfigText = if (Test-Path (Join-Path $repoRoot "Assets\Resources\Data\AudioClipConfig.asset")) {
    Get-Content (Join-Path $repoRoot "Assets\Resources\Data\AudioClipConfig.asset") -Raw
}
else { "" }
$audioKeys = @("vintage", "forest", "ruby", "electric", "gallery")
$audioMapped = $true
foreach ($theme in $audioKeys) {
    if ($audioConfigText -notmatch "pack\.open\.$theme" -or $audioConfigText -notmatch "card\.rare\.$theme") {
        $audioMapped = $false
    }
}
$audioDetail = "$($audioFiles.Count)/10 WAV files; config mapping=$audioMapped."
$results.Add((New-AuditResult -Name "Baked era audio" -Scope "Local" `
    -Passed ($audioFiles.Count -eq 10 -and $audioMapped) -Detail $audioDetail))
$results.Add((Test-ReleaseCatalogFiles $catalogFullPath))

$r2Names = @(
    "GACHA_R2_S3_ENDPOINT",
    "GACHA_R2_BUCKET",
    "GACHA_R2_PUBLIC_BASE_URL",
    "GACHA_R2_ACCESS_KEY_ID",
    "GACHA_R2_SECRET_ACCESS_KEY"
)
$missingR2 = @($r2Names | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
$r2Detail = if ($missingR2.Count -eq 0) {
    "All required variables are present; values were not printed."
}
else {
    "Missing: $($missingR2 -join ', ')."
}
$results.Add((New-AuditResult -Name "R2 publisher prerequisites" -Scope "Prerequisite" `
    -Passed ($missingR2.Count -eq 0) -Detail $r2Detail -RequiredFor100 $false))

if (Test-Path -LiteralPath $remoteFullPath) {
    $remote = Test-RemoteConfigurationJson (Get-Content -LiteralPath $remoteFullPath -Raw -Encoding utf8)
    $results.Add((New-AuditResult "Verified remote runtime config" "Remote" $remote.Passed $remote.Detail))
}
else {
    $remoteDetail = "Missing $remoteFullPath; create it only after the Site or R2 catalog passes public HTTPS verification."
    $results.Add((New-AuditResult -Name "Verified remote runtime config" -Scope "Remote" `
        -Passed $false -Detail $remoteDetail))
}

$adb = Resolve-AdbPath
$serials = @()
if ($null -ne $adb) {
    $deviceQuery = Invoke-AdbCommand $adb @("devices", "-l")
    if ($deviceQuery.ExitCode -eq 0) {
        $serials = @(Get-AuthorizedDeviceSerials $deviceQuery.Output)
    }
}
$connectedEnvironmentType = ""
if ($serials.Count -eq 1) {
    $qemuQuery = Invoke-AdbCommand $adb @("-s", $serials[0], "shell", "getprop", "ro.kernel.qemu")
    if ($qemuQuery.ExitCode -eq 0 -and ($qemuQuery.Output -join "").Trim() -eq "1") {
        $connectedEnvironmentType = "emulator"
    }
    else {
        $connectedEnvironmentType = "physical"
    }
}
$deviceDetail = if ($null -eq $adb) {
    "ADB was not found."
}
elseif ($serials.Count -eq 1) {
    "Authorized target=$($serials[0]); environment=$connectedEnvironmentType."
}
else {
    "Authorized targets=$($serials.Count)."
}
$results.Add((New-AuditResult -Name "One authorized Android target" -Scope "Device" `
    -Passed ($serials.Count -eq 1) -Detail $deviceDetail))

$installed = $false
$deviceConfig = $false
if ($serials.Count -eq 1) {
    $serial = $serials[0]
    $packageQuery = Invoke-AdbCommand $adb @("-s", $serial, "shell", "pm", "path", $PackageId)
    $installed = $packageQuery.ExitCode -eq 0 -and ($packageQuery.Output -join "`n") -match "package:"
    $configQuery = Invoke-AdbCommand $adb @(
        "-s", $serial, "shell", "ls", "/sdcard/Android/data/$PackageId/files/remote-content.json")
    $deviceConfig = $configQuery.ExitCode -eq 0 -and ($configQuery.Output -join "`n") -match "remote-content\.json"
}
$installedDetail = if ($installed) { "Package $PackageId is installed." } else { "Package is not verified on an authorized device." }
$results.Add((New-AuditResult -Name "APK installed on device" -Scope "Device" `
    -Passed $installed -Detail $installedDetail))
$deviceConfigDetail = if ($deviceConfig) { "remote-content.json is present in app storage." } else { "Remote config is not verified in app storage." }
$results.Add((New-AuditResult -Name "Remote config installed on device" -Scope "Device" `
    -Passed $deviceConfig -Detail $deviceConfigDetail))

if (Test-Path -LiteralPath $receiptFullPath) {
    $receipt = Test-AndroidReceiptJson `
        -Json (Get-Content -LiteralPath $receiptFullPath -Raw -Encoding utf8) `
        -ExpectedSerial $(if ($serials.Count -eq 1) { $serials[0] } else { "" }) `
        -ExpectedEnvironmentType $connectedEnvironmentType
    $results.Add((New-AuditResult "Android manual acceptance receipt" "Device" $receipt.Passed $receipt.Detail))
}
else {
    $receiptDetail = "Missing $receiptFullPath; record all $($requiredDeviceChecks.Count) Android target checks after testing."
    $results.Add((New-AuditResult -Name "Android manual acceptance receipt" -Scope "Device" `
        -Passed $false -Detail $receiptDetail))
}

foreach ($result in $results) {
    $state = if ($result.Passed) { "PASS" } elseif ($result.RequiredFor100) { "BLOCKED" } else { "WAIT" }
    Write-Output "[$state][$($result.Scope)] $($result.Name) - $($result.Detail)"
}

$localRequired = @($results | Where-Object { $_.Scope -eq "Local" -and $_.RequiredFor100 })
$localPassed = @($localRequired | Where-Object { $_.Passed }).Count
$localPercent = if ($localRequired.Count -eq 0) { 0 } else { [Math]::Floor(92 * $localPassed / $localRequired.Count) }
$remotePassed = @($results | Where-Object { $_.Scope -eq "Remote" -and $_.RequiredFor100 -and $_.Passed }).Count -eq
    @($results | Where-Object { $_.Scope -eq "Remote" -and $_.RequiredFor100 }).Count
$devicePassed = @($results | Where-Object { $_.Scope -eq "Device" -and $_.RequiredFor100 -and $_.Passed }).Count -eq
    @($results | Where-Object { $_.Scope -eq "Device" -and $_.RequiredFor100 }).Count
$percent = $localPercent
if ($remotePassed) { $percent += 4 }
if ($devicePassed) { $percent += 4 }
$complete = $percent -eq 100 -and @($results | Where-Object { $_.RequiredFor100 -and -not $_.Passed }).Count -eq 0
Write-Output "Completion audit: $percent% (local ceiling 92%, verified remote HTTPS release +4%, verified Android acceptance +4%)."

if ($complete) {
    Write-Output "PROJECT COMPLETION VERIFIED: 100%."
    exit 0
}
if ($RequireComplete) {
    exit 2
}
exit 0
