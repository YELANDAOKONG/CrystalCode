[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repository = "YELANDAOKONG/CrystalCode"
$installDirectory = Join-Path $HOME ".crystal/binaries/code"
$binaryName = "CrystalCode.exe"

# Mineral palette from CrystalCode.Display Theme: grey42, lightsteelblue, indianred.
# Code points keep this file ASCII so Windows PowerShell 5.1 does not depend on a BOM.
$Esc = [char]27
$GlyphFilled = [string][char]0x2501
$GlyphEmpty = [string][char]0x2500
$GlyphDownRight = [string][char]0x250C
$GlyphDownLeft = [string][char]0x2510
$GlyphUpRight = [string][char]0x2514
$GlyphUpLeft = [string][char]0x2518
$GlyphVertical = [string][char]0x2502

$Styled = $false
$CursorHidden = $false
$Muted = ""
$Accent = ""
$FailColor = ""
$Reset = ""
$LastPercent = -1

function Restore-Cursor {
    if (-not $script:CursorHidden) {
        return
    }

    [Console]::Write("$script:Esc[?25h")
    $script:CursorHidden = $false
}

function Fail([string]$message) {
    if ($script:CursorHidden) {
        [Console]::Write("`n")
    }

    Restore-Cursor
    if ($script:Styled -and -not [Console]::IsErrorRedirected) {
        throw ($script:FailColor + $message + $script:Reset)
    }

    throw $message
}

function Enable-VirtualTerminal {
    try {
        if (-not ("CrystalCode.Install.ConsoleMode" -as [type])) {
            Add-Type -Namespace CrystalCode.Install -Name ConsoleMode -MemberDefinition @"
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr GetStdHandle(int nStdHandle);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
"@
        }

        $console = [CrystalCode.Install.ConsoleMode]
        $handle = $console::GetStdHandle(-11)
        $mode = [uint32]0
        if ($console::GetConsoleMode($handle, [ref]$mode)) {
            [void]$console::SetConsoleMode($handle, ($mode -bor 4))
        }
    }
    catch {
        # The host already accepts ANSI, or it does not expose the console mode.
    }
}

function Write-ProgressBar([long]$received, [long]$total) {
    if ($total -le 0) {
        return
    }

    $width = 50
    $percent = [int](($received * 100) / $total)
    if ($percent -gt 100) {
        $percent = 100
    }

    if ($percent -eq $script:LastPercent) {
        return
    }

    $script:LastPercent = $percent
    $on = [int](($percent * $width) / 100)
    $off = $width - $on
    $bar = $script:Accent + ($script:GlyphFilled * $on) + ($script:GlyphEmpty * $off) + (" {0,3}%" -f $percent) + $script:Reset
    [Console]::Write("`r" + $bar)
}

function Write-Card([bool]$addedToPath, [string]$archiveName, [string]$installDirectory) {
    $rule = $script:GlyphEmpty * 9
    $docsUrl = "https://github.com/$repository/blob/master/docs/user-guide.md"
    Write-Host ""
    Write-Host ($script:Muted + $script:GlyphDownRight + $rule + $script:GlyphDownLeft + $script:Reset)
    Write-Host ($script:Muted + $script:GlyphVertical + " " + $script:Accent + "crystal" + $script:Muted + " " + $script:GlyphVertical + $script:Reset)
    Write-Host ($script:Muted + $script:GlyphUpRight + $rule + $script:GlyphUpLeft + $script:Reset)
    Write-Host ""
    Write-Host ("Installed $archiveName to " + $script:Accent + $installDirectory + $script:Reset)
    if ($addedToPath) {
        Write-Host ("Added " + $script:Accent + $installDirectory + $script:Reset + " to the user PATH.")
    }
    else {
        Write-Host ($script:Accent + $installDirectory + $script:Reset + " is already configured in the user PATH.")
    }

    Write-Host "Open a new terminal to use CrystalCode from any directory."
    Write-Host ("Start Crystal Code with: " + $script:Accent + "CrystalCode" + $script:Reset)
    Write-Host ""
    Write-Host ($script:Accent + "cd <project>" + $script:Reset + "  " + $script:Muted + "# Open a repository" + $script:Reset)
    Write-Host ($script:Accent + "CrystalCode" + $script:Reset + "   " + $script:Muted + "# Start Crystal Code" + $script:Reset)
    Write-Host ""
    Write-Host ($script:Muted + "For more information visit " + $script:Reset + $docsUrl)
    Write-Host ""
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
        if (-not $script:Styled) {
            Write-Host "$directory is already configured in the user PATH."
        }

        return $false
    }

    $updatedPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $directory
    }
    else {
        "$userPath;$directory"
    }

    [Environment]::SetEnvironmentVariable("Path", $updatedPath, "User")
    if (-not $script:Styled) {
        Write-Host "Added $directory to the user PATH."
    }

    return $true
}

