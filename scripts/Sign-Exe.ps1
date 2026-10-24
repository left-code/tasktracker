<#
.SYNOPSIS
    Signs the published TaskTracker.exe with the certificate from New-SigningCert.ps1.

.DESCRIPTION
    Adds a timestamp, so the signature stays valid after the certificate itself expires.
    The timestamp server must be reachable over the internet.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [string] $Subject = 'CN=Vladimir Kozlov (TaskTracker code signing)',
    [string] $TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults, so resolve here.
if (-not $Path) { $Path = Join-Path $PSScriptRoot '..\publish\TaskTracker.exe' }

if (-not (Test-Path $Path)) {
    throw "Not found: $Path. Run the 'publish (single exe)' task first."
}

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if (-not $cert) {
    throw "No valid code-signing certificate '$Subject' in Cert:\CurrentUser\My. Run scripts\New-SigningCert.ps1 first."
}

$result = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer
if (-not $result.SignerCertificate -or $result.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
    throw "Signing failed: $($result.StatusMessage)"
}
if (-not $result.TimeStamperCertificate) {
    # Without a timestamp the signature becomes invalid when the certificate expires.
    throw "Signed, but without a timestamp from $TimestampServer. Check the internet connection and sign again."
}

# 'UnknownError' only means this PC doesn't trust the self-signed root yet (see Trust-SigningCert.ps1).
Write-Host "Signed $((Resolve-Path $Path).Path)"
Write-Host "  signer:    $($cert.Subject) ($($cert.Thumbprint))"
Write-Host "  timestamp: $($result.TimeStamperCertificate.Subject)"
Write-Host "  status:    $($result.Status) - $($result.StatusMessage)"
