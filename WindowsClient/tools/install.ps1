<#
.SYNOPSIS
  Installs Sill for Windows for this user (no administrator rights): the published folder this
  script sits in (or -Source) is copied to %LOCALAPPDATA%\Programs\Sill, with a Start menu shortcut.

.DESCRIPTION
  Checks first that the PC can run Sill: 64-bit Windows 11 (TLS 1.3), the .NET 10 Desktop Runtime
  (unless the build is self-contained) and an HEVC decoder (the HEVC Video Extensions from the
  Microsoft Store), and says how to get what is missing. Sill already running is asked to quit first.
  -OpenOnMac adds Explorer's "Open on Mac" to files and folders; -StartAtSignIn opens Sill when you
  sign in. uninstall.ps1 takes it all away again.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -OpenOnMac
#>
param(
    [string] $Source = $PSScriptRoot,
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Sill'),
    [switch] $OpenOnMac,
    [switch] $StartAtSignIn
)
$ErrorActionPreference = 'Stop'

function Fail($message) { Write-Host $message -ForegroundColor Red; exit 1 }

if (-not [Environment]::Is64BitOperatingSystem) { Fail 'Sill needs 64-bit Windows.' }
$build = [Environment]::OSVersion.Version.Build
if ($build -lt 22000) { Fail "Sill needs Windows 11 (this is build $build): Windows 10 has no TLS 1.3, which every Sill for Mac requires." }

$exe = Join-Path $Source 'Sill.exe'
if (-not (Test-Path $exe)) { Fail "No Sill.exe in $Source. Run this script from a published folder (tools\publish.ps1), or pass -Source." }

# Framework-dependent builds carry Sill.runtimeconfig.json without "includedFrameworks".
$config = Join-Path $Source 'Sill.runtimeconfig.json'
$selfContained = (Test-Path $config) -and ((Get-Content $config -Raw) -match 'includedFrameworks')
if (-not $selfContained) {
    $runtimes = @()
    try { $runtimes = & dotnet --list-runtimes 2>$null } catch { }
    if (-not ($runtimes -match '^Microsoft\.WindowsDesktop\.App 10\.')) {
        Fail ("Sill needs the .NET 10 Desktop Runtime. Install it with:`n" +
              "  winget install Microsoft.DotNet.DesktopRuntime.10`n" +
              "or from https://dotnet.microsoft.com/download/dotnet/10.0, then run this again.")
    }
}

# Sill already running: ask it to quit (its windows close, its Mac's windows go back on the Mac).
Get-Process -Name 'Sill' -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.Path -and ($_.Path -like "$Destination*")) {
        Write-Host 'Closing the Sill that runs from the install folder…'
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(5000)) { $_.Kill() }
    }
}

if ((Resolve-Path $Source).Path -ne (New-Item -ItemType Directory -Force -Path $Destination).FullName) {
    Write-Host "Copying Sill to $Destination"
    Get-ChildItem -Path $Destination -Exclude 'install.ps1', 'uninstall.ps1' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}
Get-ChildItem -Path $Destination -Recurse | Unblock-File
$installed = Join-Path $Destination 'Sill.exe'

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sill for Windows.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installed
$link.WorkingDirectory = $Destination
$link.Description = 'Streams Mac windows to this PC over the local network.'
$link.IconLocation = "$installed,0"
$link.Save()
Write-Host "Start menu: $shortcut"

$integration = @('--install-integration')
if ($OpenOnMac) { $integration += '--open-on-mac' }
if ($StartAtSignIn) { $integration += '--start-at-sign-in' }
$p = Start-Process -FilePath $installed -ArgumentList $integration -Wait -PassThru
if ($p.ExitCode -ne 0) { Write-Host "Sill could not set up Explorer's verb or the sign-in item (exit $($p.ExitCode))." -ForegroundColor Yellow }

# The HEVC decoder (Sill.exe --diag prints Media Foundation's decoders).
$diag = & $installed --diag 2>&1 | Out-String
if ($diag -notmatch '(?i)hevc') {
    Write-Host ("No HEVC decoder found. Install the HEVC Video Extensions from the Microsoft Store:`n" +
                "  https://apps.microsoft.com/detail/9nmzlz57r3t7") -ForegroundColor Yellow
}
Write-Host 'Sill for Windows is installed. Open it from the Start menu.' -ForegroundColor Green
