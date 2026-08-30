param(
    [string]$PrimaryKeystorePath,
    [string[]]$BackupKeystorePaths,
    [string]$KeyAlias = "universal-gacha-release",
    [string]$DistinguishedName = "CN=Universal Gacha Simulator, OU=Personal Release, O=Basic Game Studio, L=Kuala Lumpur, ST=Kuala Lumpur, C=MY",
    [int]$ValidityDays = 10000,
    [string]$UnityVersion = "6000.0.73f1",
    [string]$KeytoolPath,
    [string]$VersionName,
    [int]$VersionCode,
    [switch]$BuildCandidate,
    [switch]$UseExisting,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$secretEnvironmentName = "UGS_RELEASE_SIGNING_SECRET"

function Test-IsPathUnder {
    param([string]$Candidate, [string]$Root)
    $candidateFull = [IO.Path]::GetFullPath($Candidate)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
}

function Get-NormalizedTargets {
    param([string]$Primary, [string[]]$Backups)
    if ([string]::IsNullOrWhiteSpace($Primary)) {
        throw "PrimaryKeystorePath is required."
    }
    if ($null -eq $Backups -or $Backups.Count -ne 2) {
        throw "Exactly two local backup keystore paths are required."
    }

    $targets = @($Primary) + @($Backups)
    $normalized = @($targets | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_)) {
            throw "Release signing paths cannot be empty."
        }
        [IO.Path]::GetFullPath($_)
    })
    if (@($normalized | Sort-Object -Unique).Count -ne 3) {
        throw "Primary and backup keystore paths must be distinct."
    }
    foreach ($target in $normalized) {
        if (Test-IsPathUnder $target $repoRoot) {
            throw "Release signing files must be stored outside the Git repository."
        }
        $extension = [IO.Path]::GetExtension($target)
        if ($extension -notin @(".p12", ".pfx")) {
            throw "Release signing files must use .p12 or .pfx extension."
        }
    }
    $volumeCount = @($normalized | ForEach-Object {
        [IO.Path]::GetPathRoot($_).ToUpperInvariant()
    } | Sort-Object -Unique).Count
    if ($volumeCount -lt 2) {
        throw "The primary and local backups must span at least two filesystem volumes."
    }
    return $normalized
}

function Invoke-CheckedCommand {
    param([string]$Executable, [string[]]$Arguments, [string]$FailureMessage)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& $Executable @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0) {
        throw "$FailureMessage (exit=$exitCode)."
    }
    return $output
}

function Protect-PrivatePath {
    param([string]$Path, [bool]$Directory)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $arguments = if ($Directory) {
        @($Path, "/inheritance:r", "/grant:r", "${identity}:(OI)(CI)F", "SYSTEM:(OI)(CI)F")
    }
    else {
        @($Path, "/inheritance:r", "/grant:r", "${identity}:F", "SYSTEM:F")
    }
    $output = @(& "$env:SystemRoot\System32\icacls.exe" @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restrict release signing ACL for '$Path'."
    }
}

function Read-ConfirmedPassword {
    $first = Read-Host -Prompt "Release keystore password (20+ characters; store it in your password manager)" -AsSecureString
    $second = Read-Host -Prompt "Confirm release keystore password" -AsSecureString
    $firstPointer = [IntPtr]::Zero
    $secondPointer = [IntPtr]::Zero
    try {
        $firstPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($first)
        $secondPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($second)
        $firstPlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($firstPointer)
        $secondPlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secondPointer)
        if ($firstPlain.Length -lt 20) {
            throw "Release keystore password must contain at least 20 characters."
        }
        if (-not [string]::Equals($firstPlain, $secondPlain, [StringComparison]::Ordinal)) {
            throw "Release keystore password confirmation did not match."
        }
        return $firstPlain
    }
    finally {
        $firstPlain = $null
        $secondPlain = $null
        if ($firstPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($firstPointer)
        }
        if ($secondPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secondPointer)
        }
        if ($first -is [IDisposable]) { $first.Dispose() }
        if ($second -is [IDisposable]) { $second.Dispose() }
    }
}

