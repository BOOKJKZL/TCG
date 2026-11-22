function Read-UnityTestResult {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label did not produce a test result XML: $Path"
    }
    [xml]$document = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $run = $document.DocumentElement
    if ($null -eq $run -or $run.Name -ne "test-run") {
        throw "$Label result has no test-run root."
    }
    $counts = @{}
    foreach ($name in @("total", "passed", "failed", "skipped")) {
        $value = $run.GetAttribute($name)
        if ($value -notmatch '^[0-9]+$') {
            throw "$Label result has an invalid or missing '$name' count."
        }
        $counts[$name] = [int]$value
    }
    $result = $run.GetAttribute("result")
    return [ordered]@{
        valid = $result -eq "Passed" -and $counts.total -gt 0 -and $counts.passed -eq $counts.total -and
            $counts.failed -eq 0 -and $counts.skipped -eq 0
        result = $result
        total = $counts.total
        passed = $counts.passed
        failed = $counts.failed
        skipped = $counts.skipped
        startTime = $run.GetAttribute("start-time")
        endTime = $run.GetAttribute("end-time")
    }
}
