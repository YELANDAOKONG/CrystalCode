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

    $publishedDirectory = $publishedBinary.DirectoryName
    Write-Host "Installing Crystal Code files..."
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $publishedDirectory "*") -Destination $installDirectory -Recurse -Force

    Write-Host "Installed $archiveName to $installDirectory"
    Write-Host "Configuring the user PATH..."
    Add-UserPath $installDirectory
    Write-Host "Open a new terminal to use CrystalCode from any directory."
    Write-Host "Start Crystal Code with: CrystalCode"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
