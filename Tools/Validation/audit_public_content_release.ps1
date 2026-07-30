param(
    [string]$CatalogUrl = "https://universal-gacha-content.jiejingleek.chatgpt.site/api/content/catalog.json",
    [string]$ReleaseRoot = "LocalContent/Releases/android-complete",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Send-HeadersOnly {
    param(
        [System.Net.Http.HttpClient]$Client,
        [System.Net.Http.HttpRequestMessage]$Request
    )
    return $Client.SendAsync(
        $Request,
        [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
    ).GetAwaiter().GetResult()
}

$releaseRootPath = (Resolve-Path $ReleaseRoot).Path
$catalogPath = Join-Path $releaseRootPath "catalog.json"
Assert-True (Test-Path $catalogPath -PathType Leaf) "Local catalog was not found: $catalogPath"
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $releaseRootPath "remote-release-audit.json"
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(5)

try {
    $catalogUri = [Uri]::new($CatalogUrl, [UriKind]::Absolute)
    $remoteCatalogBytes = $client.GetByteArrayAsync($catalogUri).GetAwaiter().GetResult()
    $localCatalogBytes = [IO.File]::ReadAllBytes($catalogPath)
    $remoteCatalogSha256 = Get-Sha256Hex $remoteCatalogBytes
    $localCatalogSha256 = Get-Sha256Hex $localCatalogBytes
    Assert-True ($remoteCatalogSha256 -eq $localCatalogSha256) "Remote catalog SHA-256 differs from the local release."

    $catalog = [Text.Encoding]::UTF8.GetString($remoteCatalogBytes) | ConvertFrom-Json
    $packages = @($catalog.packages)
    Assert-True ($packages.Count -gt 0) "Remote catalog contains no packages."
    Assert-True ((@($packages.packageId | Select-Object -Unique)).Count -eq $packages.Count) "Remote catalog has duplicate package ids."

    $headPassed = 0
    $rangePassed = 0
    foreach ($package in $packages) {
        $archiveUri = [Uri]::new($catalogUri, [string]$package.archiveUrl)
        $headRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Head, $archiveUri)
        $headResponse = Send-HeadersOnly $client $headRequest
        try {
            Assert-True ([int]$headResponse.StatusCode -eq 200) "HEAD failed for $($package.packageId): $([int]$headResponse.StatusCode)"
            Assert-True ($headResponse.Content.Headers.ContentLength -eq [long]$package.downloadBytes) "HEAD length differs for $($package.packageId)."
            $headPassed++
        }
        finally { $headResponse.Dispose(); $headRequest.Dispose() }

        $offset = [long][Math]::Floor(([long]$package.downloadBytes) / 2)
        $rangeRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $archiveUri)
        $rangeRequest.Headers.Range = [System.Net.Http.Headers.RangeHeaderValue]::new($offset, $null)
        $rangeResponse = Send-HeadersOnly $client $rangeRequest
        try {
            Assert-True ([int]$rangeResponse.StatusCode -eq 206) "Range GET failed for $($package.packageId): $([int]$rangeResponse.StatusCode)"
            $expectedRange = "bytes $offset-$([long]$package.downloadBytes - 1)/$([long]$package.downloadBytes)"
            Assert-True ($rangeResponse.Content.Headers.ContentRange.ToString() -eq $expectedRange) "Content-Range differs for $($package.packageId)."
            $rangePassed++
        }
        finally { $rangeResponse.Dispose(); $rangeRequest.Dispose() }
    }

    $writeRejected = 0
    $writeTargets = @(
        $catalogUri,
        [Uri]::new($catalogUri, [string]$packages[0].archiveUrl)
    )
    foreach ($target in $writeTargets) {
        foreach ($methodName in @("POST", "PUT", "PATCH", "DELETE")) {
            $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($methodName), $target)
            $request.Content = [System.Net.Http.ByteArrayContent]::new([byte[]](1, 2, 3))
            $response = Send-HeadersOnly $client $request
            try {
                Assert-True ([int]$response.StatusCode -eq 405) "$methodName was not rejected for $target."
                $writeRejected++
            }
            finally { $response.Dispose(); $request.Dispose() }
        }
    }

    $report = [ordered]@{
        schemaVersion = 1
        auditedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        catalogUrl = $catalogUri.AbsoluteUri
        catalogSha256 = $remoteCatalogSha256
        packageCount = $packages.Count
        downloadBytes = [long](($packages | Measure-Object downloadBytes -Sum).Sum)
        largestPackageBytes = [long](($packages | Measure-Object downloadBytes -Maximum).Maximum)
        headPassed = $headPassed
        rangePassed = $rangePassed
        writeMethodsRejected = $writeRejected
        authorizationHeaderUsed = $false
        valid = $true
    }
    $report | ConvertTo-Json -Depth 4 | Set-Content -Path $ReportPath -Encoding UTF8
    $report | ConvertTo-Json -Depth 4
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
