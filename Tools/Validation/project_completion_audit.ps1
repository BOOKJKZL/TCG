param(
    [string]$UnityVersion = "6000.0.73f1",
    [string]$ApkPath = "Builds/Android/UniversalGachaSimulator-smoke.apk",
    [string]$EditModeResults = "TestResults/final-editmode.xml",
    [string]$PlayModeResults = "TestResults/final-playmode.xml",
    [string]$ReleaseCatalog = "LocalContent/Releases/android-complete/catalog.json",
    [string]$RemoteConfig = "LocalContent/remote-content.json",
    [string]$RemoteAuditReport = "LocalContent/Releases/android-complete/remote-release-audit.json",
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
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.INTERNET",
        "android.permission.VIBRATE",
        "$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
    )
    $unexpectedPermissions = @($permissions | Where-Object { $allowedPermissions -notcontains $_ })
    $targetMatch = [regex]::Match($Text, "(?m)^targetSdkVersion:'(\d+)'[ \t\r]*$")
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
        $allowed = @("catalogUrl", "timeoutSeconds", "maxCatalogBytes", "trustedCatalogKeys")
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
        $trustedKeyCount = 0
        if ($null -ne $config.PSObject.Properties["trustedCatalogKeys"]) {
            if ($config.trustedCatalogKeys -isnot [Array]) {
                throw "trustedCatalogKeys must be an array."
            }
            $keyIds = @{}
            foreach ($key in @($config.trustedCatalogKeys)) {
                if ($null -eq $key -or $key -is [Array] -or $key -is [ValueType] -or $key -is [string]) {
                    throw "trustedCatalogKeys entries must be objects."
                }
                $keyFields = @($key.PSObject.Properties.Name)
                $unexpectedKeyFields = @($keyFields | Where-Object {
                    $_ -notin @("keyId", "subjectPublicKeyInfoBase64")
                })
                if ($unexpectedKeyFields.Count -gt 0 -or
                    $keyFields -notcontains "keyId" -or
                    $keyFields -notcontains "subjectPublicKeyInfoBase64") {
                    throw "trustedCatalogKeys entries must contain only keyId and subjectPublicKeyInfoBase64."
                }
                $keyId = [string]$key.keyId
                if ($keyId -notmatch '^[A-Za-z0-9._-]{1,64}$' -or $keyIds.ContainsKey($keyId)) {
                    throw "trustedCatalogKeys contains an invalid or duplicate keyId."
                }
                $keyIds[$keyId] = $true
                try {
                    $keyBytes = [Convert]::FromBase64String([string]$key.subjectPublicKeyInfoBase64)
                }
                catch {
                    throw "trustedCatalogKeys public keys must be Base64 SubjectPublicKeyInfo."
                }
                if ($keyBytes.Length -lt 256 -or $keyBytes.Length -gt 1024) {
                    throw "trustedCatalogKeys public key length is outside the accepted range."
                }
                $trustedKeyCount++
            }
        }
        return [pscustomobject]@{
            Passed = $true
            Detail = "Public catalog: $($uri.AbsoluteUri); trusted catalog keys=$trustedKeyCount."
        }
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
        $testedAt = $receipt.testedAtUtc
        $date = [DateTimeOffset]::MinValue
        if ($testedAt -is [DateTimeOffset]) {
            $date = $testedAt
        }
        elseif ($testedAt -is [DateTime]) {
            $date = [DateTimeOffset]::new([DateTime]$testedAt)
        }
        elseif (-not [DateTimeOffset]::TryParse(
            [string]$testedAt,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$date)) {
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
    param(
        [string]$CatalogPath,
        [int]$ExpectedPackageCount = 538
    )
    if (-not (Test-Path -LiteralPath $CatalogPath)) {
        return New-AuditResult "Deterministic release fixtures" "Local" $false "Missing catalog: $CatalogPath"
    }
    try {
        $catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ([int]$catalog.schemaVersion -ne 2 -or [int]$catalog.revision -lt 6) {
            throw "Release catalog must be schemaVersion 2 at revision 6 or newer."
        }
        $packages = @($catalog.packages)
        if ($packages.Count -ne $ExpectedPackageCount) {
            throw "Release catalog package count is $($packages.Count); expected $ExpectedPackageCount."
        }
        $ids = @($packages | ForEach-Object { [string]$_.packageId })
        if (@($ids | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
            @($ids | Sort-Object -Unique).Count -ne $ids.Count) {
            throw "Release catalog package IDs must be non-empty and unique."
        }
        if ($ExpectedPackageCount -eq 538) {
            foreach ($required in @(
                "en.base1",
                "en.neo1",
                "pokemon.pokedex.taxonomy",
                "pokemon.printing-language-groups")) {
                if ($ids -notcontains $required) {
                    throw "Release catalog is missing $required."
                }
            }
        }
        $catalogRoot = Split-Path -Parent $CatalogPath
        $catalogRootPrefix = [IO.Path]::GetFullPath($catalogRoot).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($package in $packages) {
            if ($null -eq $package.metadata -or
                [string]::IsNullOrWhiteSpace([string]$package.metadata.kind) -or
                [string]::IsNullOrWhiteSpace([string]$package.metadata.gameId)) {
                throw "Release package metadata is incomplete: $($package.packageId)."
            }
            $archive = [IO.Path]::GetFullPath((Join-Path $catalogRoot ([string]$package.archiveUrl)))
            if (-not $archive.StartsWith($catalogRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
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
        return New-AuditResult "Deterministic release fixtures" "Local" $true `
            "schema=$($catalog.schemaVersion), revision=$($catalog.revision); $($packages.Count) archives match size and SHA-256."
    }
    catch {
        return New-AuditResult "Deterministic release fixtures" "Local" $false $_.Exception.Message
    }
}

function Test-RemoteReleaseEvidence {
    param(
        [string]$RemoteConfigPath,
        [string]$ReportPath,
        [string]$CatalogPath
    )
    try {
        foreach ($requiredPath in @($RemoteConfigPath, $ReportPath, $CatalogPath)) {
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                throw "Required remote evidence is missing: $requiredPath"
            }
        }

        $configJson = Get-Content -LiteralPath $RemoteConfigPath -Raw -Encoding utf8
        $configContract = Test-RemoteConfigurationJson $configJson
        if (-not $configContract.Passed) {
            throw $configContract.Detail
        }
        $config = $configJson | ConvertFrom-Json
        $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding utf8 | ConvertFrom-Json
        $catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding utf8 | ConvertFrom-Json
        $packageCount = @($catalog.packages).Count
        $catalogHash = (Get-FileHash -LiteralPath $CatalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $configuredUri = [Uri]::new([string]$config.catalogUrl, [UriKind]::Absolute).AbsoluteUri
        $reportedUri = [Uri]::new([string]$report.catalogUrl, [UriKind]::Absolute).AbsoluteUri

        if ([int]$report.schemaVersion -ne 1 -or $report.valid -ne $true) {
            throw "Remote audit report is not a valid schemaVersion 1 receipt."
        }
        if ($reportedUri -ne $configuredUri) {
            throw "Remote audit URL does not match the runtime configuration."
        }
        if ([string]$report.catalogSha256 -ne $catalogHash) {
            throw "Remote audit Catalog SHA-256 does not match the current local release."
        }
        if ([int]$report.packageCount -ne $packageCount -or
            [int]$report.headPassed -ne $packageCount -or
            [int]$report.rangePassed -ne $packageCount) {
            throw "Remote audit does not cover all $packageCount packages with HEAD and Range."
        }
        if ([int]$report.writeMethodsRejected -ne 8 -or $report.authorizationHeaderUsed -ne $false) {
            throw "Remote audit must reject all 8 anonymous writes without an authorization header."
        }

        return New-AuditResult "Verified remote HTTPS release" "Remote" $true `
            "$packageCount/$packageCount HEAD and Range; current Catalog SHA-256; 8/8 writes rejected."
    }
    catch {
        return New-AuditResult "Verified remote HTTPS release" "Remote" $false $_.Exception.Message
    }
}

function Test-ProtectedCatalogDeclaration {
    param([string]$ConfigPath, [string]$CatalogPath)
    if (-not (Test-Path -LiteralPath $ConfigPath) -or -not (Test-Path -LiteralPath $CatalogPath)) {
        return New-AuditResult "Protected Catalog v3 contract" "Remote" $false `
            "Runtime config or release Catalog is missing."
    }
    try {
        $configJson = Get-Content -LiteralPath $ConfigPath -Raw -Encoding utf8
        $configContract = Test-RemoteConfigurationJson $configJson
        if (-not $configContract.Passed) {
            throw $configContract.Detail
        }
        $config = $configJson | ConvertFrom-Json
        $catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ([int]$catalog.schemaVersion -ne 3) {
            throw "Current Catalog schemaVersion is $($catalog.schemaVersion); protected hot-update requires v3."
        }
        if ([string]$catalog.minAppVersion -notmatch `
            '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$' -or
            [int]$catalog.contentSchemaVersion -lt 1 -or
            [int]$catalog.ruleSchemaVersion -lt 1) {
            throw "Catalog v3 compatibility fields are invalid."
        }
        if ([string]$catalog.signature.algorithm -ne "RS256" -or
            [string]$catalog.signature.keyId -notmatch '^[A-Za-z0-9._-]{1,64}$') {
            throw "Catalog v3 signature declaration is invalid."
        }
        try {
            $signatureBytes = [Convert]::FromBase64String([string]$catalog.signature.value)
        }
        catch {
            throw "Catalog v3 signature value is not Base64."
        }
        if ($signatureBytes.Length -lt 128 -or $signatureBytes.Length -gt 1024) {
            throw "Catalog v3 signature length is invalid."
        }
        $matchingKeys = @($config.trustedCatalogKeys | Where-Object {
            [string]$_.keyId -eq [string]$catalog.signature.keyId
        })
        if ($matchingKeys.Count -ne 1) {
            throw "Runtime config does not contain exactly one trusted public key for the Catalog keyId."
        }
        return New-AuditResult "Protected Catalog v3 contract" "Remote" $true `
            "v3 compatibility fields, RS256 declaration, and matching runtime trust key are present; device acceptance performs cryptographic consumption."
    }
    catch {
        return New-AuditResult "Protected Catalog v3 contract" "Remote" $false $_.Exception.Message
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
    $fixturePublicKey = [Convert]::ToBase64String([byte[]](0..293 | ForEach-Object { $_ % 251 }))
    $protectedConfigJson = [ordered]@{
        catalogUrl = "https://content.example.test/releases/catalog.json"
        trustedCatalogKeys = @([ordered]@{
            keyId = "fixture-2026"
            subjectPublicKeyInfoBase64 = $fixturePublicKey
        })
    } | ConvertTo-Json -Depth 4
    $protectedConfig = Test-RemoteConfigurationJson $protectedConfigJson
    if (-not $validConfig.Passed -or -not $protectedConfig.Passed -or $secretConfig.Passed) {
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
    $validReceipt = Test-AndroidReceiptJson -Json $receipt -ExpectedSerial "emulator-5554" `
        -ExpectedEnvironmentType "emulator"
    if (-not $validReceipt.Passed) {
        throw "Self-test failed: valid Android receipt. $($validReceipt.Detail)"
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
uses-permission: name='android.permission.ACCESS_NETWORK_STATE'
uses-permission: name='android.permission.INTERNET'
uses-permission: name='android.permission.VIBRATE'
uses-permission: name='$PackageId.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION'
native-code: 'arm64-v8a'
"@
    $invalidAbiBadging = @"
targetSdkVersion:'36'
uses-permission: name='android.permission.ACCESS_NETWORK_STATE'
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
    $validLfContract = Test-AaptBadgingContract ($validBadging -replace "`r`n", "`n")
    $pollutedTargetContract = Test-AaptBadgingContract `
        ($validBadging -replace "targetSdkVersion:'36'", "targetSdkVersion:'36'garbage")
    $invalidAbiContract = Test-AaptBadgingContract $invalidAbiBadging
    $invalidPermissionContract = Test-AaptBadgingContract $invalidPermissionBadging
    if (-not $validContract.AbiPassed -or -not $validContract.PermissionsPassed -or
        -not $validLfContract.AbiPassed -or -not $validLfContract.PermissionsPassed -or
        -not $pollutedTargetContract.AbiPassed -or $pollutedTargetContract.PermissionsPassed -or
        $invalidAbiContract.AbiPassed -or -not $invalidAbiContract.PermissionsPassed -or
        -not $invalidPermissionContract.AbiPassed -or $invalidPermissionContract.PermissionsPassed) {
        throw "Self-test failed: Android APK static contract validation."
    }
    $passed++

    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("gacha-audit-" + [Guid]::NewGuid().ToString("N"))
    try {
        $packageDirectory = Join-Path $fixtureRoot "packages/test"
        [IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
        $archivePath = Join-Path $packageDirectory "fixture.zip"
        [IO.File]::WriteAllBytes($archivePath, [byte[]](1, 2, 3, 4))
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $catalogPath = Join-Path $fixtureRoot "catalog.json"
        $catalogJson = [ordered]@{
            schemaVersion = 2
            revision = 6
            packages = @([ordered]@{
                packageId = "test.fixture"
                archiveUrl = "packages/test/fixture.zip"
                downloadBytes = 4
                sha256 = $archiveHash
                metadata = [ordered]@{ kind = "test"; gameId = "test-game" }
            })
        } | ConvertTo-Json -Depth 6
        [IO.File]::WriteAllText($catalogPath, $catalogJson, [Text.UTF8Encoding]::new($false))
        $catalogHash = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $configPath = Join-Path $fixtureRoot "remote-content.json"
        [IO.File]::WriteAllText(
            $configPath,
            '{"catalogUrl":"https://content.example.test/catalog.json"}',
            [Text.UTF8Encoding]::new($false))
        $reportPath = Join-Path $fixtureRoot "remote-release-audit.json"
        $reportJson = [ordered]@{
            schemaVersion = 1
            catalogUrl = "https://content.example.test/catalog.json"
            catalogSha256 = $catalogHash
            packageCount = 1
            headPassed = 1
            rangePassed = 1
            writeMethodsRejected = 8
            authorizationHeaderUsed = $false
            valid = $true
        } | ConvertTo-Json
        [IO.File]::WriteAllText($reportPath, $reportJson, [Text.UTF8Encoding]::new($false))

        if (-not (Test-ReleaseCatalogFiles -CatalogPath $catalogPath -ExpectedPackageCount 1).Passed -or
            -not (Test-RemoteReleaseEvidence $configPath $reportPath $catalogPath).Passed) {
            throw "Self-test failed: current release and remote evidence were rejected."
        }
        $badReport = $reportJson | ConvertFrom-Json
        $badReport.catalogSha256 = ("0" * 64)
        [IO.File]::WriteAllText(
            $reportPath,
            ($badReport | ConvertTo-Json),
            [Text.UTF8Encoding]::new($false))
        if ((Test-RemoteReleaseEvidence $configPath $reportPath $catalogPath).Passed) {
            throw "Self-test failed: stale remote evidence was accepted."
        }
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
    $passed++
    Write-Output "Project completion audit self-test passed: $passed/5."
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
$remoteAuditFullPath = Resolve-RepoPath $RemoteAuditReport
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

$results.Add((Test-RemoteReleaseEvidence $remoteFullPath $remoteAuditFullPath $catalogFullPath))
$results.Add((Test-ProtectedCatalogDeclaration $remoteFullPath $catalogFullPath))

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
