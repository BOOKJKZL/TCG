[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "../.."),
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe",
    [string]$AdbPath,
    [Parameter(Mandatory = $true)]
    [string]$Serial,
    [string]$EvidenceDirectory,
    [string]$BuildLogPath,
    [ValidateRange(1, 10)]
    [int]$StartupSamples = 3,
    [ValidateRange(1, 60)]
    [int]$RenderWaitSeconds = 8,
    [switch]$SkipBuild,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageId = "com.personal.universalgacha"
$activityName = "com.unity3d.player.UnityPlayerGameActivity"
$artifactRelativePath = "Builds/Android/UniversalGachaSimulator-emulator-x86_64.apk"

function Assert-Condition {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-NativeText {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$FailureMessage = "Native command failed"
    )

    $output = & $FilePath @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FailureMessage (exit=$exitCode). $($output.Trim())"
    }
    return $output.TrimEnd()
}

function Get-AdbTargets {
    param([string]$DevicesOutput)

    $targets = @()
    foreach ($line in ($DevicesOutput -split "`r?`n")) {
        if ($line -match '^([^\s]+)\s+(device|offline|unauthorized)(?:\s|$)') {
            $targets += [pscustomobject]@{
                serial = $Matches[1]
                state = $Matches[2]
            }
        }
    }
    return @($targets)
}

function Get-FirstMatch {
    param([string]$Text, [string]$Pattern, [string]$Name)

    $match = [regex]::Match($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        throw "Could not parse $Name."
    }
    return $match.Groups[1].Value
}

function Get-OptionalInt {
    param([string]$Text, [string]$Pattern)

    $match = [regex]::Match($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        return $null
    }
    return [int64]$match.Groups[1].Value
}

function Resolve-AdbPath {
    param([string]$RequestedPath, [string]$RequestedUnityPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
        $candidates += (Join-Path $env:ANDROID_SDK_ROOT "platform-tools/adb.exe")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        $candidates += (Join-Path $env:ANDROID_HOME "platform-tools/adb.exe")
    }
    $candidates += (Join-Path $env:LOCALAPPDATA "Android/Sdk/platform-tools/adb.exe")
    if (-not [string]::IsNullOrWhiteSpace($RequestedUnityPath)) {
        $editorRoot = Split-Path (Split-Path $RequestedUnityPath -Parent) -Parent
        $candidates += (Join-Path $editorRoot "Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe")
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    $command = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }
    throw "adb.exe was not found. Pass -AdbPath explicitly."
}

function Resolve-AaptPath {
    param([string]$RequestedUnityPath)

    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
        $roots += $env:ANDROID_SDK_ROOT
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
        $roots += $env:ANDROID_HOME
    }
    $roots += (Join-Path $env:LOCALAPPDATA "Android/Sdk")
    if (-not [string]::IsNullOrWhiteSpace($RequestedUnityPath)) {
        $editorRoot = Split-Path (Split-Path $RequestedUnityPath -Parent) -Parent
        $roots += (Join-Path $editorRoot "Data/PlaybackEngines/AndroidPlayer/SDK")
    }

    $matches = @()
    foreach ($root in $roots | Select-Object -Unique) {
        if (Test-Path -LiteralPath $root -PathType Container) {
            $matches += Get-ChildItem -LiteralPath (Join-Path $root "build-tools") -Filter aapt.exe `
                -Recurse -File -ErrorAction SilentlyContinue
        }
    }
    $selected = $matches | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $selected) {
        throw "aapt.exe was not found in the Android SDK build-tools directories."
    }
    return $selected.FullName
}

function Invoke-Adb {
    param(
        [string]$ResolvedAdbPath,
        [string]$TargetSerial,
        [string[]]$Arguments,
        [string]$FailureMessage = "ADB command failed"
    )

    return Invoke-NativeText -FilePath $ResolvedAdbPath `
        -Arguments (@("-s", $TargetSerial) + $Arguments) -FailureMessage $FailureMessage
}

function Wait-AdbTargetReady {
    param(
        [string]$ResolvedAdbPath,
        [string]$TargetSerial,
        [ValidateRange(1, 180)][int]$TimeoutSeconds = 60
    )

    Invoke-NativeText -FilePath $ResolvedAdbPath -Arguments @("start-server") `
        -FailureMessage "Could not start the ADB server" | Out-Null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $devicesText = Invoke-NativeText -FilePath $ResolvedAdbPath -Arguments @("devices", "-l") `
                -FailureMessage "Could not enumerate ADB targets after Unity build"
            $target = @(Get-AdbTargets $devicesText | Where-Object serial -eq $TargetSerial)
            if ($target.Count -eq 1 -and $target[0].state -eq "device") {
                $boot = Invoke-Adb $ResolvedAdbPath $TargetSerial @("shell", "getprop", "sys.boot_completed")
                if ($boot.Trim() -eq "1") {
                    return
                }
            }
        }
        catch {
            # Unity stops its bundled ADB daemon during batchmode shutdown.
            # A transient offline/daemon restart is expected; the deadline
            # still fails closed if the same target does not recover.
        }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "ADB target '$TargetSerial' did not return to online/boot-complete state within $TimeoutSeconds seconds."
}

