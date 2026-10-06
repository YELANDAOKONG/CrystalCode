# Publish this checkout as Release into build/ and install it under
# ~/.crystal/binaries/code/. The publish arguments match the release
# workflow. That workflow writes ./publish for the GitHub asset; this
# script writes the ignored build/ directory.

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$BinaryName = "CrystalCode.exe"
# A self-contained single-file publish includes the runtime and is far
# larger than a framework-dependent apphost.
$MinimumBinaryBytes = 10000000
$ProfileChanged = $false

function Fail([string]$message) {
    [Console]::Error.WriteLine("Check failed: $message")
    exit 1
}

function Step([string]$name, [string]$title) {
    Write-Host ""
    Write-Host "[$name] $title"
}

function Detail([string]$message) {
    Write-Host "  $message"
}

function Test-ReparsePoint([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return $false
    }

    $item = Get-Item -LiteralPath $path -Force
    return [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

function Clear-InstallTree([string]$target, [string]$label) {
    if (-not (Test-Path -LiteralPath $target)) {
        Detail "No previous $label."
        return
    }

    if (Test-ReparsePoint $target) {
        Fail "Refusing to remove $target because it is a symbolic link."
    }

    Detail "Removing previous $label."
    Remove-Item -LiteralPath $target -Recurse -Force
}

function Install-StagedDirectory([string]$installPath, [string]$stagingPath) {
    $parent = Split-Path -Parent $installPath
    $previousPath = Join-Path $parent "code.previous"
    $expectedInstall = Join-Path $parent "code"
    $expectedStaging = Join-Path $parent "code.new"
    $backupSuffix = [System.IO.Path]::Combine(".crystal", "binaries", "code.previous")

    if ($installPath -ne $expectedInstall) {
        Fail "Refusing to replace unexpected install directory: $installPath"
    }

    if ($stagingPath -ne $expectedStaging) {
        Fail "Refusing to replace unexpected staging directory: $stagingPath"
    }

    if (-not $previousPath.EndsWith($backupSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail "Refusing to replace unexpected backup directory: $previousPath"
    }

    foreach ($candidate in @($previousPath, $installPath, $stagingPath)) {
        if (Test-ReparsePoint $candidate) {
            Fail "Refusing to replace $candidate because it is a symbolic link."
        }
    }

    if ((Test-Path -LiteralPath $previousPath) -and -not (Test-Path -LiteralPath $previousPath -PathType Container)) {
        Fail "$previousPath exists and is not a directory."
    }

    # An interrupted replace leaves the live directory missing and the
    # previous tree beside it. Put that tree back before trying again.
    if ((Test-Path -LiteralPath $previousPath -PathType Container) -and -not (Test-Path -LiteralPath $installPath)) {
        Detail "Restoring the install directory after an interrupted replace."
        Move-Item -LiteralPath $previousPath -Destination $installPath
    }

    if (Test-Path -LiteralPath $previousPath) {
        Clear-InstallTree $previousPath "install backup"
    }

    $replaceFailed = $false
    $restoredPrevious = $false
    try {
        if (Test-Path -LiteralPath $installPath) {
            Detail "Moving the current install aside."
            Move-Item -LiteralPath $installPath -Destination $previousPath
        }

        Detail "Moving the staged install into place."
        Move-Item -LiteralPath $stagingPath -Destination $installPath
    }
    catch {
        $replaceFailed = $true
    }
    finally {
        $installMissing = -not (Test-Path -LiteralPath $installPath)
        $backupRemains = Test-Path -LiteralPath $previousPath -PathType Container
        $stagedRemains = Test-Path -LiteralPath $stagingPath -PathType Container
        if ($installMissing -and $backupRemains -and $stagedRemains) {
            Move-Item -LiteralPath $previousPath -Destination $installPath
            $restoredPrevious = $true
            if (-not $replaceFailed) {
                [Console]::Error.WriteLine("Restored the previous install at $installPath.")
            }
        }
    }

    if ($replaceFailed) {
        if ($restoredPrevious) {
            Fail "Could not move $stagingPath to $installPath. Restored the previous install."
        }

        if (Test-Path -LiteralPath $previousPath -PathType Container) {
            Fail "Could not move $stagingPath to $installPath. The previous install is at $previousPath."
        }

        Fail "Could not move $stagingPath to $installPath."
    }

    if (Test-Path -LiteralPath $previousPath) {
        Detail "Removing the previous install."
        if (Test-ReparsePoint $previousPath) {
            Detail "Could not remove $previousPath because it is a symbolic link. The new install is in place."
        }
        else {
            try {
                Remove-Item -LiteralPath $previousPath -Recurse -Force -ErrorAction Stop
            }
            catch {
                Detail "Could not remove $previousPath. The new install is in place."
            }
        }
    }
}

function Test-CrystalCodeStopped {
    Step "check" "Crystal Code process"
    $running = @(Get-Process -Name "CrystalCode" -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Fail "Crystal Code is running. Exit it, then run this script again."
    }

    Detail "Check passed: CrystalCode is not running."
}

function Add-UserPath([string]$directory) {
    Step "check" "User PATH"
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathEntries = @()

    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $pathEntries = @($userPath -split ";" | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        })
    }

    $alreadyPresent = $false
    foreach ($entry in $pathEntries) {
        if ($entry -eq $directory) {
            $alreadyPresent = $true
            break
        }
    }

    if ($alreadyPresent) {
        Detail "PATH entry: present"
        Detail "Check passed: profile already contains the install directory."
        return
    }

    Detail "PATH entry: missing"
    $updatedPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $directory
    }
    else {
        "$userPath;$directory"
    }

    [Environment]::SetEnvironmentVariable("Path", $updatedPath, "User")
    $script:ProfileChanged = $true
    Detail "Check passed: added $directory to the user PATH."
}

