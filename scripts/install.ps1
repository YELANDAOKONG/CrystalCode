[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repository = "YELANDAOKONG/CrystalHarness"
$installDirectory = Join-Path $HOME ".crystal/binaries/code"
$binaryName = "CrystalHarness.exe"

function Fail([string]$message) {
    throw $message
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Fail "This installer is for Windows."
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($architecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    Fail "Unsupported Windows architecture: $architecture"
}

$asset = "windows-x64"
$archiveName = "CrystalHarness-$asset.zip"
$downloadUrl = "https://github.com/$repository/releases/latest/download/$archiveName"
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("CrystalHarness-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $temporaryDirectory $archiveName
$extractionDirectory = Join-Path $temporaryDirectory "extracted"
$destinationPath = Join-Path $installDirectory $binaryName
$stagedPath = Join-Path $installDirectory (".CrystalHarness-" + [Guid]::NewGuid().ToString("N") + ".exe")

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $webClient = [System.Net.WebClient]::new()
    try {
        $webClient.DownloadFile($downloadUrl, $archivePath)
    }
    finally {
        $webClient.Dispose()
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory -Force
    $publishedBinary = Get-ChildItem -LiteralPath $extractionDirectory -Filter $binaryName -File -Recurse |
        Select-Object -First 1

    if ($null -eq $publishedBinary) {
        Fail "The archive does not contain $binaryName."
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $publishedBinary.FullName -Destination $stagedPath -Force

    if (Test-Path -LiteralPath $destinationPath) {
        [System.IO.File]::Replace($stagedPath, $destinationPath, $null)
    }
    else {
        [System.IO.File]::Move($stagedPath, $destinationPath)
    }

    Write-Host "Installed $archiveName to $destinationPath"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
