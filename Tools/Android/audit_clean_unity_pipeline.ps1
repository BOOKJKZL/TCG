param(
    [string]$SourceCommit = "HEAD",
    [string]$UnityVersion = "6000.0.73f1",
    [string]$UnityPath,
    [string]$ScratchRoot,
    [string]$EvidenceDirectory,
    [string]$PrimaryKeystorePath,
    [string[]]$BackupKeystorePaths,
    [string]$KeyAlias = "universal-gacha-release",
    [string]$VersionName = "0.1.1-rc.1",
    [int]$VersionCode = 2,
    [int]$MinimumFreeGiB = 30,
    [switch]$SkipReleaseBuild,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workId = "WI-20260830-016"

function Get-FullPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Path cannot be empty."
    }
    return [IO.Path]::GetFullPath($Path)
}

function Test-IsPathUnder {
    param([string]$Candidate, [string]$Root)
    $candidateFull = (Get-FullPath $Candidate).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $rootFull = (Get-FullPath $Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeScratchPath {
    param([string]$Path)
    $fullPath = Get-FullPath $Path
    $root = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($root) -or
        $fullPath.TrimEnd('\', '/') -eq $root.TrimEnd('\', '/')) {
        throw "ScratchRoot cannot be a filesystem root."
    }
    if (Test-IsPathUnder $fullPath $repoRoot) {
        throw "ScratchRoot must be outside the source repository."
    }
    if ([IO.Path]::GetFileName($fullPath.TrimEnd('\', '/')) -ne $workId) {
        throw "ScratchRoot leaf must be exactly $workId."
    }
    return $fullPath
}

function Invoke-GitCapture {
    param([string[]]$Arguments)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& git -C $repoRoot @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed (exit=$exitCode): $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Invoke-WaitingProcess {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$FailureMessage
    )
    $process = Start-Process `
        -FilePath $Executable `
        -ArgumentList $Arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$FailureMessage (exit=$($process.ExitCode))."
    }
}

function Read-UnityTestResult {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label did not produce a test result XML: $Path"
    }
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $run = $document.'test-run'
    if ($null -eq $run) {
        throw "$Label result has no test-run root."
    }
    $result = [string]$run.result
    $total = [int]$run.total
    $passed = [int]$run.passed
    $failed = [int]$run.failed
    $skipped = [int]$run.skipped
    if ($result -ne "Passed" -or $failed -ne 0 -or $total -lt 1 -or $passed -ne $total) {
        throw "$Label failed: result=$result total=$total passed=$passed failed=$failed skipped=$skipped"
    }
    return [ordered]@{
        result = $result
        total = $total
        passed = $passed
        failed = $failed
        skipped = $skipped
        startTime = [string]$run.'start-time'
        endTime = [string]$run.'end-time'
    }
}

function Copy-EvidenceIfPresent {
    param([string]$Source, [string]$DestinationDirectory, [string]$DestinationName)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        return $null
    }
    [IO.Directory]::CreateDirectory($DestinationDirectory) | Out-Null
    $destination = Join-Path $DestinationDirectory $DestinationName
    Copy-Item -LiteralPath $Source -Destination $destination -Force
    return $destination
}

function Write-JsonUtf8 {
    param([string]$Path, [object]$Value)
    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Assert-EvidenceSecretBoundary {
    param([string]$Directory, [string[]]$PrivatePaths)
    $patterns = @(
        '-----BEGIN (?:RSA |ENCRYPTED )?PRIVATE KEY-----',
        'TCG_ANDROID_(?:KEYSTORE|KEY)_PASSWORD\s*=',
        'UGS_RELEASE_SIGNING_SECRET\s*='
    )
    $normalizedPrivatePaths = @($PrivatePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        Get-FullPath $_
    })
    $matches = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Extension -notin @('.json', '.log', '.txt', '.xml', '.md')) {
            return
        }
        $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) {
                $matches.Add("$($_.Name):$pattern")
            }
        }
        foreach ($privatePath in $normalizedPrivatePaths) {
            if ($content.IndexOf($privatePath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $matches.Add("$($_.Name):private-path")
            }
        }
    }
    if ($matches.Count -gt 0) {
        throw "Evidence secret boundary failed: $($matches -join ', ')"
    }
}

