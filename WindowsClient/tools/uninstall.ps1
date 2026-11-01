<#
.SYNOPSIS
  Removes Sill for Windows installed by install.ps1: Explorer's "Open on Mac", the sign-in item, the
  Start menu's shortcuts (Sill's own and those to Mac apps) and the install folder.

.DESCRIPTION
  Settings, saved Macs, the clipboard's and drops' staging and the device key stay, so a later
  install is paired already. -RemoveData removes %LOCALAPPDATA%\Sill and the device key too (every
  Mac then needs pairing again). Mapped drives to a Mac's share and their saved passwords are
  Windows' own: remove them in Explorer (Disconnect) and Credential Manager.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File uninstall.ps1 -RemoveData
#>
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Sill'),
    [switch] $RemoveData
)
$ErrorActionPreference = 'Stop'
$installed = Join-Path $Destination 'Sill.exe'

Get-Process -Name 'Sill' -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(5000)) { $_.Kill() }
}
if (Test-Path $installed) {
    Start-Process -FilePath $installed -ArgumentList '--uninstall-integration' -Wait | Out-Null
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sill for Windows.lnk'
if (Test-Path $shortcut) { Remove-Item -Force $shortcut }
if (Test-Path $Destination) {
    # This script may run from the install folder itself: remove everything else, then the folder
    # once this script has finished.
    Get-ChildItem -Path $Destination | Where-Object { $_.FullName -ne $PSCommandPath } | Remove-Item -Recurse -Force
    if ($PSCommandPath -like "$Destination*") {
        Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$Destination`""
    } else { Remove-Item -Recurse -Force $Destination }
}
if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'Sill'
    if (Test-Path $data) { Remove-Item -Recurse -Force $data }
    # The device key lives in Windows' key storage under this name (Net/Identity.cs).
    try {
        $key = [System.Security.Cryptography.CngKey]::Open('Sill for Windows device key')
        $key.Delete()
    } catch { }
}
Write-Host 'Sill for Windows is removed.' -ForegroundColor Green
