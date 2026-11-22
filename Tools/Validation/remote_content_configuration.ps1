function Read-RemoteContentConfiguration {
    param(
        [string]$Json,
        [string]$SourceName
    )

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "Remote content configuration '$SourceName' is empty."
    }

    try {
        $config = $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Remote content configuration '$SourceName' is not valid JSON: $($_.Exception.Message)"
    }

    if ($null -eq $config -or $config -is [Array] -or $config -is [string] -or $config -is [ValueType]) {
        throw "Remote content configuration '$SourceName' must contain one JSON object."
    }

    $allowed = @("catalogUrl", "timeoutSeconds", "maxCatalogBytes", "trustedCatalogKeys")
    $properties = @($config.PSObject.Properties.Name)
    $unknown = @($properties | Where-Object { $allowed -notcontains $_ })
    if ($unknown.Count -gt 0) {
        throw "Remote content configuration contains unsupported fields: $($unknown -join ', '). Do not put credentials or secrets in the phone configuration."
    }
    if ($properties -notcontains "catalogUrl" -or [string]::IsNullOrWhiteSpace([string]$config.catalogUrl)) {
        throw "Remote content configuration requires catalogUrl."
    }

    $catalogUri = $null
    if (-not [Uri]::TryCreate(([string]$config.catalogUrl).Trim(), [UriKind]::Absolute, [ref]$catalogUri)) {
        throw "catalogUrl must be an absolute URI."
    }
    if ($catalogUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "Android remote catalogUrl must use HTTPS."
    }
    if (-not [string]::IsNullOrEmpty($catalogUri.UserInfo)) {
        throw "catalogUrl cannot contain embedded credentials."
    }
    if (-not [string]::IsNullOrEmpty($catalogUri.Fragment)) {
        throw "catalogUrl cannot contain a fragment."
    }

    if ($properties -contains "timeoutSeconds") {
        $timeout = 0
        if (-not [int]::TryParse([string]$config.timeoutSeconds, [ref]$timeout) -or $timeout -lt 1 -or $timeout -gt 120) {
            throw "timeoutSeconds must be an integer from 1 through 120."
        }
    }
    if ($properties -contains "maxCatalogBytes") {
        $maximumBytes = 0
        if (-not [int]::TryParse([string]$config.maxCatalogBytes, [ref]$maximumBytes) -or
            $maximumBytes -lt 1024 -or $maximumBytes -gt (4 * 1024 * 1024)) {
            throw "maxCatalogBytes must be an integer from 1024 through 4194304."
        }
    }

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
        }
    }
    return $config
}
