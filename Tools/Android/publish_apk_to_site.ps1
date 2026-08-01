param(
    [string]$ApkPath = "Builds/Android/UniversalGachaSimulator-smoke.apk",
    [string]$CredentialPath = "LocalContent/site-publisher-credential.json",
    [string]$VersionName = "",
    [int]$VersionCode = 0,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$maximumApkBytes = 200MB

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-UnityReleaseMetadata {
    param([string]$ProjectSettingsText)
    $versionMatch = [regex]::Match($ProjectSettingsText, '(?m)^\s*bundleVersion:\s*(\S+)\s*$')
    $codeMatch = [regex]::Match($ProjectSettingsText, '(?m)^\s*AndroidBundleVersionCode:\s*(\d+)\s*$')
    if (-not $versionMatch.Success -or -not $codeMatch.Success) {
        throw "ProjectSettings.asset does not contain Android release metadata."
    }
    return [pscustomobject]@{
        VersionName = $versionMatch.Groups[1].Value
        VersionCode = [int]$codeMatch.Groups[1].Value
    }
}

function Assert-VersionName {
    param([string]$Value)
    if ($Value -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]{0,39}$') {
        throw "The APK version name is invalid."
    }
}

function Assert-SiteUri {
    param([Uri]$Value)
    if (-not $Value.IsAbsoluteUri -or $Value.Scheme -ne 'https') {
        throw "The Sites URL must be an absolute HTTPS URL."
    }
    if (-not $Value.Host.EndsWith('.chatgpt.site', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publisher credentials may only be sent to chatgpt.site."
    }
    if ($Value.UserInfo -or $Value.Query -or $Value.Fragment -or ($Value.AbsolutePath -ne '/' -and $Value.AbsolutePath)) {
        throw "The Sites URL cannot contain user info, a path, query, or fragment."
    }
}

function Read-ErrorMessage {
    param([System.Net.Http.HttpResponseMessage]$Response)
    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    try {
        $payload = $body | ConvertFrom-Json
        if ($payload.error) { return [string]$payload.error }
    }
    catch { }
    if ($body.Length -gt 300) { $body = $body.Substring(0, 300) }
    return "HTTP $([int]$Response.StatusCode): $body"
}

function Invoke-ApkUpload {
    param(
        [System.Net.Http.HttpClient]$Client,
        [Uri]$Uri,
        [string]$Token,
        [string]$Path,
        [string]$ReleaseVersion,
        [int]$ReleaseCode,
        [string]$Sha256
    )
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $stream = [IO.File]::OpenRead($Path)
        $content = [System.Net.Http.StreamContent]::new($stream)
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $Uri)
        try {
            $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
            $request.Headers.TryAddWithoutValidation('X-Release-Version', $ReleaseVersion) | Out-Null
            $request.Headers.TryAddWithoutValidation('X-Release-Code', $ReleaseCode.ToString()) | Out-Null
            $request.Headers.TryAddWithoutValidation('X-Apk-Sha256', $Sha256) | Out-Null
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/vnd.android.package-archive')
            $content.Headers.ContentLength = $stream.Length
            $request.Content = $content
            $response = $Client.SendAsync($request).GetAwaiter().GetResult()
            if ($response.IsSuccessStatusCode) {
                try { return ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json) }
                finally { $response.Dispose() }
            }
            $retryable = [int]$response.StatusCode -eq 408 -or [int]$response.StatusCode -eq 429 -or [int]$response.StatusCode -ge 500
            $message = Read-ErrorMessage $response
            $response.Dispose()
            if (-not $retryable -or $attempt -eq 3) { throw $message }
        }
        finally {
            $request.Dispose()
            $content.Dispose()
            $stream.Dispose()
        }
        Start-Sleep -Seconds ([Math]::Pow(2, $attempt - 1))
    }
    throw "APK upload retries were exhausted."
}

