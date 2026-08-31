[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repository = "YELANDAOKONG/CrystalCode"
$installDirectory = Join-Path $HOME ".crystal/binaries/code"
$binaryName = "CrystalCode.exe"

function Fail([string]$message) {
    throw $message
}

function Add-UserPath([string]$directory) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathEntries = @()

    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $pathEntries = $userPath -split ";" | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        }
    }

    if ($pathEntries -contains $directory) {
        Write-Host "$directory is already configured in the user PATH."
        return
    }

    $updatedPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $directory
    }
    else {
        "$userPath;$directory"
    }

    [Environment]::SetEnvironmentVariable("Path", $updatedPath, "User")
    Write-Host "Added $directory to the user PATH."
    Write-Host "Open a new terminal to use $binaryName from any directory."
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Fail "This installer is for Windows."
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($architecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    Fail "Unsupported Windows architecture: $architecture"
}

$asset = "windows-x64"
$archiveName = "CrystalCode-$asset.zip"
$downloadUrl = "https://github.com/$repository/releases/latest/download/$archiveName"
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("CrystalCode-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $temporaryDirectory $archiveName
$extractionDirectory = Join-Path $temporaryDirectory "extracted"
$destinationPath = Join-Path $installDirectory $binaryName
$stagedPath = Join-Path $installDirectory (".CrystalCode-" + [Guid]::NewGuid().ToString("N") + ".exe")

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "Downloading $archiveName..."
    $webClient = [System.Net.WebClient]::new()
    try {
        $webClient.DownloadFile($downloadUrl, $archivePath)
    }
    finally {
        $webClient.Dispose()
    }

    Write-Host "Extracting $archiveName..."
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory -Force
    $publishedBinary = Get-ChildItem -LiteralPath $extractionDirectory -Filter $binaryName -File -Recurse |
        Select-Object -First 1

    if ($null -eq $publishedBinary) {
        Fail "The archive does not contain $binaryName."
    }

    Write-Host "Installing $binaryName..."
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $publishedBinary.FullName -Destination $stagedPath -Force

    if (Test-Path -LiteralPath $destinationPath) {
        [System.IO.File]::Replace($stagedPath, $destinationPath, $null)
    }
    else {
        [System.IO.File]::Move($stagedPath, $destinationPath)
    }

    Write-Host "Installed $archiveName to $destinationPath"
    Write-Host "Configuring the user PATH..."
    Add-UserPath $installDirectory
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