function Enable-ArchiveDownload {
    if ("CrystalCode.Install.ArchiveDownload" -as [type]) {
        return
    }

    # The transfer runs in C#. A PowerShell event handler has no runspace
    # on the thread that reports download progress, and that terminates the host.
    if (-not ("System.Net.Http.HttpClient" -as [type])) {
        Add-Type -AssemblyName System.Net.Http
    }

    $typeSource = @'
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CrystalCode.Install
{
    public sealed class ArchiveDownload
    {
        private long received;
        private long total;
        public volatile bool Done;
        public string Error;

        public long Received
        {
            get { return Interlocked.Read(ref received); }
        }

        public long Total
        {
            get { return Interlocked.Read(ref total); }
        }

        public void Start(string url, string destination)
        {
            var self = this;
            Task.Run(delegate
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(15);
                        var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue)
                        {
                            Interlocked.Exchange(ref self.total, response.Content.Headers.ContentLength.Value);
                        }

                        using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (var output = File.Create(destination))
                        {
                            var buffer = new byte[65536];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, read);
                                Interlocked.Add(ref self.received, read);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    self.Error = ex.Message;
                }
                finally
                {
                    self.Done = true;
                }
            });
        }
    }
}
'@
    # Passing ReferencedAssemblies replaces the default compile references.
    # Try the host defaults first, then add HttpClient for Windows PowerShell 5.1.
    try {
        Add-Type -TypeDefinition $typeSource
    }
    catch {
        $references = [System.Collections.Generic.List[string]]::new()
        foreach ($assembly in @(
                [System.Net.Http.HttpClient].Assembly,
                [object].Assembly,
                [System.IO.File].Assembly,
                [System.Threading.Interlocked].Assembly,
                [System.Threading.Tasks.Task].Assembly
            )) {
            if (-not [string]::IsNullOrEmpty($assembly.Location) -and -not $references.Contains($assembly.Location)) {
                [void]$references.Add($assembly.Location)
            }
        }

        Add-Type -TypeDefinition $typeSource -ReferencedAssemblies $references
    }
}

function Receive-CrystalArchive([string]$url, [string]$destination, [string]$archiveName) {
    Enable-ArchiveDownload
    $download = [CrystalCode.Install.ArchiveDownload]::new()
    [Console]::Write("$script:Esc[?25l")
    $script:CursorHidden = $true
    try {
        $download.Start($url, $destination)
        while (-not $download.Done) {
            if ($download.Total -gt 0) {
                Write-ProgressBar $download.Received $download.Total
            }

            Start-Sleep -Milliseconds 50
        }

        if (-not [string]::IsNullOrEmpty($download.Error)) {
            Fail "Could not download $archiveName from the latest release."
        }

        $finalLength = (Get-Item -LiteralPath $destination).Length
        $script:LastPercent = -1
        Write-ProgressBar $finalLength $finalLength
        Write-Host ""
    }
    finally {
        Restore-Cursor
    }
}

if (-not [Console]::IsOutputRedirected -and
    -not [Console]::IsErrorRedirected -and
    [string]::IsNullOrEmpty($env:NO_COLOR) -and
    $env:TERM -ne "dumb") {
    $Styled = $true
    $Muted = "$Esc[38;5;242m"
    $Reset = "$Esc[0m"
    if ($env:COLORTERM -eq "truecolor" -or $env:COLORTERM -eq "24bit") {
        $Accent = "$Esc[38;2;176;196;222m"
        $FailColor = "$Esc[38;2;205;92;92m"
    }
    else {
        $Accent = "$Esc[38;5;152m"
        $FailColor = "$Esc[38;5;167m"
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Fail "This installer is for Windows."
}

$architecture = [Environment]::GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")
if ([string]::IsNullOrWhiteSpace($architecture)) {
    $architecture = [Environment]::GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")
}

if ($architecture -ne "AMD64") {
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
    if ($Styled) {
        Enable-VirtualTerminal
        Write-Host ($Muted + "Installing Crystal Code..." + $Reset)
        Write-Host ""
        Receive-CrystalArchive $downloadUrl $archivePath $archiveName
    }
    else {
        Write-Host "Downloading $archiveName..."
        $webClient = [System.Net.WebClient]::new()
        try {
            $webClient.DownloadFile($downloadUrl, $archivePath)
        }
        finally {
            $webClient.Dispose()
        }
    }

    if (-not $Styled) {
        Write-Host "Extracting $archiveName..."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory -Force
    $publishedBinary = Get-ChildItem -LiteralPath $extractionDirectory -Filter $binaryName -File -Recurse |
        Select-Object -First 1

    if ($null -eq $publishedBinary) {
        Fail "The archive does not contain $binaryName."
    }

    $publishedDirectory = $publishedBinary.DirectoryName
    if (-not $Styled) {
        Write-Host "Installing Crystal Code files..."
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $publishedDirectory "*") -Destination $installDirectory -Recurse -Force

    if ($Styled) {
        $addedToPath = Add-UserPath $installDirectory
        Write-Card -addedToPath $addedToPath -archiveName $archiveName -installDirectory $installDirectory
    }
    else {
        Write-Host "Installed $archiveName to $installDirectory"
        Write-Host "Configuring the user PATH..."
        Add-UserPath $installDirectory | Out-Null
        Write-Host "Open a new terminal to use CrystalCode from any directory."
        Write-Host "Start Crystal Code with: CrystalCode"
    }
}
finally {
    if ($CursorHidden) {
        [Console]::Write("`n")
    }

    Restore-Cursor
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
