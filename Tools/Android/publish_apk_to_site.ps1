param(
    [string]$ApkPath = "",
    [string]$CredentialPath = "LocalContent/site-publisher-credential.json",
    [string]$VersionName = "",
    [int]$VersionCode = 0,
    [string]$ReleaseNotes = "",
    [string]$AuditReportPath = "",
    [string]$CertificateFingerprintPath = "LocalContent/ReleaseSigning/certificate.sha256",
    [string]$UnityVersion = "6000.0.73f1",
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$maximumApkBytes = 60MB

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-VersionName {
    param([string]$Value)
    if ($Value -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]{0,39}$') {
        throw "The APK version name is invalid."
    }
}

function Assert-ReleaseArtifactPath {
    param([string]$Path)
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Builds/Android/Release'))
    $candidate = [IO.Path]::GetFullPath($Path)
    $releasePrefix = $releaseRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $name = [IO.Path]::GetFileName($candidate).ToLowerInvariant()
    if (-not $candidate.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Only APKs under Builds/Android/Release may be published as stable releases."
    }
    if (-not $name.EndsWith('.apk') -or -not $name.Contains('release') -or
        $name.Contains('smoke') -or $name.Contains('emulator') -or $name.Contains('development')) {
        throw "The stable APK filename must identify a release and must not identify a smoke, emulator, or development build."
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

function Test-StableManifestIdentity {
    param(
        [object]$Manifest,
        [string]$ExpectedSha256,
        [long]$ExpectedBytes,
        [string]$ExpectedVersionName,
        [int]$ExpectedVersionCode,
        [string]$ExpectedCertificateSha256,
        [string]$ExpectedReleaseNotes
    )
    return (
        $null -ne $Manifest -and
        [int]$Manifest.schemaVersion -eq 2 -and $Manifest.releaseChannel -eq 'stable' -and
        $Manifest.sha256 -eq $ExpectedSha256 -and [long]$Manifest.downloadBytes -eq $ExpectedBytes -and
        $Manifest.versionName -eq $ExpectedVersionName -and [int]$Manifest.versionCode -eq $ExpectedVersionCode -and
        $Manifest.certificateSha256 -eq $ExpectedCertificateSha256 -and
        $Manifest.releaseNotes -eq $ExpectedReleaseNotes)
}

function Invoke-ApkUpload {
    param(
        [System.Net.Http.HttpClient]$Client,
        [Uri]$Uri,
        [string]$Token,
        [string]$Path,
        [string]$ReleaseVersion,
        [int]$ReleaseCode,
        [string]$Sha256,
        [string]$AuditBase64,
        [string]$ReleaseNotesBase64
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
            $request.Headers.TryAddWithoutValidation('X-Release-Audit', $AuditBase64) | Out-Null
            $request.Headers.TryAddWithoutValidation('X-Release-Notes', $ReleaseNotesBase64) | Out-Null
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
        [int]$ExpectedVersionCode,
        [string]$ExpectedCertificateSha256,
        [string]$ExpectedReleaseNotes
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
    if (
        [int]$manifest.schemaVersion -ne 2 -or
        $manifest.releaseChannel -ne 'stable' -or
        $manifest.certificateSha256 -ne $ExpectedCertificateSha256 -or
        $manifest.releaseNotes -ne $ExpectedReleaseNotes
    ) {
        throw "The public APK manifest is not the expected audited stable release."
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
    Assert-VersionName '1.2.3-beta.1'
    Assert-SiteUri ([Uri]'https://example.chatgpt.site/')
    Assert-ReleaseArtifactPath (Join-Path $repoRoot 'Builds/Android/Release/game-release-1.apk')
    try {
        Assert-SiteUri ([Uri]'https://attacker.example/')
        throw "External-host rejection self-test failed."
    }
    catch {
        if ($_.Exception.Message -eq 'External-host rejection self-test failed.') { throw }
    }
    try {
        Assert-ReleaseArtifactPath (Join-Path $repoRoot 'Builds/Android/game-smoke.apk')
        throw "Smoke-path rejection self-test failed."
    }
    catch {
        if ($_.Exception.Message -eq 'Smoke-path rejection self-test failed.') { throw }
    }
    $stableFixture = [pscustomobject]@{
        schemaVersion = 2; releaseChannel = 'stable'; sha256 = ('a' * 64); downloadBytes = 42
        versionName = '1.2.3'; versionCode = 7; certificateSha256 = ('b' * 64); releaseNotes = 'notes'
    }
    if (-not (Test-StableManifestIdentity $stableFixture ('a' * 64) 42 '1.2.3' 7 ('b' * 64) 'notes')) {
        throw "Stable retry identity self-test failed."
    }
    Write-Output "APK publisher self-test passed."
    exit 0
}

Add-Type -AssemblyName System.Net.Http
if (-not $ApkPath) { throw "-ApkPath is required; smoke/default artifacts are never inferred." }
if (-not $VersionName) { throw "-VersionName is required and must match the audited APK." }
if ($VersionCode -le 0) { throw "-VersionCode is required and must be a positive integer." }
$ReleaseNotes = $ReleaseNotes.Trim()
if (-not $ReleaseNotes -or $ReleaseNotes.Length -gt 2000) {
    throw "-ReleaseNotes is required and must contain 1-2000 characters."
}
$apkFullPath = Resolve-RepoPath $ApkPath
$credentialFullPath = Resolve-RepoPath $CredentialPath
$fingerprintFullPath = Resolve-RepoPath $CertificateFingerprintPath
if (-not (Test-Path -LiteralPath $apkFullPath -PathType Leaf)) { throw "APK not found: $apkFullPath" }
if (-not (Test-Path -LiteralPath $credentialFullPath -PathType Leaf)) { throw "Sites publisher credential was not found." }
if (-not (Test-Path -LiteralPath $fingerprintFullPath -PathType Leaf)) { throw "Release certificate fingerprint file was not found." }
Assert-ReleaseArtifactPath $apkFullPath

$apk = Get-Item -LiteralPath $apkFullPath
if ($apk.Length -le 0 -or $apk.Length -gt $maximumApkBytes) {
    throw "The APK must be non-empty and no larger than 60 MiB."
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

Assert-VersionName $VersionName
$certificateSha256 = ([regex]::Replace(
    (Get-Content -LiteralPath $fingerprintFullPath -Raw -Encoding utf8),
    '[\s:]', '')).ToLowerInvariant()
if ($certificateSha256 -notmatch '^[0-9a-f]{64}$') {
    throw "Release certificate fingerprint file must contain exactly one SHA-256 fingerprint."
}

$sha256 = (Get-FileHash -LiteralPath $apkFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "APK publication preflight: version=$VersionName+$VersionCode, bytes=$($apk.Length), sha256=$sha256"

$publishedLatestUrl = [Uri]::new($siteBaseUri, 'api/releases/android/latest.json').AbsoluteUri
try {
    $publishedManifest = Invoke-RestMethod -Uri $publishedLatestUrl -Method Get
}
catch {
    throw "Unable to read the target Site Android latest manifest: $($_.Exception.Message)"
}
if (Test-StableManifestIdentity $publishedManifest $sha256 $apk.Length $VersionName $VersionCode $certificateSha256 $ReleaseNotes) {
    $recoveryHandler = [System.Net.Http.HttpClientHandler]::new()
    $recoveryHandler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
    $recoveryClient = [System.Net.Http.HttpClient]::new($recoveryHandler)
    $recoveryClient.Timeout = [TimeSpan]::FromMinutes(15)
    try {
        $manifest = Invoke-PublicVerification $recoveryClient $siteBaseUri $sha256 $apk.Length $VersionName $VersionCode $certificateSha256 $ReleaseNotes
        Write-Output "APK was already published; public byte-for-byte verification passed: '$([Uri]::new($siteBaseUri, [string]$manifest.downloadUrl))'."
    }
    finally {
        $recoveryClient.Dispose()
        $recoveryHandler.Dispose()
    }
    exit 0
}

if (-not $AuditReportPath) {
    $AuditReportPath = [IO.Path]::ChangeExtension($apkFullPath, '.release-audit.json')
}
$auditReportFullPath = Resolve-RepoPath $AuditReportPath
$auditStartedUtc = [DateTime]::UtcNow
$powerShellExecutable = (Get-Process -Id $PID).Path
$auditScript = Join-Path $PSScriptRoot 'audit_release_apk.ps1'
& $powerShellExecutable -NoProfile -NonInteractive -File $auditScript `
    -ApkPath $apkFullPath `
    -ExpectedVersionName $VersionName `
    -ExpectedVersionCode $VersionCode `
    -ExpectedCertificateSha256 $certificateSha256 `
    -UnityVersion $UnityVersion `
    -PublishedLatestUrl $publishedLatestUrl `
    -ReportPath $auditReportFullPath
if ($LASTEXITCODE -ne 0) { throw "Fresh stable APK audit failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $auditReportFullPath -PathType Leaf)) {
    throw "Stable APK audit did not create its report."
}
$auditFile = Get-Item -LiteralPath $auditReportFullPath
if ($auditFile.LastWriteTimeUtc -lt $auditStartedUtc.AddSeconds(-2)) {
    throw "Stable APK audit report was not freshly generated."
}
$auditJson = Get-Content -LiteralPath $auditReportFullPath -Raw -Encoding utf8
$audit = $auditJson | ConvertFrom-Json
if (
    $audit.schemaVersion -ne 1 -or $audit.channel -ne 'stable-candidate' -or -not $audit.valid -or
    $audit.artifact.sha256 -ne $sha256 -or [long]$audit.artifact.downloadBytes -ne $apk.Length -or
    $audit.artifact.versionName -ne $VersionName -or [int]$audit.artifact.versionCode -ne $VersionCode -or
    $audit.artifact.certificateSha256 -ne $certificateSha256 -or [int]$audit.artifact.signerCount -ne 1 -or
    [bool]$audit.artifact.debuggable
) {
    throw "Fresh audit report does not match the stable APK publication request."
}
$auditBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($auditJson))
$releaseNotesBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($ReleaseNotes))

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(15)
try {
    $adminUri = [Uri]::new($siteBaseUri, 'api/admin/releases/android')
    $result = Invoke-ApkUpload $client $adminUri $credential.publisherToken $apkFullPath $VersionName $VersionCode $sha256 $auditBase64 $releaseNotesBase64
    $manifest = Invoke-PublicVerification $client $siteBaseUri $sha256 $apk.Length $VersionName $VersionCode $certificateSha256 $ReleaseNotes
    Write-Output "APK publication passed: reused=$($result.reused), previousDeleted=$($result.previousReleaseDeleted), download='$([Uri]::new($siteBaseUri, [string]$manifest.downloadUrl))'."
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