function Invoke-SelfTest {
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ("g08-clean-selftest-" + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Directory]::CreateDirectory($testRoot) | Out-Null
        $passedXml = Join-Path $testRoot "passed.xml"
        [IO.File]::WriteAllText($passedXml,
            '<test-run result="Passed" total="2" passed="2" failed="0" skipped="0" start-time="2026-08-30 00:00:00Z" end-time="2026-08-30 00:00:01Z" />',
            [Text.UTF8Encoding]::new($false))
        $parsed = Read-UnityTestResult $passedXml "self-test"
        if ($parsed.passed -ne 2) {
            throw "Passed XML count mismatch."
        }

        $unsafeRejected = $false
        try {
            Assert-SafeScratchPath ([IO.Path]::GetPathRoot($repoRoot)) | Out-Null
        }
        catch {
            $unsafeRejected = $true
        }
        if (-not $unsafeRejected) {
            throw "Filesystem root scratch path was not rejected."
        }

        $secretFile = Join-Path $testRoot "clean.log"
        [IO.File]::WriteAllText($secretFile, "clean evidence", [Text.UTF8Encoding]::new($false))
        Assert-EvidenceSecretBoundary $testRoot @()
        Write-Output "Self-test passed: 3/3"
    }
    finally {
        if (Test-Path -LiteralPath $testRoot) {
            Remove-Item -LiteralPath $testRoot -Recurse -Force
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if ([string]::IsNullOrWhiteSpace($ScratchRoot)) {
    throw "ScratchRoot is required."
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    throw "EvidenceDirectory is required."
}
if (-not $SkipReleaseBuild) {
    if ([string]::IsNullOrWhiteSpace($PrimaryKeystorePath)) {
        throw "PrimaryKeystorePath is required unless SkipReleaseBuild is used."
    }
    if ($null -eq $BackupKeystorePaths -or $BackupKeystorePaths.Count -ne 2) {
        throw "Exactly two BackupKeystorePaths are required unless SkipReleaseBuild is used."
    }
}

$scratchFullPath = Assert-SafeScratchPath $ScratchRoot
$evidenceFullPath = Get-FullPath $EvidenceDirectory
if (Test-IsPathUnder $evidenceFullPath $scratchFullPath) {
    throw "EvidenceDirectory must be outside ScratchRoot."
}
if (Test-Path -LiteralPath $scratchFullPath) {
    throw "ScratchRoot already exists; refusing to overwrite or delete it: $scratchFullPath"
}
[IO.Directory]::CreateDirectory($evidenceFullPath) | Out-Null

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
}
$unityFullPath = Get-FullPath $UnityPath
if (-not (Test-Path -LiteralPath $unityFullPath -PathType Leaf)) {
    throw "Unity Editor was not found: $unityFullPath"
}

$sourceCommitFull = [string](Invoke-GitCapture @('rev-parse', '--verify', "$SourceCommit^{commit}") |
    Select-Object -First 1)
$sourceCommitFull = $sourceCommitFull.Trim()
if ($sourceCommitFull -notmatch '^[0-9a-f]{40}$') {
    throw "SourceCommit did not resolve to a full commit."
}
$sourceStatus = @(Invoke-GitCapture @('status', '--short'))
$scratchDrive = [IO.Path]::GetPathRoot($scratchFullPath).TrimEnd('\', '/')
$driveName = $scratchDrive.TrimEnd(':')
$drive = Get-PSDrive -Name $driveName -PSProvider FileSystem
$freeGiB = [math]::Round($drive.Free / 1GB, 3)
if ($drive.Free -lt ($MinimumFreeGiB * 1GB)) {
    throw "Scratch drive has $freeGiB GiB free; at least $MinimumFreeGiB GiB is required."
}

$startedUtc = [DateTime]::UtcNow
$checkoutPath = Join-Path $scratchFullPath "checkout"
$archivePath = Join-Path $scratchFullPath "source.zip"
$testResultsPath = Join-Path $checkoutPath "TestResults"
$fileVersion = [regex]::Replace($VersionName, '[^0-9A-Za-z.-]', '-')
$apkPath = Join-Path $checkoutPath "Builds\Android\Release\UniversalGachaSimulator-release-$fileVersion+$VersionCode.apk"
$apkAuditPath = [IO.Path]::ChangeExtension($apkPath, ".release-audit.json")
$buildLogPath = Join-Path $checkoutPath "Builds\Android\Release\build-$fileVersion+$VersionCode.log"
$summaryPath = Join-Path $evidenceFullPath "clean-unity-pipeline-summary.json"
$summary = [ordered]@{
    schemaVersion = 1
    workId = $workId
    valid = $false
    sourceCommit = $sourceCommitFull
    sourceWorkingTreeStatus = @($sourceStatus)
    unityVersion = $UnityVersion
    scratchRoot = $scratchFullPath
    scratchDriveFreeGiBAtStart = $freeGiB
    minimumFreeGiB = $MinimumFreeGiB
    startedAtUtc = $startedUtc.ToString('o')
    finishedAtUtc = $null
    scratchRemoved = $false
    tests = [ordered]@{}
    release = $null
    evidenceSecretScan = "not_run"
    error = $null
}
$capturedFailure = $null

try {
    [IO.Directory]::CreateDirectory($checkoutPath) | Out-Null
    $archiveOutput = Invoke-GitCapture @('archive', '--format=zip', "--output=$archivePath", $sourceCommitFull)
    $null = $archiveOutput
    $summary.archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Expand-Archive -LiteralPath $archivePath -DestinationPath $checkoutPath
    Remove-Item -LiteralPath $archivePath -Force
    [IO.Directory]::CreateDirectory($testResultsPath) | Out-Null

    $projectVersionText = Get-Content -LiteralPath (Join-Path $checkoutPath "ProjectSettings\ProjectVersion.txt") -Raw
    if ($projectVersionText -notmatch "m_EditorVersion:\s+$([regex]::Escape($UnityVersion))(?:\r?\n|$)") {
        throw "Archived project does not declare Unity $UnityVersion."
    }

    $editXml = Join-Path $testResultsPath "g08-clean-editmode.xml"
    $editLog = Join-Path $testResultsPath "g08-clean-editmode.log"
    Invoke-WaitingProcess $unityFullPath @(
        '-batchmode', '-nographics',
        '-projectPath', ('"{0}"' -f $checkoutPath),
        '-runTests', '-testPlatform', 'EditMode',
        '-testResults', ('"{0}"' -f $editXml),
        '-logFile', ('"{0}"' -f $editLog)
    ) "Clean EditMode Unity process failed"
    $summary.tests.editMode = Read-UnityTestResult $editXml "Clean EditMode"

    $playXml = Join-Path $testResultsPath "g08-clean-playmode.xml"
    $playLog = Join-Path $testResultsPath "g08-clean-playmode.log"
    Invoke-WaitingProcess $unityFullPath @(
        '-batchmode',
        '-projectPath', ('"{0}"' -f $checkoutPath),
        '-runTests', '-testPlatform', 'PlayMode',
        '-testResults', ('"{0}"' -f $playXml),
        '-logFile', ('"{0}"' -f $playLog)
    ) "Clean PlayMode Unity process failed"
    $summary.tests.playMode = Read-UnityTestResult $playXml "Clean PlayMode"

    if (-not $SkipReleaseBuild) {
        & (Join-Path $checkoutPath "Tools\Android\initialize_release_signing.ps1") `
            -PrimaryKeystorePath (Get-FullPath $PrimaryKeystorePath) `
            -BackupKeystorePaths @($BackupKeystorePaths | ForEach-Object { Get-FullPath $_ }) `
            -KeyAlias $KeyAlias `
            -UnityVersion $UnityVersion `
            -VersionName $VersionName `
            -VersionCode $VersionCode `
            -UseExisting `
            -UseCredentialDialog `
            -BuildCandidate

        if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $apkAuditPath -PathType Leaf)) {
            throw "Clean release build did not produce the expected APK and audit report."
        }
        $audit = Get-Content -LiteralPath $apkAuditPath -Raw | ConvertFrom-Json
        if (-not [bool]$audit.valid -or @($audit.checks | Where-Object { $_.passed }).Count -ne @($audit.checks).Count) {
            throw "Clean release APK audit is not fully valid."
        }
        $summary.release = [ordered]@{
            fileName = [IO.Path]::GetFileName($apkPath)
            bytes = (Get-Item -LiteralPath $apkPath).Length
            sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
            auditSha256 = (Get-FileHash -LiteralPath $apkAuditPath -Algorithm SHA256).Hash.ToLowerInvariant()
            auditValid = [bool]$audit.valid
            passedChecks = @($audit.checks | Where-Object { $_.passed }).Count
            totalChecks = @($audit.checks).Count
            packageId = [string]$audit.artifact.packageId
            versionName = [string]$audit.artifact.versionName
            versionCode = [int]$audit.artifact.versionCode
            certificateSha256 = [string]$audit.artifact.certificateSha256
            signerCount = [int]$audit.artifact.signerCount
            debuggable = [bool]$audit.artifact.debuggable
            abis = @($audit.artifact.abis)
        }
    }

    $summary.valid = $true
}
catch {
    $capturedFailure = $_
    $summary.error = $_.Exception.Message
}
finally {
    foreach ($item in @(
        @{ Source = (Join-Path $testResultsPath 'g08-clean-editmode.xml'); Name = 'g08-clean-editmode.xml' },
        @{ Source = (Join-Path $testResultsPath 'g08-clean-editmode.log'); Name = 'g08-clean-editmode.log' },
        @{ Source = (Join-Path $testResultsPath 'g08-clean-playmode.xml'); Name = 'g08-clean-playmode.xml' },
        @{ Source = (Join-Path $testResultsPath 'g08-clean-playmode.log'); Name = 'g08-clean-playmode.log' },
        @{ Source = $apkAuditPath; Name = 'g08-clean-release-audit.json' },
        @{ Source = $buildLogPath; Name = 'g08-clean-release-build.log' }
    )) {
        Copy-EvidenceIfPresent $item.Source $evidenceFullPath $item.Name | Out-Null
    }

    if (Test-Path -LiteralPath $scratchFullPath) {
        try {
            Remove-Item -LiteralPath $scratchFullPath -Recurse -Force
        }
        catch {
            if ($null -eq $capturedFailure) {
                $capturedFailure = $_
                $summary.error = "Scratch cleanup failed: $($_.Exception.Message)"
            }
        }
    }
    $summary.scratchRemoved = -not (Test-Path -LiteralPath $scratchFullPath)
    if (-not $summary.scratchRemoved) {
        $summary.valid = $false
        if ([string]::IsNullOrWhiteSpace([string]$summary.error)) {
            $summary.error = "ScratchRoot still exists after cleanup."
        }
    }
    $summary.finishedAtUtc = [DateTime]::UtcNow.ToString('o')
    Write-JsonUtf8 $summaryPath $summary

    try {
        $privatePaths = @($PrimaryKeystorePath) + @($BackupKeystorePaths)
        Assert-EvidenceSecretBoundary $evidenceFullPath $privatePaths
        $summary.evidenceSecretScan = "passed"
    }
    catch {
        $summary.valid = $false
        $summary.evidenceSecretScan = "failed"
        $summary.error = $_.Exception.Message
        if ($null -eq $capturedFailure) {
            $capturedFailure = $_
        }
    }
    Write-JsonUtf8 $summaryPath $summary
}

if ($null -ne $capturedFailure) {
    throw $capturedFailure
}

Write-Output "Clean Unity pipeline passed for commit $sourceCommitFull."
Write-Output "Evidence: $evidenceFullPath"
Write-Output "Scratch removed: $($summary.scratchRemoved)"
Write-Output "No artifact was installed, uploaded, or published."
