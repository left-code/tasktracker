<#
.SYNOPSIS
    Installs or updates TaskTracker for the current user from a GitHub release.
.DESCRIPTION
    Downloads the public standalone release without GitHub CLI or authentication.
    Installs the standalone exe and adds its directory to the user's PATH.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+\.\d+([+-][0-9A-Za-z.-]+)?$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$releaseUrl = 'https://github.com/vlkozl/tasktracker/releases'
if ($Version) { $downloadUrl = "$releaseUrl/download/$Version/TaskTracker.exe" }
else { $downloadUrl = "$releaseUrl/latest/download/TaskTracker.exe" }

$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\TaskTracker'
$destination = Join-Path $installDirectory 'TaskTracker.exe'
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
$download = Join-Path $installDirectory ('download-' + [Guid]::NewGuid().ToString('N') + '.tmp')

try {
    # Older Windows PowerShell installations may not enable TLS 1.2 by default.
    $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
    try {
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
        Write-Host "Downloading $downloadUrl"
        Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $download
    }
    catch {
        throw "Download failed. Check your internet connection and that the release includes TaskTracker.exe. $($_.Exception.Message)"
    }
    finally {
        [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
    }
    if (-not (Test-Path -LiteralPath $download) -or (Get-Item -LiteralPath $download).Length -eq 0) {
        throw 'The release download is missing or empty.'
    }

    # Stage on the same volume so replacing an existing installation is atomic.
    try {
        if (Test-Path -LiteralPath $destination) {
            [IO.File]::Replace($download, $destination, [System.Management.Automation.Language.NullString]::Value)
        }
        else {
            [IO.File]::Move($download, $destination)
        }
    }
    catch {
        throw "Could not install TaskTracker. Close TaskTracker if it is running and retry. $($_.Exception.Message)"
    }
}
finally {
    if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (@($userPath -split ';' | Where-Object { $_.Trim().TrimEnd('\') -ieq $installDirectory }).Count -eq 0) {
    $newPath = $installDirectory
    if ($userPath) { $newPath = $userPath.TrimEnd(';') + ';' + $installDirectory }
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
}

Write-Host "Installed $destination"
Write-Host 'Open a new terminal, then run TaskTracker --help.'