if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
    Fail "Run this script as a file: powershell -File scripts/install-local.ps1"
}

if ([string]::IsNullOrWhiteSpace($HOME)) {
    Fail "HOME must be set."
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..")).Path
$publishDirectory = Join-Path $repositoryRoot "build"
$crystalRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "..\Crystal"))
$installDirectory = Join-Path $HOME ".crystal\binaries\code"
$stagingDirectory = Join-Path (Split-Path -Parent $installDirectory) "code.new"
$runtimeId = "win-x64"

Write-Host "Crystal Code local install"
Write-Host "Repository: $repositoryRoot"

Step "check" "Repository"
Set-Location -LiteralPath $repositoryRoot
Detail "Working directory: $repositoryRoot"

$solutionPath = Join-Path $repositoryRoot "CrystalCode.sln"
if (-not (Test-Path -LiteralPath $solutionPath)) {
    Fail "Missing solution: $solutionPath"
}
Detail "Solution: $solutionPath"

$projectPath = Join-Path $repositoryRoot "CrystalCode\CrystalCode.csproj"
if (-not (Test-Path -LiteralPath $projectPath)) {
    Fail "Missing project: $projectPath"
}
Detail "Project: $projectPath"
Detail "Check passed: repository files exist."

Step "check" ".NET SDK"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    Fail "Required command not found: dotnet"
}
Detail "dotnet: $($dotnetCommand.Source)"

$sdkVersion = (& dotnet --version)
if ($LASTEXITCODE -ne 0) {
    Fail "Could not read the .NET SDK version."
}
$sdkVersion = "$sdkVersion".Trim()
Detail "Version: $sdkVersion"
if ($sdkVersion -notlike "10.*") {
    Fail "The release workflow uses the .NET 10 SDK. This machine selected $sdkVersion."
}
Detail "Check passed: SDK major version is 10."

Step "check" "Crystal sibling"
if (-not (Test-Path -LiteralPath $crystalRoot -PathType Container)) {
    Fail "Missing sibling Crystal checkout: $crystalRoot"
}
Detail "Root: $crystalRoot"

$relativeProjects = @(
    "Crystal\Crystal.csproj",
    "Crystal.Tools\Crystal.Tools.csproj",
    "Crystal.Agents\Crystal.Agents.csproj",
    "Crystal.Harness\Crystal.Harness.csproj"
)

foreach ($relativePath in $relativeProjects) {
    $siblingProject = Join-Path $crystalRoot $relativePath
    if (-not (Test-Path -LiteralPath $siblingProject)) {
        Fail "Missing Crystal project: $siblingProject"
    }
    Detail "Found $siblingProject"
}
Detail "Check passed: sibling Crystal projects exist."

Step "check" "Runtime"
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Fail "This script is for Windows."
}

$architecture = [Environment]::GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")
if ([string]::IsNullOrWhiteSpace($architecture)) {
    $architecture = [Environment]::GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")
}

Detail "Operating system: Windows"
Detail "Architecture: $architecture"
if ($architecture -ne "AMD64") {
    Fail "Unsupported Windows architecture: $architecture"
}

Detail "Runtime identifier: $runtimeId"
Detail "Check passed: platform matches a release workflow runtime."

Step "check" "Install destination"
Detail "Directory: $installDirectory"

$expectedDirectory = Join-Path $HOME ".crystal\binaries\code"
if ($installDirectory -ne $expectedDirectory) {
    Fail "Refusing to replace unexpected install directory: $installDirectory"
}

if (Test-Path -LiteralPath $installDirectory) {
    $installItem = Get-Item -LiteralPath $installDirectory -Force
    if (Test-ReparsePoint $installDirectory) {
        Fail "Refusing to replace $installDirectory because it is a symbolic link."
    }
    if (-not $installItem.PSIsContainer) {
        Fail "$installDirectory exists and is not a directory."
    }
}

