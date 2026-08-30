param(
    [string]$SourceCommit = "HEAD",
    [string]$UnityVersion = "6000.0.73f1",
    [string]$UnityPath,
    [string]$ScratchRoot,
    [string]$EvidenceDirectory,
    [string]$PrivateImportsPath,
    [string]$PrivatePokedexPath,
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
        [string[]]$Arguments
    )
    $process = Start-Process `
        -FilePath $Executable `
        -ArgumentList $Arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    return [int]$process.ExitCode
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
    return [ordered]@{
        valid = $result -eq "Passed" -and $failed -eq 0 -and $total -ge 1 -and $passed -eq $total
        result = $result
        total = $total
        passed = $passed
        failed = $failed
        skipped = $skipped
        startTime = [string]$run.'start-time'
        endTime = [string]$run.'end-time'
    }
}

function Get-PrivateInputMetadata {
    param([string]$Path)
    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction Stop)
    if ($files.Count -lt 1) {
        throw "Private Imports input contains no files."
    }
    $manifestCount = @($files | Where-Object {
        $_.Name -eq 'manifest.json' -or $_.Name -eq 'printing-language-groups.json'
    }).Count
    if ($manifestCount -lt 1) {
        throw "Private Imports input contains no manifest files."
    }
    $rootPrefix = (Get-FullPath $Path).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $records = @($files | ForEach-Object {
        $relative = $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        "$relative`0$($_.Length)"
    } | Sort-Object -CaseSensitive)
    $treeBytes = [Text.UTF8Encoding]::new($false).GetBytes(($records -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $treeHash = (($sha256.ComputeHash($treeBytes) | ForEach-Object {
            $_.ToString('x2')
        }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
    return [ordered]@{
        logicalSource = "LocalContent/Imports"
        fileCount = $files.Count
        manifestCount = $manifestCount
        totalBytes = [long](($files | Measure-Object Length -Sum).Sum)
        treeMetadataSha256 = $treeHash
        injected = $false
    }
}

function Get-PrivatePokedexInputMetadata {
    param([string]$Path)
    $requiredDirectories = @('snapshot', 'artwork', 'links')
    foreach ($name in $requiredDirectories) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $name) -PathType Container)) {
            throw "Private Pokedex runtime input is missing '$name'."
        }
    }
    $taxonomyPath = Join-Path $Path 'snapshot\pokemon-taxonomy.json'
    $englishLinksPath = Join-Path $Path 'links\pokemon-card-subject-links.en.json'
    $artworkManifestCount = @(
        Get-ChildItem -LiteralPath (Join-Path $Path 'artwork') -Recurse -Filter 'manifest.json' -File
    ).Count
    if (-not (Test-Path -LiteralPath $taxonomyPath -PathType Leaf)) {
        throw "Private Pokedex runtime input is missing the taxonomy snapshot."
    }
    if (-not (Test-Path -LiteralPath $englishLinksPath -PathType Leaf)) {
        throw "Private Pokedex runtime input is missing English card-subject links."
    }
    if ($artworkManifestCount -lt 1) {
        throw "Private Pokedex runtime input contains no artwork manifests."
    }
    $files = @($requiredDirectories | ForEach-Object {
        Get-ChildItem -LiteralPath (Join-Path $Path $_) -Recurse -File -ErrorAction Stop
    })
    $rootPrefix = (Get-FullPath $Path).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $records = @($files | ForEach-Object {
        $relative = $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        "$relative`0$($_.Length)"
    } | Sort-Object -CaseSensitive)
    $treeBytes = [Text.UTF8Encoding]::new($false).GetBytes(($records -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $treeHash = (($sha256.ComputeHash($treeBytes) | ForEach-Object {
            $_.ToString('x2')
        }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
    return [ordered]@{
        logicalSource = "LocalContent/Pokedex/{snapshot,artwork,links}"
        fileCount = $files.Count
        artworkManifestCount = $artworkManifestCount
        totalBytes = [long](($files | Measure-Object Length -Sum).Sum)
        treeMetadataSha256 = $treeHash
        injected = $false
    }
}

function Copy-PrivateDirectory {
    param([string]$Source, [string]$Destination)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $process = Start-Process `
        -FilePath "robocopy.exe" `
        -ArgumentList @(
            ('"{0}"' -f $Source),
            ('"{0}"' -f $Destination),
            '/E', '/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:1',
            '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
        ) `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -gt 7) {
        throw "Private Imports injection failed (robocopy exit=$($process.ExitCode))."
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
        if (-not $parsed.valid -or $parsed.passed -ne 2) {
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

        $privateFixture = Join-Path $testRoot "private-input"
        [IO.Directory]::CreateDirectory($privateFixture) | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $privateFixture "manifest.json"),
            '{"SchemaVersion":2}',
            [Text.UTF8Encoding]::new($false))
        $privateMetadata = Get-PrivateInputMetadata $privateFixture
        if ($privateMetadata.fileCount -ne 1 -or $privateMetadata.manifestCount -ne 1 -or
            $privateMetadata.treeMetadataSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Private input aggregate metadata mismatch."
        }

        $pokedexFixture = Join-Path $testRoot "private-pokedex"
        foreach ($relativeDirectory in @('snapshot', 'artwork\generation-1', 'links')) {
            [IO.Directory]::CreateDirectory((Join-Path $pokedexFixture $relativeDirectory)) | Out-Null
        }
        [IO.File]::WriteAllText(
            (Join-Path $pokedexFixture 'snapshot\pokemon-taxonomy.json'), '{}',
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            (Join-Path $pokedexFixture 'artwork\generation-1\manifest.json'), '{}',
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            (Join-Path $pokedexFixture 'links\pokemon-card-subject-links.en.json'), '{}',
            [Text.UTF8Encoding]::new($false))
        $pokedexMetadata = Get-PrivatePokedexInputMetadata $pokedexFixture
        if ($pokedexMetadata.fileCount -ne 3 -or $pokedexMetadata.artworkManifestCount -ne 1 -or
            $pokedexMetadata.treeMetadataSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Private Pokedex aggregate metadata mismatch."
        }
        Write-Output "Self-test passed: 5/5"
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
if ([string]::IsNullOrWhiteSpace($PrivateImportsPath)) {
    throw "PrivateImportsPath is required."
}
if ([string]::IsNullOrWhiteSpace($PrivatePokedexPath)) {
    throw "PrivatePokedexPath is required."
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
$privateImportsFullPath = Get-FullPath $PrivateImportsPath
$expectedPrivateImportsPath = Get-FullPath (Join-Path $repoRoot "LocalContent\Imports")
$privatePokedexFullPath = Get-FullPath $PrivatePokedexPath
$expectedPrivatePokedexPath = Get-FullPath (Join-Path $repoRoot "LocalContent\Pokedex")
if (-not $privateImportsFullPath.Equals($expectedPrivateImportsPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "PrivateImportsPath must be the source repository LocalContent/Imports directory."
}
if (-not (Test-Path -LiteralPath $privateImportsFullPath -PathType Container)) {
    throw "PrivateImportsPath was not found."
}
if (-not $privatePokedexFullPath.Equals($expectedPrivatePokedexPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "PrivatePokedexPath must be the source repository LocalContent/Pokedex directory."
}
if (-not (Test-Path -LiteralPath $privatePokedexFullPath -PathType Container)) {
    throw "PrivatePokedexPath was not found."
}
if (Test-IsPathUnder $evidenceFullPath $scratchFullPath) {
    throw "EvidenceDirectory must be outside ScratchRoot."
}
if ((Test-IsPathUnder $privateImportsFullPath $scratchFullPath) -or
    (Test-IsPathUnder $privateImportsFullPath $evidenceFullPath)) {
    throw "PrivateImportsPath must be outside ScratchRoot and EvidenceDirectory."
}
if ((Test-IsPathUnder $privatePokedexFullPath $scratchFullPath) -or
    (Test-IsPathUnder $privatePokedexFullPath $evidenceFullPath)) {
    throw "PrivatePokedexPath must be outside ScratchRoot and EvidenceDirectory."
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
$privateIgnored = @(& git -C $repoRoot check-ignore -q -- 'LocalContent/Imports')
if ($LASTEXITCODE -ne 0) {
    throw "LocalContent/Imports is not protected by the repository ignore policy."
}
$privatePokedexIgnored = @(& git -C $repoRoot check-ignore -q -- 'LocalContent/Pokedex')
if ($LASTEXITCODE -ne 0) {
    throw "LocalContent/Pokedex is not protected by the repository ignore policy."
}
$privateInputMetadata = Get-PrivateInputMetadata $privateImportsFullPath
$privatePokedexMetadata = Get-PrivatePokedexInputMetadata $privatePokedexFullPath
$scratchDrive = [IO.Path]::GetPathRoot($scratchFullPath).TrimEnd('\', '/')
$driveName = $scratchDrive.TrimEnd(':')
$drive = Get-PSDrive -Name $driveName -PSProvider FileSystem
$freeGiB = [math]::Round($drive.Free / 1GB, 3)
if ($drive.Free -lt ($MinimumFreeGiB * 1GB)) {
    throw "Scratch drive has $freeGiB GiB free; at least $MinimumFreeGiB GiB is required."
}

$startedUtc = [DateTime]::UtcNow
$checkoutPath = Join-Path $scratchFullPath "checkout"
$checkoutPrivateImportsPath = Join-Path $checkoutPath "LocalContent\Imports"
$checkoutPrivatePokedexPath = Join-Path $checkoutPath "LocalContent\Pokedex"
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
    privateInputs = [ordered]@{
        imports = $privateInputMetadata
        pokedexRuntime = $privatePokedexMetadata
    }
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
    Copy-PrivateDirectory $privateImportsFullPath $checkoutPrivateImportsPath
    $summary.privateInputs.imports.injected = $true
    foreach ($name in @('snapshot', 'artwork', 'links')) {
        Copy-PrivateDirectory `
            (Join-Path $privatePokedexFullPath $name) `
            (Join-Path $checkoutPrivatePokedexPath $name)
    }
    $summary.privateInputs.pokedexRuntime.injected = $true
    [IO.Directory]::CreateDirectory($testResultsPath) | Out-Null

    $projectVersionText = Get-Content -LiteralPath (Join-Path $checkoutPath "ProjectSettings\ProjectVersion.txt") -Raw
    if ($projectVersionText -notmatch "m_EditorVersion:\s+$([regex]::Escape($UnityVersion))(?:\r?\n|$)") {
        throw "Archived project does not declare Unity $UnityVersion."
    }

    $editXml = Join-Path $testResultsPath "g08-clean-editmode.xml"
    $editLog = Join-Path $testResultsPath "g08-clean-editmode.log"
    $editExitCode = Invoke-WaitingProcess $unityFullPath @(
        '-batchmode', '-nographics',
        '-projectPath', ('"{0}"' -f $checkoutPath),
        '-runTests', '-testPlatform', 'EditMode',
        '-testResults', ('"{0}"' -f $editXml),
        '-logFile', ('"{0}"' -f $editLog)
    )
    $summary.tests.editMode = Read-UnityTestResult $editXml "Clean EditMode"
    $summary.tests.editMode['processExitCode'] = $editExitCode
    if (-not $summary.tests.editMode.valid -or $editExitCode -ne 0) {
        throw "Clean EditMode failed: result=$($summary.tests.editMode.result) total=$($summary.tests.editMode.total) passed=$($summary.tests.editMode.passed) failed=$($summary.tests.editMode.failed) skipped=$($summary.tests.editMode.skipped) exit=$editExitCode"
    }

    $playXml = Join-Path $testResultsPath "g08-clean-playmode.xml"
    $playLog = Join-Path $testResultsPath "g08-clean-playmode.log"
    $playExitCode = Invoke-WaitingProcess $unityFullPath @(
        '-batchmode',
        '-projectPath', ('"{0}"' -f $checkoutPath),
        '-runTests', '-testPlatform', 'PlayMode',
        '-testResults', ('"{0}"' -f $playXml),
        '-logFile', ('"{0}"' -f $playLog)
    )
    $summary.tests.playMode = Read-UnityTestResult $playXml "Clean PlayMode"
    $summary.tests.playMode['processExitCode'] = $playExitCode
    if (-not $summary.tests.playMode.valid -or $playExitCode -ne 0) {
        throw "Clean PlayMode failed: result=$($summary.tests.playMode.result) total=$($summary.tests.playMode.total) passed=$($summary.tests.playMode.passed) failed=$($summary.tests.playMode.failed) skipped=$($summary.tests.playMode.skipped) exit=$playExitCode"
    }

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
        $privatePaths = @($PrimaryKeystorePath) + @($BackupKeystorePaths) +
            @($privateImportsFullPath, $privatePokedexFullPath)
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
