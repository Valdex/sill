<#
.SYNOPSIS
  Builds Sill for Windows for installing elsewhere: a folder and a zip per architecture in
  WindowsClient\dist (Sill-<version>-win-x64.zip, Sill-<version>-win-arm64.zip).

.DESCRIPTION
  Framework-dependent by default: the PC needs the .NET 10 Desktop Runtime (install.ps1 checks and
  says how to get it). -SelfContained bundles the runtime instead (about 150 MB more, nothing to
  install first). Nothing is signed: Windows SmartScreen may warn once for a file from the internet
  (install.ps1 unblocks the files it copies).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File WindowsClient\tools\publish.ps1
  powershell -ExecutionPolicy Bypass -File WindowsClient\tools\publish.ps1 -SelfContained -Runtimes win-x64
#>
param(
    [string[]] $Runtimes = @('win-x64', 'win-arm64'),
    [switch] $SelfContained
)
$ErrorActionPreference = 'Stop'
$client = Split-Path -Parent $PSScriptRoot
$project = Join-Path $client 'Sill.Windows\Sill.Windows.csproj'
$dist = Join-Path $client 'dist'
[xml] $csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = '0.0.0' }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

foreach ($rid in $Runtimes) {
    $out = Join-Path $dist "Sill-$version-$rid"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    Write-Host "Publishing $rid (self-contained: $sc) to $out"
    # EnableWindowsTargeting: so the same script also publishes from macOS or Linux (PowerShell 7).
    & dotnet publish $project -c Release -r $rid --self-contained $sc -o $out -p:DebugType=none -p:PublishReadyToRun=false -p:EnableWindowsTargeting=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
    Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $out
    Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $out
    $zip = "$out.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host "  $zip"
}