$installParent = Split-Path -Parent $installDirectory
New-Item -ItemType Directory -Path $installParent -Force | Out-Null
$probe = Join-Path $installParent ".install-local-probe"
try {
    New-Item -ItemType File -Path $probe -Force | Out-Null
}
catch {
    Fail "Cannot write to $installParent."
}
finally {
    if (Test-Path -LiteralPath $probe) {
        Remove-Item -LiteralPath $probe -Force
    }
}
Detail "Check passed: $installParent is writable."

if (Test-Path -LiteralPath $installDirectory) {
    Detail "Check passed: existing install directory can be replaced."
}
else {
    Detail "Check passed: no previous install directory."
}

Test-CrystalCodeStopped

Step "publish" "Release"
Detail "Output: build ($publishDirectory)"
Detail "Configuration: Release"
Detail "Runtime: $runtimeId"
Detail "Self-contained: true"
Detail "PublishSingleFile: true"

if (Test-ReparsePoint $publishDirectory) {
    Fail "Refusing to remove $publishDirectory because it is a symbolic link."
}

Clear-InstallTree $publishDirectory "build output"

Detail "dotnet publish CrystalCode/CrystalCode.csproj --configuration Release --runtime $runtimeId --self-contained true -p:PublishSingleFile=true --output build"
& dotnet publish CrystalCode/CrystalCode.csproj `
    --configuration Release `
    --runtime $runtimeId `
    --self-contained true `
    "-p:PublishSingleFile=true" `
    --output build
if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish exited with an error."
}
Detail "Check passed: publish finished."

Step "check" "Publish output"
$publishedBinary = Join-Path $publishDirectory $BinaryName
Detail "Binary: $publishedBinary"
if (-not (Test-Path -LiteralPath $publishedBinary -PathType Leaf)) {
    Fail "Publish output is missing $BinaryName."
}
Detail "Check passed: binary exists."

$managedAssembly = Join-Path $publishDirectory "CrystalCode.dll"
if (Test-Path -LiteralPath $managedAssembly) {
    Fail "Publish output contains CrystalCode.dll. The release workflow publishes a single file."
}
Detail "Check passed: CrystalCode.dll is absent."

$binaryBytes = (Get-Item -LiteralPath $publishedBinary).Length
Detail "Size: $binaryBytes bytes"
if ($binaryBytes -lt $MinimumBinaryBytes) {
    Fail "Published binary is $binaryBytes bytes. A self-contained single-file build is larger than $MinimumBinaryBytes bytes."
}
Detail "Check passed: size is large enough for a self-contained single-file build."

Test-CrystalCodeStopped

Step "install" "Replace $installDirectory"
Clear-InstallTree $stagingDirectory "staging directory"
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

Detail "Copying build output."
Get-ChildItem -LiteralPath $publishDirectory -Force | Copy-Item -Destination $stagingDirectory -Recurse -Force

$stagedBinary = Join-Path $stagingDirectory $BinaryName
if (-not (Test-Path -LiteralPath $stagedBinary -PathType Leaf)) {
    Fail "Staged binary is missing: $stagedBinary"
}

$stagedBytes = (Get-Item -LiteralPath $stagedBinary).Length
if ($stagedBytes -ne $binaryBytes) {
    Fail "Staged binary is $stagedBytes bytes. Build binary is $binaryBytes bytes."
}
Detail "Check passed: staged binary matches the build ($stagedBytes bytes)."

Install-StagedDirectory $installDirectory $stagingDirectory

$installedBinary = Join-Path $installDirectory $BinaryName
if (-not (Test-Path -LiteralPath $installedBinary -PathType Leaf)) {
    Fail "Installed binary is missing: $installedBinary"
}

$installedBytes = (Get-Item -LiteralPath $installedBinary).Length
if ($installedBytes -ne $binaryBytes) {
    Fail "Installed binary is $installedBytes bytes. Build binary is $binaryBytes bytes."
}

$fileCount = @(Get-ChildItem -LiteralPath $installDirectory -Recurse -File -Force).Count
Detail "Copied $fileCount files."
Detail "Check passed: installed binary matches the build ($installedBytes bytes)."

Add-UserPath $installDirectory

Step "done" "Local install complete"
Detail "Build: $publishedBinary"
Detail "Installed: $installedBinary"
Detail "Runtime: $runtimeId"
Detail "Configuration: Release"
Detail "Self-contained: true"
Detail "Single file: true"
Detail "Configuration, credentials, and prompts were left in place."

if ($script:ProfileChanged) {
    Detail "Open a new terminal to use CrystalCode from any directory."
}

Detail "Start Crystal Code with: CrystalCode"
