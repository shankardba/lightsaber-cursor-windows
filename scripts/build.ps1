# Builds Lightsaber Cursor for Windows. Run on Windows (or in a Windows VM) from anywhere:
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Runtime win-arm64|win-x64] [-Install]
# The source is copied to a local folder first so builds don't churn a synced/network drive.
param(
    [string]$Runtime = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }),
    [switch]$Install
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:LOCALAPPDATA 'LightsaberCursorBuild'
$src = Join-Path $work 'src'
$out = Join-Path $work "publish\$Runtime"

$dotnet = Join-Path $env:LOCALAPPDATA 'dotnet8\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

New-Item -ItemType Directory -Force -Path $src | Out-Null
robocopy $root $src /MIR /XD .git bin obj /NFL /NDL /NJH /NJS /NP | Out-Null

Get-Process LightsaberCursor -ErrorAction SilentlyContinue | Stop-Process -Force
& $dotnet publish (Join-Path $src 'LightsaberCursor.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Host "Built $out\LightsaberCursor.exe"

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA 'Programs\LightsaberCursor'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item (Join-Path $out 'LightsaberCursor.exe') $dest -Force
    $lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Lightsaber Cursor.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($lnk)
    $shortcut.TargetPath = Join-Path $dest 'LightsaberCursor.exe'
    $shortcut.Save()
    Write-Host "Installed $dest\LightsaberCursor.exe (Start menu: Lightsaber Cursor)"
}
