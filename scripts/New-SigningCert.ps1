<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for TaskTracker (one-time setup).

.DESCRIPTION
    The certificate and its private key go into your personal store (Cert:\CurrentUser\My) and
    never leave this PC. Only the public part is exported, to signing\tasktracker-codesign.cer,
    so other PCs can trust the signature (see Trust-SigningCert.ps1). The signing\ folder is
    git-ignored; copy the .cer to other PCs yourself.
#>
[CmdletBinding()]
param(
    [string] $Subject = 'CN=Vladimir Kozlov (TaskTracker code signing)',
    [int] $Years = 3,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$cerPath = Join-Path $PSScriptRoot '..\signing\tasktracker-codesign.cer'

$existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) }
if ($existing -and -not $Force) {
    Write-Host "A valid certificate already exists: $($existing[0].Thumbprint) (expires $($existing[0].NotAfter.ToString('d')))."
    Write-Host 'Use -Force to create another one.'
    return
}

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName 'TaskTracker code signing' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears($Years)

New-Item -ItemType Directory -Force (Split-Path $cerPath) | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host "Created certificate $($cert.Thumbprint), valid until $($cert.NotAfter.ToString('d'))."
Write-Host "Public certificate exported to $((Resolve-Path $cerPath).Path)"
