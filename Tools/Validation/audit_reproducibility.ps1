param(
    [switch]$KeepTemp,
    [switch]$SkipSite,
    [string]$UnityVersion = "6000.0.73f1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$siteRelativeRoot = "Cloud/TCGContentSite"

function Invoke-NativeCapture {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $lines = @(& $Executable @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        return [pscustomobject]@{
            exitCode = $exitCode
            output = $lines
            tail = @($lines | Select-Object -Last 25)
        }
    }
    finally {
        Pop-Location
    }
}

function Convert-AuditJson {
    param([object]$Result)

    $text = @($Result.output) -join [Environment]::NewLine
    try {
        return $text | ConvertFrom-Json
    }
    catch {
        throw "npm audit did not return valid JSON: $($_.Exception.Message)"
    }
}

function Get-GitValue {
    param([string[]]$Arguments, [string]$WorkingDirectory = $repoRoot)

    $result = Invoke-NativeCapture "git" $Arguments $WorkingDirectory
    if ($result.exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($result.tail -join [Environment]::NewLine)"
    }
    return (@($result.output) -join [Environment]::NewLine).Trim()
}

$outerRoot = Get-GitValue @("rev-parse", "--show-superproject-working-tree")
$unityPath = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
$nodeVersion = (Invoke-NativeCapture "node" @("--version") $repoRoot).output -join ""
$npmVersion = (Invoke-NativeCapture "npm" @("--version") $repoRoot).output -join ""
$innerCommit = Get-GitValue @("rev-parse", "HEAD")
$innerBranch = Get-GitValue @("rev-parse", "--abbrev-ref", "HEAD")
$innerWorktreeResult = Invoke-NativeCapture "git" @("worktree", "list") $repoRoot
$innerStatusResult = Invoke-NativeCapture "git" @("status", "--porcelain=v1") $repoRoot
if ($innerWorktreeResult.exitCode -ne 0 -or $innerStatusResult.exitCode -ne 0) {
    throw "Could not inspect the inner repository worktrees and status."
}
$innerWorktrees = @($innerWorktreeResult.output | Where-Object { $_ })
$innerStatus = @($innerStatusResult.output | Where-Object { $_ })
$outerCommit = if ($outerRoot) { Get-GitValue @("rev-parse", "HEAD") $outerRoot } else { $null }
$outerGitlink = if ($outerRoot) {
    Get-GitValue @("ls-files", "--stage", "Games/universal-gacha-simulator") $outerRoot
}
else {
    $null
}

$report = [ordered]@{
    schemaVersion = 1
    auditedAtUtc = [DateTime]::UtcNow.ToString("o")
    mode = if ($SkipSite) { "identity-only" } else { "tracked-input-clean-site-rebuild" }
    environment = [ordered]@{
        os = [Environment]::OSVersion.VersionString
        powershell = $PSVersionTable.PSVersion.ToString()
        node = $nodeVersion.Trim()
        npm = $npmVersion.Trim()
        unityRequested = $UnityVersion
        unityExists = Test-Path -LiteralPath $unityPath
        unityFileVersion = if (Test-Path -LiteralPath $unityPath) {
            (Get-Item -LiteralPath $unityPath).VersionInfo.FileVersion
        }
        else {
            $null
        }
    }
    repository = [ordered]@{
        innerCommit = $innerCommit
        innerBranch = $innerBranch
        innerWorktreeCount = $innerWorktrees.Count
        innerWorktrees = $innerWorktrees
        innerStatusBeforeAudit = $innerStatus
        outerRoot = $outerRoot
        outerCommit = $outerCommit
        outerGitlink = $outerGitlink
    }
    site = $null
}

if (-not $SkipSite) {
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $tempRoot = Join-Path $tempBase ("ugs-g08-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        $trackedResult = Invoke-NativeCapture "git" @("ls-files", "--", $siteRelativeRoot) $repoRoot
        if ($trackedResult.exitCode -ne 0) {
            throw "Could not enumerate tracked Site inputs."
        }
        $tracked = @($trackedResult.output | Where-Object { $_ })
        foreach ($relative in $tracked) {
            $source = Join-Path $repoRoot $relative
            $target = Join-Path $tempRoot $relative
            $targetDirectory = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $targetDirectory)) {
                New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            }
            Copy-Item -LiteralPath $source -Destination $target
        }

        $cleanSiteRoot = Join-Path $tempRoot "Cloud\TCGContentSite"
        $npmCi = Invoke-NativeCapture "npm" @("ci") $cleanSiteRoot
        $npmTest = Invoke-NativeCapture "npm" @("test") $cleanSiteRoot
        $npmLint = Invoke-NativeCapture "npm" @("run", "lint") $cleanSiteRoot
        $productionAuditResult = Invoke-NativeCapture "npm" @("audit", "--omit=dev", "--json") $cleanSiteRoot
        $fullAuditResult = Invoke-NativeCapture "npm" @("audit", "--json") $cleanSiteRoot
        $productionAudit = Convert-AuditJson $productionAuditResult
        $fullAudit = Convert-AuditJson $fullAuditResult
        $advisories = @($fullAudit.vulnerabilities.psobject.Properties | ForEach-Object {
            [pscustomobject]@{
                name = $_.Name
                severity = $_.Value.severity
                isDirect = $_.Value.isDirect
                via = @($_.Value.via | ForEach-Object {
                    if ($_ -is [string]) { $_ } else { $_.title }
                })
            }
        })

        $report.site = [ordered]@{
            trackedInputFiles = $tracked.Count
            packageLockSha256 = (Get-FileHash -LiteralPath (Join-Path $cleanSiteRoot "package-lock.json") -Algorithm SHA256).Hash.ToLowerInvariant()
            npmCi = [ordered]@{ exitCode = $npmCi.exitCode; tail = $npmCi.tail }
            npmTest = [ordered]@{
                exitCode = $npmTest.exitCode
                tail = $npmTest.tail
                failureOutput = if ($npmTest.exitCode -ne 0) { $npmTest.output } else { $null }
            }
            npmLint = [ordered]@{ exitCode = $npmLint.exitCode; tail = $npmLint.tail }
            productionAudit = [ordered]@{
                exitCode = $productionAuditResult.exitCode
                vulnerabilities = $productionAudit.metadata.vulnerabilities
                totalDependencies = $productionAudit.metadata.dependencies.total
            }
            fullAudit = [ordered]@{
                exitCode = $fullAuditResult.exitCode
                vulnerabilities = $fullAudit.metadata.vulnerabilities
                totalDependencies = $fullAudit.metadata.dependencies.total
                advisories = $advisories
            }
        }
    }
    finally {
        if ($KeepTemp) {
            $report.tempRoot = $tempRoot
        }
        elseif (Test-Path -LiteralPath $tempRoot) {
            $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
            if (-not $resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
                -not ([IO.Path]::GetFileName($resolvedTemp)).StartsWith("ugs-g08-", [StringComparison]::Ordinal)) {
                throw "Refusing to clean unexpected temporary path '$resolvedTemp'."
            }
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
        }
    }
}

$sitePassed = $SkipSite -or (
    $report.site.npmCi.exitCode -eq 0 -and
    $report.site.npmTest.exitCode -eq 0 -and
    $report.site.npmLint.exitCode -eq 0 -and
    $report.site.productionAudit.exitCode -eq 0)
$report.passed = (
    $report.environment.unityExists -and
    $report.repository.innerBranch -eq "master" -and
    $report.repository.innerWorktreeCount -eq 1 -and
    $sitePassed)

$report | ConvertTo-Json -Depth 12
if (-not $report.passed) {
    exit 1
}
