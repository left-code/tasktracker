<#
.SYNOPSIS
    Makes this PC trust TaskTracker's self-signed certificate (run once per PC, no admin needed).

.DESCRIPTION
    Imports the public certificate into your user's Trusted Root and Trusted Publishers stores.
    Windows shows a security dialog for the Trusted Root import; check that the thumbprint it
    shows matches the one printed here, then click Yes.

    Trusting a certificate means trusting everything signed with it, so only do this for a
    certificate you created yourself.
#>
[CmdletBinding()]
param(
    [string] $CerPath
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults, so resolve here.
if (-not $CerPath) { $CerPath = Join-Path $PSScriptRoot '..\signing\tasktracker-codesign.cer' }

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (Resolve-Path $CerPath).Path
Write-Host "Certificate: $($cert.Subject)"
Write-Host "Thumbprint:  $($cert.Thumbprint)"
Write-Host "Expires:     $($cert.NotAfter.ToString('d'))"

Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null

Write-Host 'Trusted. Signed TaskTracker.exe files now show this certificate as a valid publisher.'