function Get-CertificateFingerprint {
    param([string]$Keytool, [string]$Keystore, [string]$Alias)
    $temporaryCertificate = Join-Path ([IO.Path]::GetTempPath()) `
        ("ugs-release-certificate-" + [Guid]::NewGuid().ToString("N") + ".der")
    try {
        Invoke-CheckedCommand $Keytool @(
            "-exportcert",
            "-keystore", $Keystore,
            "-storetype", "PKCS12",
            "-storepass:env", $secretEnvironmentName,
            "-alias", $Alias,
            "-file", $temporaryCertificate) `
            "Unable to export the release signing certificate" | Out-Null
        return (Get-FileHash -LiteralPath $temporaryCertificate -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    finally {
        if (Test-Path -LiteralPath $temporaryCertificate -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryCertificate -Force
        }
    }
}

function Assert-KeyIdentity {
    param([string]$Keytool, [string]$Keystore, [string]$Alias)
    Invoke-CheckedCommand $Keytool @(
        "-list",
        "-keystore", $Keystore,
        "-storetype", "PKCS12",
        "-storepass:env", $secretEnvironmentName,
        "-alias", $Alias) "Release signing alias validation failed" | Out-Null
}

function Write-RecoveryManifest {
    param(
        [string]$ManifestPath,
        [string[]]$Targets,
        [string]$Alias,
        [string]$Subject,
        [string]$CertificateSha256,
        [string]$KeystoreSha256)
    $copies = for ($index = 0; $index -lt $Targets.Count; $index++) {
        [ordered]@{
            role = if ($index -eq 0) { "primary" } else { "local-backup-$index" }
            path = $Targets[$index]
            volume = [IO.Path]::GetPathRoot($Targets[$index])
            sha256 = $KeystoreSha256
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        productId = "universal-gacha-simulator"
        createdAtUtc = [DateTime]::UtcNow.ToString("o")
        keyAlias = $Alias
        keystoreType = "PKCS12"
        keyAlgorithm = "RSA"
        keySize = 4096
        signatureAlgorithm = "SHA256withRSA"
        certificateSubject = $Subject
        certificateSha256 = $CertificateSha256
        keystoreSha256 = $KeystoreSha256
        passwordStored = $false
        offlineBackupConfirmed = $false
        copies = @($copies)
        recoveryWarning = "Local copies are not offline backups. Store the password in a password manager and copy one verified keystore to independent offline storage."
    }
    [IO.File]::WriteAllText(
        $ManifestPath,
        ($manifest | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
    Protect-PrivatePath $ManifestPath $false
}

function Invoke-SelfTest {
    $passed = 0
    if (-not (Test-IsPathUnder (Join-Path $repoRoot "Assets\fixture.p12") $repoRoot) -or
        (Test-IsPathUnder "D:\private\fixture.p12" $repoRoot)) {
        throw "Self-test failed: repository path boundary."
    }
    $passed++

    $outside = Get-NormalizedTargets "C:\private\primary.p12" @(
        "C:\private\backup-a.p12",
        "D:\private\backup-b.p12")
    if ($outside.Count -ne 3) {
        throw "Self-test failed: valid signing targets."
    }
    $passed++

    foreach ($invalid in @(
        { Get-NormalizedTargets "C:\private\primary.p12" @("C:\private\backup.p12") },
        { Get-NormalizedTargets "C:\private\primary.p12" @("C:\private\primary.p12", "D:\private\backup.p12") },
        { Get-NormalizedTargets "C:\private\primary.p12" @("C:\private\backup-a.p12", "C:\private\backup-b.p12") },
        { Get-NormalizedTargets (Join-Path $repoRoot "secret.p12") @("C:\private\a.p12", "D:\private\b.p12") })) {
        $rejected = $false
        try { & $invalid | Out-Null } catch { $rejected = $true }
        if (-not $rejected) {
            throw "Self-test failed: unsafe signing targets were accepted."
        }
    }
    $passed++

    Write-Output "Release signing initialization self-test passed: $passed/3."
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if ([string]::IsNullOrWhiteSpace($KeyAlias) -or $KeyAlias -notmatch '^[a-z0-9][a-z0-9._-]{2,63}$') {
    throw "KeyAlias must contain 3-64 lowercase ASCII letters, digits, dot, underscore, or hyphen."
}
if ($ValidityDays -lt 3650 -or $ValidityDays -gt 20000) {
    throw "ValidityDays must be between 3650 and 20000."
}
if ($BuildCandidate -and ([string]::IsNullOrWhiteSpace($VersionName) -or $VersionCode -lt 2)) {
    throw "BuildCandidate requires VersionName and VersionCode >= 2."
}

$targets = Get-NormalizedTargets $PrimaryKeystorePath $BackupKeystorePaths
$primary = $targets[0]
if ([string]::IsNullOrWhiteSpace($KeytoolPath)) {
    $KeytoolPath = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe"
}
$keytoolFullPath = [IO.Path]::GetFullPath($KeytoolPath)
if (-not (Test-Path -LiteralPath $keytoolFullPath -PathType Leaf)) {
    throw "Unity embedded keytool was not found: $keytoolFullPath"
}

if ($UseExisting) {
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "Existing release signing copy was not found: $target"
        }
    }
}
else {
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target) {
            throw "Release signing target already exists; refusing to overwrite: $target"
        }
    }
}

$password = $null
$originalSecret = [Environment]::GetEnvironmentVariable($secretEnvironmentName, "Process")
$originalStorePassword = [Environment]::GetEnvironmentVariable("TCG_ANDROID_KEYSTORE_PASSWORD", "Process")
$originalKeyPassword = [Environment]::GetEnvironmentVariable("TCG_ANDROID_KEY_PASSWORD", "Process")
$createdPrimary = $false
try {
    $password = Read-ConfirmedPassword
    [Environment]::SetEnvironmentVariable($secretEnvironmentName, $password, "Process")
    [Environment]::SetEnvironmentVariable("TCG_ANDROID_KEYSTORE_PASSWORD", $password, "Process")
    [Environment]::SetEnvironmentVariable("TCG_ANDROID_KEY_PASSWORD", $password, "Process")

    if (-not $UseExisting) {
        foreach ($target in $targets) {
            $directory = Split-Path -Parent $target
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            Protect-PrivatePath $directory $true
        }
        Invoke-CheckedCommand $keytoolFullPath @(
            "-genkeypair",
            "-keystore", $primary,
            "-storetype", "PKCS12",
            "-storepass:env", $secretEnvironmentName,
            "-keypass:env", $secretEnvironmentName,
            "-alias", $KeyAlias,
            "-keyalg", "RSA",
            "-keysize", "4096",
            "-sigalg", "SHA256withRSA",
            "-validity", $ValidityDays.ToString([Globalization.CultureInfo]::InvariantCulture),
            "-dname", $DistinguishedName,
            "-noprompt") "Release signing key generation failed" | Out-Null
        $createdPrimary = $true
        Protect-PrivatePath $primary $false
        for ($index = 1; $index -lt $targets.Count; $index++) {
            Copy-Item -LiteralPath $primary -Destination $targets[$index]
            Protect-PrivatePath $targets[$index] $false
        }
    }

    foreach ($target in $targets) {
        Protect-PrivatePath $target $false
    }

    Assert-KeyIdentity $keytoolFullPath $primary $KeyAlias
    $certificateSha256 = Get-CertificateFingerprint $keytoolFullPath $primary $KeyAlias
    $hashes = @($targets | ForEach-Object {
        (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    if (@($hashes | Sort-Object -Unique).Count -ne 1) {
        throw "Release signing copies do not have identical SHA-256 hashes."
    }
    $keystoreSha256 = $hashes[0]
    $manifestPath = Join-Path (Split-Path -Parent $primary) "release-signing-recovery.json"
    Write-RecoveryManifest $manifestPath $targets $KeyAlias $DistinguishedName `
        $certificateSha256 $keystoreSha256

    Write-Output "Release signing identity verified."
    Write-Output "Certificate SHA-256: $certificateSha256"
    Write-Output "Keystore SHA-256: $keystoreSha256"
    Write-Output "Primary and two local copies are hash-identical."
    Write-Output "Offline backup remains unconfirmed; do not treat this as release custody completion."

    if ($BuildCandidate) {
        & (Join-Path $PSScriptRoot "build_release_apk.ps1") `
            -VersionName $VersionName `
            -VersionCode $VersionCode `
            -KeystorePath $primary `
            -KeyAlias $KeyAlias `
            -ExpectedCertificateSha256 $certificateSha256 `
            -UnityVersion $UnityVersion `
            -NonInteractive
        if ($LASTEXITCODE -ne 0) {
            throw "Signed Android candidate build failed with exit code $LASTEXITCODE."
        }
    }
}
catch {
    if (-not $UseExisting -and -not $createdPrimary) {
        foreach ($target in $targets) {
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Remove-Item -LiteralPath $target -Force
            }
        }
    }
    throw
}
finally {
    [Environment]::SetEnvironmentVariable($secretEnvironmentName, $originalSecret, "Process")
    [Environment]::SetEnvironmentVariable("TCG_ANDROID_KEYSTORE_PASSWORD", $originalStorePassword, "Process")
    [Environment]::SetEnvironmentVariable("TCG_ANDROID_KEY_PASSWORD", $originalKeyPassword, "Process")
    $password = $null
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