function Get-UiHierarchyXml {
    param([string]$ResolvedAdbPath, [string]$TargetSerial)

    $remotePath = "/data/local/tmp/universal-gacha-preflight-window.xml"
    Invoke-Adb $ResolvedAdbPath $TargetSerial @("shell", "uiautomator", "dump", $remotePath) `
        "Could not dump the Android window hierarchy" | Out-Null
    try {
        return Invoke-Adb $ResolvedAdbPath $TargetSerial @("shell", "cat", $remotePath) `
            "Could not read the Android window hierarchy"
    }
    finally {
        Invoke-Adb $ResolvedAdbPath $TargetSerial @("shell", "rm", $remotePath) | Out-Null
    }
}

function Get-BoundsCenter {
    param([string]$Bounds)

    $match = [regex]::Match($Bounds, '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$')
    if (-not $match.Success) {
        throw "Could not parse Android UI bounds '$Bounds'."
    }
    return [pscustomobject]@{
        x = [int](($match.Groups[1].Value -as [int]) + ($match.Groups[3].Value -as [int])) / 2
        y = [int](($match.Groups[2].Value -as [int]) + ($match.Groups[4].Value -as [int])) / 2
    }
}

function Resolve-KnownSystemUiAnr {
    param(
        [string]$ResolvedAdbPath,
        [string]$TargetSerial,
        [ValidateRange(0, 3)][int]$MaximumRecoveries = 2
    )

    $recoveries = 0
    $finalHierarchy = $null
    while ($true) {
        $hierarchyText = Get-UiHierarchyXml $ResolvedAdbPath $TargetSerial
        $finalHierarchy = $hierarchyText
        [xml]$hierarchy = $hierarchyText
        $nodes = @($hierarchy.SelectNodes("//node"))
        $waitNode = $nodes | Where-Object { $_.GetAttribute("resource-id") -eq "android:id/aerr_wait" } |
            Select-Object -First 1
        if ($null -eq $waitNode) {
            break
        }

        $titleNode = $nodes | Where-Object { $_.GetAttribute("resource-id") -eq "android:id/alertTitle" } |
            Select-Object -First 1
        $title = if ($null -eq $titleNode) { "<missing ANR title>" } else { $titleNode.GetAttribute("text") }
        if ($title -notmatch '(?i)^System UI\b') {
            throw "An application-not-responding dialog is visible: '$title'."
        }
        if ($recoveries -ge $MaximumRecoveries) {
            throw "System UI ANR remained visible after $recoveries controlled Wait recoveries."
        }

        $center = Get-BoundsCenter -Bounds ($waitNode.GetAttribute("bounds"))
        Invoke-Adb $ResolvedAdbPath $TargetSerial @("shell", "input", "tap", "$($center.x)", "$($center.y)") `
            "Could not select Wait on the known System UI ANR dialog" | Out-Null
        $recoveries++
        Start-Sleep -Seconds 10
    }

    return [pscustomobject]@{
        recoveries = $recoveries
        hierarchy = $finalHierarchy
    }
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)

    $parent = Split-Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Get-FileEvidence {
    param([string]$Path)

    $item = Get-Item -LiteralPath $Path
    $hash = $null
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $hash = Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256
            break
        }
        catch [IO.IOException] {
            if ($attempt -eq 10) {
                throw
            }
            Start-Sleep -Milliseconds 500
        }
    }
    return [ordered]@{
        file = $item.Name
        bytes = $item.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    }
}

function Invoke-SelfTest {
    $targets = Get-AdbTargets @"
List of devices attached
emulator-5554 device product:sdk_gphone64_x86_64 model:sdk_gphone64_x86_64
phone-1 unauthorized usb:1-1
"@
    Assert-Condition ($targets.Count -eq 2) "ADB target parser self-test failed."
    Assert-Condition ($targets[0].serial -eq "emulator-5554") "ADB serial parser self-test failed."
    Assert-Condition ($targets[1].state -eq "unauthorized") "ADB state parser self-test failed."

    $totalTime = Get-OptionalInt "Status: ok`nTotalTime: 431`nWaitTime: 438" '^TotalTime:\s*(\d+)\s*$'
    Assert-Condition ($totalTime -eq 431) "Android start metric parser self-test failed."

    $badging = "package: name='com.personal.universalgacha' versionCode='1' versionName='0.1.0'`nnative-code: 'x86_64'"
    Assert-Condition ((Get-FirstMatch $badging "package: name='([^']+)'" "package") -eq $packageId) `
        "APK package parser self-test failed."
    Assert-Condition ((Get-FirstMatch $badging "native-code: '([^']+)'" "native ABI") -eq "x86_64") `
        "APK ABI parser self-test failed."

    [pscustomobject]@{
        status = "passed"
        checks = 6
        productionReleaseChanged = $false
        releaseGateEligible = $false
    } | ConvertTo-Json -Depth 4
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
Assert-Condition (Test-Path -LiteralPath (Join-Path $resolvedProjectPath "Assets") -PathType Container) `
    "ProjectPath is not a Unity project."
Assert-Condition (Test-Path -LiteralPath $UnityPath -PathType Leaf) "Unity.exe was not found at '$UnityPath'."

$resolvedAdbPath = Resolve-AdbPath -RequestedPath $AdbPath -RequestedUnityPath $UnityPath
$resolvedAaptPath = Resolve-AaptPath -RequestedUnityPath $UnityPath
$artifactPath = Join-Path $resolvedProjectPath $artifactRelativePath
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $resolvedProjectPath "TestResults/EmulatorPreflight/$timestamp"
}
$resolvedEvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force -Path $resolvedEvidenceDirectory | Out-Null

# Addressables may materialize these build-only files. Only remove a path when
# this run created it from an absent baseline; pre-existing user files remain
# untouched even if they have the same name.
$knownGeneratedRelativePaths = @(
    "Assets/AddressableAssetsData/Windows.meta",
    "Assets/AddressableAssetsData/link.xml",
    "Assets/AddressableAssetsData/link.xml.meta"
)
$generatedPathExistedBefore = [ordered]@{}
foreach ($relativePath in $knownGeneratedRelativePaths) {
    $generatedPathExistedBefore[$relativePath] = Test-Path -LiteralPath (Join-Path $resolvedProjectPath $relativePath)
}

$receiptPath = Join-Path $resolvedEvidenceDirectory "emulator-preflight-receipt.json"
$unityLogPath = if ([string]::IsNullOrWhiteSpace($BuildLogPath)) {
    Join-Path $resolvedProjectPath "Logs/emulator-preflight-$timestamp.log"
}
else {
    [IO.Path]::GetFullPath($BuildLogPath)
}
$resultStatus = "failed"
$failureMessage = $null
$evidenceFiles = @()
$launchSamples = @()
$sourceCommit = $null
$sourceContentDiff = $null
$sourceStatusEntries = @()
$sourcePostBuildContentDiff = $null
$sourcePostBuildStatusEntries = @()
$cleanedGeneratedFiles = @()
$artifactEvidence = $null
$deviceEvidence = $null
$metrics = [ordered]@{}
$checks = [ordered]@{
    selectedTargetIsOnlyOnlineTarget = $false
    emulatorIdentity = $false
    x86_64Target = $false
    bootCompleted = $false
    apkIdentity = $false
    upgradeInstallPreservedData = $false
    packageIdentity = $false
    postBuildDeviceReady = $false
    applicationForeground = $false
    screenshotCaptured = $false
    systemUiDialogClear = $false
    fatalLogScan = $false
}

try {
    $sourceCommit = Invoke-NativeText -FilePath "git.exe" `
        -Arguments @("-C", $resolvedProjectPath, "rev-parse", "HEAD") -FailureMessage "Could not resolve source commit"
    & git.exe -C $resolvedProjectPath diff --quiet --
    $sourceContentDiff = ($LASTEXITCODE -ne 0)
    $sourceStatusText = Invoke-NativeText -FilePath "git.exe" `
        -Arguments @("-C", $resolvedProjectPath, "status", "--porcelain=v2") -FailureMessage "Could not inspect source status"
    $sourceStatusEntries = @($sourceStatusText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $devicesText = Invoke-NativeText -FilePath $resolvedAdbPath -Arguments @("devices", "-l") `
        -FailureMessage "Could not enumerate ADB targets"
    $targets = Get-AdbTargets $devicesText
    $onlineTargets = @($targets | Where-Object state -eq "device")
    Assert-Condition ($onlineTargets.Count -eq 1) `
        "Exactly one online ADB target is required; found $($onlineTargets.Count)."
    Assert-Condition ($onlineTargets[0].serial -eq $Serial) `
        "The only online ADB target '$($onlineTargets[0].serial)' does not match requested serial '$Serial'."
    $checks.selectedTargetIsOnlyOnlineTarget = $true

    $qemu = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.kernel.qemu")).Trim()
    $abiList = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.product.cpu.abilist")).Trim()
    $bootCompleted = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "sys.boot_completed")).Trim()
    Assert-Condition ($qemu -eq "1") "Selected target is not an Android emulator (ro.kernel.qemu != 1)."
    Assert-Condition (($abiList -split ',') -contains "x86_64") "Selected emulator does not advertise x86_64 ABI."
    Assert-Condition ($bootCompleted -eq "1") "Selected emulator has not completed boot."
    $checks.emulatorIdentity = $true
    $checks.x86_64Target = $true
    $checks.bootCompleted = $true

    $deviceEvidence = [ordered]@{
        serial = $Serial
        manufacturer = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.product.manufacturer")).Trim()
        model = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.product.model")).Trim()
        androidVersion = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.build.version.release")).Trim()
        apiLevel = [int](Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.build.version.sdk")).Trim()
        abiList = $abiList
        hardware = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.hardware")).Trim()
        isEmulator = $true
    }

    if (-not $SkipBuild) {
        $unityArguments = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $resolvedProjectPath,
            "-executeMethod", "Gacha.EditorTools.AndroidSmokeBuilder.BuildEmulatorBatch",
            "-logFile", $unityLogPath
        )
        Push-Location $resolvedProjectPath
        try {
            # Unity.exe is a Windows GUI executable. PowerShell may return before
            # it exits, so use Start-Process -Wait to prevent inspecting a stale
            # APK or hashing a log that Unity is still writing.
            $quotedUnityArguments = @($unityArguments | ForEach-Object {
                if ($_ -match '[\s"]') {
                    '"' + $_.Replace('"', '\"') + '"'
                }
                else {
                    $_
                }
            })
            $unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $quotedUnityArguments `
                -Wait -PassThru
            $unityExitCode = $unityProcess.ExitCode
        }
        finally {
            Pop-Location
        }
        Assert-Condition ($unityExitCode -eq 0) "Unity emulator build failed (exit=$unityExitCode). See '$unityLogPath'."
    }
    Assert-Condition (Test-Path -LiteralPath $artifactPath -PathType Leaf) `
        "Emulator APK was not produced at '$artifactRelativePath'."

    $badging = Invoke-NativeText -FilePath $resolvedAaptPath -Arguments @("dump", "badging", $artifactPath) `
        -FailureMessage "Could not inspect emulator APK"
    $apkPackage = Get-FirstMatch $badging "package: name='([^']+)'" "APK package"
    $versionCode = Get-FirstMatch $badging "versionCode='([^']+)'" "APK versionCode"
    $versionName = Get-FirstMatch $badging "versionName='([^']+)'" "APK versionName"
    $nativeAbi = Get-FirstMatch $badging "^native-code:\s*'([^']+)'\s*$" "APK native ABI"
    Assert-Condition ($apkPackage -eq $packageId) "Unexpected APK package '$apkPackage'."
    Assert-Condition ($nativeAbi -eq "x86_64") "Emulator APK must contain only x86_64; found '$nativeAbi'."
    $checks.apkIdentity = $true

    $artifactItem = Get-Item -LiteralPath $artifactPath
    $artifactEvidence = [ordered]@{
        relativePath = $artifactRelativePath
        bytes = $artifactItem.Length
        sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        packageId = $apkPackage
        versionCode = $versionCode
        versionName = $versionName
        nativeAbi = $nativeAbi
        artifactClass = "development-x86_64"
        buildExecutedInThisInvocation = (-not $SkipBuild.IsPresent)
    }

    Wait-AdbTargetReady -ResolvedAdbPath $resolvedAdbPath -TargetSerial $Serial -TimeoutSeconds 60
    $postBuildQemu = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.kernel.qemu")).Trim()
    $postBuildAbiList = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "getprop", "ro.product.cpu.abilist")).Trim()
    Assert-Condition ($postBuildQemu -eq "1") "Recovered ADB target is not the expected Android emulator."
    Assert-Condition (($postBuildAbiList -split ',') -contains "x86_64") `
        "Recovered ADB target no longer advertises x86_64 ABI."
    $checks.postBuildDeviceReady = $true

    $installOutput = Invoke-Adb $resolvedAdbPath $Serial @("install", "-r", $artifactPath) `
        "Could not upgrade-install emulator APK"
    Assert-Condition ($installOutput -match 'Success') "ADB install did not report Success."
    $checks.upgradeInstallPreservedData = $true

    $packageDump = Invoke-Adb $resolvedAdbPath $Serial @("shell", "dumpsys", "package", $packageId)
    Assert-Condition ($packageDump -match 'primaryCpuAbi=x86_64') "Installed package ABI is not x86_64."
    Assert-Condition ($packageDump -match 'DEBUGGABLE') "Installed emulator package is not debuggable Development software."
    $checks.packageIdentity = $true
    $packageInfoPath = Join-Path $resolvedEvidenceDirectory "package-info.txt"
    $packageSummary = @(
        $packageDump -split "`r?`n" |
            Where-Object { $_ -match 'versionName=|versionCode=|primaryCpuAbi=|secondaryCpuAbi=|flags=|pkgFlags=' }
    ) -join "`n"
    Write-Utf8NoBom $packageInfoPath ($packageSummary + "`n")
    $evidenceFiles += $packageInfoPath

    for ($sample = 1; $sample -le $StartupSamples; $sample++) {
        Invoke-Adb $resolvedAdbPath $Serial @("shell", "am", "force-stop", $packageId) | Out-Null
        $startOutput = Invoke-Adb $resolvedAdbPath $Serial @(
            "shell", "am", "start", "-W", "-n", "$packageId/$activityName",
            "--es", "unity", "-force-gles30"
        ) "Could not cold-start emulator application"
        Assert-Condition ($startOutput -match '(?m)^Status:\s*ok\s*$') "Android activity start did not return Status: ok."
        $launchSamples += [ordered]@{
            sample = $sample
            totalTimeMs = Get-OptionalInt $startOutput '^TotalTime:\s*(\d+)\s*$'
            waitTimeMs = Get-OptionalInt $startOutput '^WaitTime:\s*(\d+)\s*$'
        }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Seconds $RenderWaitSeconds

    $uiResult = Resolve-KnownSystemUiAnr -ResolvedAdbPath $resolvedAdbPath -TargetSerial $Serial `
        -MaximumRecoveries 2
    $metrics.systemUiRecoveryCount = $uiResult.recoveries
    $hierarchyPath = Join-Path $resolvedEvidenceDirectory "window-hierarchy.xml"
    Write-Utf8NoBom $hierarchyPath ($uiResult.hierarchy + "`n")
    $evidenceFiles += $hierarchyPath
    $checks.systemUiDialogClear = $true

    $activityDump = Invoke-Adb $resolvedAdbPath $Serial @("shell", "dumpsys", "activity", "activities")
    Assert-Condition ($activityDump -match [regex]::Escape($packageId)) "Application is not present in the activity stack."
    Assert-Condition ($activityDump -match "topResumedActivity=.*$([regex]::Escape($packageId))") `
        "Application is not the top resumed activity."
    $checks.applicationForeground = $true

    $appProcessId = (Invoke-Adb $resolvedAdbPath $Serial @("shell", "pidof", $packageId)).Trim()
    Assert-Condition ($appProcessId -match '^\d+$') "Could not resolve the application PID."

    $meminfo = Invoke-Adb $resolvedAdbPath $Serial @("shell", "dumpsys", "meminfo", $packageId)
    $gfxinfo = Invoke-Adb $resolvedAdbPath $Serial @("shell", "dumpsys", "gfxinfo", $packageId, "framestats")
    $meminfoPath = Join-Path $resolvedEvidenceDirectory "meminfo.txt"
    $gfxinfoPath = Join-Path $resolvedEvidenceDirectory "gfxinfo-framestats.txt"
    Write-Utf8NoBom $meminfoPath ($meminfo + "`n")
    Write-Utf8NoBom $gfxinfoPath ($gfxinfo + "`n")
    $evidenceFiles += $meminfoPath, $gfxinfoPath
    $metrics.totalPssKiB = Get-OptionalInt $meminfo '^\s*TOTAL PSS:\s*(\d+)'
    $metrics.launchSamples = $launchSamples

    $remoteScreenshot = "/data/local/tmp/universal-gacha-preflight.png"
    Invoke-Adb $resolvedAdbPath $Serial @("shell", "screencap", "-p", $remoteScreenshot) | Out-Null
    $screenshotPath = Join-Path $resolvedEvidenceDirectory "launch-screen.png"
    Invoke-Adb $resolvedAdbPath $Serial @("pull", $remoteScreenshot, $screenshotPath) `
        "Could not pull emulator screenshot" | Out-Null
    Invoke-Adb $resolvedAdbPath $Serial @("shell", "rm", $remoteScreenshot) | Out-Null
    Assert-Condition ((Get-Item -LiteralPath $screenshotPath).Length -gt 10240) "Captured screenshot is unexpectedly small."
    $checks.screenshotCaptured = $true
    $evidenceFiles += $screenshotPath

    $pidLog = Invoke-Adb $resolvedAdbPath $Serial @("logcat", "--pid=$appProcessId", "-d", "-v", "threadtime")
    $fatalPattern = 'FATAL EXCEPTION|Fatal signal|ANR in com\.personal\.universalgacha|NullReferenceException|Unable to load runtime data|No Locales|UniversalRenderPipeline.*ctor'
    $fatalMatches = @($pidLog -split "`r?`n" | Where-Object { $_ -match $fatalPattern })
    $fatalScanPath = Join-Path $resolvedEvidenceDirectory "fatal-log-scan.txt"
    $fatalScanContent = if ($fatalMatches.Count -eq 0) {
        "PASS: no configured fatal signatures found for pid $appProcessId.`n"
    }
    else {
        ($fatalMatches -join "`n") + "`n"
    }
    Write-Utf8NoBom $fatalScanPath $fatalScanContent
    $evidenceFiles += $fatalScanPath
    Assert-Condition ($fatalMatches.Count -eq 0) `
        "Configured fatal signatures were found in the application log. See '$fatalScanPath'."
    $checks.fatalLogScan = $true

    $launchPath = Join-Path $resolvedEvidenceDirectory "launch-metrics.json"
    Write-Utf8NoBom $launchPath (($launchSamples | ConvertTo-Json -Depth 4) + "`n")
    $evidenceFiles += $launchPath

    $resultStatus = "passed"
}
catch {
    $failureMessage = $_.Exception.Message
}
finally {
    foreach ($relativePath in $knownGeneratedRelativePaths) {
        $generatedPath = Join-Path $resolvedProjectPath $relativePath
        if (-not $generatedPathExistedBefore[$relativePath] -and
            (Test-Path -LiteralPath $generatedPath -PathType Leaf)) {
            try {
                Remove-Item -LiteralPath $generatedPath -Force
                $cleanedGeneratedFiles += $relativePath
            }
            catch {
                $resultStatus = "failed"
                $cleanupMessage = "Could not remove build-generated file '$relativePath': $($_.Exception.Message)"
                $failureMessage = if ([string]::IsNullOrWhiteSpace($failureMessage)) {
                    $cleanupMessage
                }
                else {
                    "$failureMessage $cleanupMessage"
                }
            }
        }
    }
    try {
        & git.exe -C $resolvedProjectPath diff --quiet --
        $sourcePostBuildContentDiff = ($LASTEXITCODE -ne 0)
        $sourcePostBuildStatusText = Invoke-NativeText -FilePath "git.exe" `
            -Arguments @("-C", $resolvedProjectPath, "status", "--porcelain=v2") `
            -FailureMessage "Could not inspect post-build source status"
        $sourcePostBuildStatusEntries = @(
            $sourcePostBuildStatusText -split "`r?`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
    }
    catch {
        $resultStatus = "failed"
        $statusMessage = "Post-build source audit failed: $($_.Exception.Message)"
        $failureMessage = if ([string]::IsNullOrWhiteSpace($failureMessage)) {
            $statusMessage
        }
        else {
            "$failureMessage $statusMessage"
        }
    }

    $manifest = @()
    foreach ($path in $evidenceFiles | Select-Object -Unique) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $manifest += Get-FileEvidence $path
        }
    }
    $buildLogEvidence = $null
    if (Test-Path -LiteralPath $unityLogPath -PathType Leaf) {
        $buildLogEvidence = Get-FileEvidence $unityLogPath
        $buildLogEvidence.file = "Logs/$($buildLogEvidence.file)"
    }

    $receipt = [ordered]@{
        schemaVersion = 1
        status = $resultStatus
        testedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        environmentType = "emulator"
        authoritative = $false
        releaseGateEligible = $false
        worktreeUsed = $false
        source = [ordered]@{
            commit = $sourceCommit
            contentDiff = $sourceContentDiff
            statusEntries = $sourceStatusEntries
            postBuildContentDiff = $sourcePostBuildContentDiff
            postBuildStatusEntries = $sourcePostBuildStatusEntries
            cleanedGeneratedFiles = $cleanedGeneratedFiles
        }
        artifact = $artifactEvidence
        device = $deviceEvidence
        checks = $checks
        metrics = $metrics
        evidence = $manifest
        buildLog = $buildLogEvidence
        limitations = @(
            "Does not prove physical touch or haptic feel.",
            "Does not prove physical speaker or audio-focus quality; this AVD may run without audio.",
            "Does not prove cellular handover, thermal behavior, battery behavior, ARM64 performance, Safe Area diversity, or the three-tier physical-device matrix.",
            "Cannot close G-02, the physical tail of G-05, Alpha, or Release Candidate gates."
        )
        failure = $failureMessage
    }
    Write-Utf8NoBom $receiptPath (($receipt | ConvertTo-Json -Depth 10) + "`n")
}

if ($resultStatus -ne "passed") {
    throw "Emulator preflight failed: $failureMessage Receipt: '$receiptPath'."
}

Write-Output "EMULATOR PREFLIGHT PASSED"
Write-Output "Receipt: $receiptPath"
Write-Output "Source: $sourceCommit"
Write-Output "APK SHA-256: $($artifactEvidence.sha256)"
