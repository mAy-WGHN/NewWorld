<#
.SYNOPSIS
  Creates a key pair for service-to-service authentication (ECDSA P-256) in the format of the ServiceAuth configuration.
.DESCRIPTION
  Writes <Name>.private.key (base64 PKCS#8, secret: only for the owning service, User Secrets / Key Vault) and
  <Name>.public.key (base64 SubjectPublicKeyInfo, goes to every service that accepts this one) into -OutputDirectory.
  Nothing is printed to the console. Delete the private file after it has been stored. See docs/service-auth.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Name,

    [string] $OutputDirectory = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$privateFile = Join-Path $OutputDirectory "$Name.private.key"
$publicFile = Join-Path $OutputDirectory "$Name.public.key"
foreach ($file in $privateFile, $publicFile) {
    if (Test-Path $file) { throw "$file exists already; refusing to overwrite a key." }
}

$key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
try {
    [IO.File]::WriteAllText($privateFile, [Convert]::ToBase64String($key.ExportPkcs8PrivateKey()))
    [IO.File]::WriteAllText($publicFile, [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo()))
}
finally {
    $key.Dispose()
}

Write-Host "Written: $publicFile (public) and $privateFile (private, keep secret, delete after storing)."