function Invoke-PublicVerification {
    param(
        [System.Net.Http.HttpClient]$Client,
        [Uri]$SiteBaseUri,
        [string]$ExpectedSha256,
        [long]$ExpectedBytes,
        [string]$ExpectedVersionName,
        [int]$ExpectedVersionCode
    )
    $manifestUri = [Uri]::new($SiteBaseUri, 'api/releases/android/latest.json')
    $manifest = $Client.GetStringAsync($manifestUri).GetAwaiter().GetResult() | ConvertFrom-Json
    if (
        $manifest.sha256 -ne $ExpectedSha256 -or
        [long]$manifest.downloadBytes -ne $ExpectedBytes -or
        $manifest.versionName -ne $ExpectedVersionName -or
        [int]$manifest.versionCode -ne $ExpectedVersionCode
    ) {
        throw "The public APK manifest differs from the local release metadata."
    }

    $downloadUri = [Uri]::new($SiteBaseUri, [string]$manifest.downloadUrl)
    $response = $Client.GetAsync($downloadUri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    try {
        if (-not $response.IsSuccessStatusCode) { throw (Read-ErrorMessage $response) }
        if ($response.Content.Headers.ContentLength -ne $ExpectedBytes) {
            throw "The public APK Content-Length differs from the local file."
        }
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
            }
            finally { $sha.Dispose() }
        }
        finally { $stream.Dispose() }
        if ($actual -ne $ExpectedSha256) { throw "The public APK SHA-256 differs from the local file." }
    }
    finally { $response.Dispose() }
    return $manifest
}

if ($SelfTest) {
    $fixture = @"
PlayerSettings:
  bundleVersion: 1.2.3-beta.1
  AndroidBundleVersionCode: 42
"@
    $metadata = Get-UnityReleaseMetadata $fixture
    if ($metadata.VersionName -ne '1.2.3-beta.1' -or $metadata.VersionCode -ne 42) {
        throw "Release metadata parser self-test failed."
    }
    Assert-VersionName $metadata.VersionName
    Assert-SiteUri ([Uri]'https://example.chatgpt.site/')
    try {
        Assert-SiteUri ([Uri]'https://attacker.example/')
        throw "External-host rejection self-test failed."
    }
    catch {
        if ($_.Exception.Message -eq 'External-host rejection self-test failed.') { throw }
    }
    Write-Output "APK publisher self-test passed."
    exit 0
}

Add-Type -AssemblyName System.Net.Http
$apkFullPath = Resolve-RepoPath $ApkPath
$credentialFullPath = Resolve-RepoPath $CredentialPath
if (-not (Test-Path -LiteralPath $apkFullPath -PathType Leaf)) { throw "APK not found: $apkFullPath" }
if (-not (Test-Path -LiteralPath $credentialFullPath -PathType Leaf)) { throw "Sites publisher credential was not found." }

$apk = Get-Item -LiteralPath $apkFullPath
if ($apk.Length -le 0 -or $apk.Length -gt $maximumApkBytes) {
    throw "The APK must be non-empty and no larger than 200 MiB."
}
$prefix = [byte[]]::new(4)
$prefixStream = [IO.File]::OpenRead($apkFullPath)
try {
    if ($prefixStream.Read($prefix, 0, 4) -ne 4) { throw "The APK file is too short." }
}
finally { $prefixStream.Dispose() }
if ($prefix[0] -ne 0x50 -or $prefix[1] -ne 0x4b -or $prefix[2] -ne 0x03 -or $prefix[3] -ne 0x04) {
    throw "The file does not have an APK/ZIP signature."
}

$credential = Get-Content -LiteralPath $credentialFullPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($credential.version -ne 1 -or $credential.publisherToken -notmatch '^[A-Za-z0-9_-]{43,512}$') {
    throw "The Sites publisher credential is invalid."
}
$siteBaseUri = [Uri]$credential.siteBaseUrl
Assert-SiteUri $siteBaseUri

if (-not $VersionName -or $VersionCode -le 0) {
    $projectSettings = Get-Content -LiteralPath (Join-Path $repoRoot 'ProjectSettings/ProjectSettings.asset') -Raw -Encoding utf8
    $metadata = Get-UnityReleaseMetadata $projectSettings
    if (-not $VersionName) { $VersionName = $metadata.VersionName }
    if ($VersionCode -le 0) { $VersionCode = $metadata.VersionCode }
}
Assert-VersionName $VersionName
if ($VersionCode -le 0) { throw "APK versionCode must be a positive integer." }

$sha256 = (Get-FileHash -LiteralPath $apkFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "APK publication preflight: version=$VersionName+$VersionCode, bytes=$($apk.Length), sha256=$sha256"

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(15)
try {
    $adminUri = [Uri]::new($siteBaseUri, 'api/admin/releases/android')
    $result = Invoke-ApkUpload $client $adminUri $credential.publisherToken $apkFullPath $VersionName $VersionCode $sha256
    $manifest = Invoke-PublicVerification $client $siteBaseUri $sha256 $apk.Length $VersionName $VersionCode
    Write-Output "APK publication passed: reused=$($result.reused), previousDeleted=$($result.previousReleaseDeleted), download='$([Uri]::new($siteBaseUri, [string]$manifest.downloadUrl))'."
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
