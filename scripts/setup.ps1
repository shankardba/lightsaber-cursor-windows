# One-step setup: installs the .NET 8 SDK for your user account if needed (after asking), builds and installs
# the app, and launches it. On its first launch the app asks about starting at sign-in and pinning its tray icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$root = Split-Path -Parent $PSScriptRoot

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Note($text) { Write-Host "    $text" }
function Fail($text) {
    Write-Host "`n$text" -ForegroundColor Red
    [System.Windows.Forms.MessageBox]::Show($text, 'Lightsaber Cursor setup', 'OK', 'Error') | Out-Null
    exit 1
}

Write-Host 'Lightsaber Cursor setup' -ForegroundColor White

Step 'Checking Windows'
$os = [Environment]::OSVersion.Version
# Windows 11 reports itself as 10.0 with build 22000 or later.
if ($os.Major -lt 10 -or $os.Build -lt 22000) { Fail "Lightsaber Cursor needs Windows 11. This PC is running Windows build $($os.Build)." }
Note "Windows 11 (build $($os.Build)) on $($env:PROCESSOR_ARCHITECTURE): OK"

Step 'Checking the .NET 8 SDK'
$userDotnet = Join-Path $env:LOCALAPPDATA 'dotnet8\dotnet.exe'
function Has-Net8 {
    foreach ($d in @($userDotnet, 'dotnet')) {
        try { if ((& $d --list-sdks 2>$null) -match '^8\.') { return $true } } catch { }
    }
    return $false
}
if (-not (Has-Net8)) {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "Lightsaber Cursor is built with Microsoft's free .NET 8 SDK, which isn't installed yet.`n`nInstall it now just for your user account? (about 250 MB download; no administrator rights needed)",
        'Lightsaber Cursor setup', 'YesNo', 'Question')
    if ($answer -ne 'Yes') { Fail 'Setup needs the .NET 8 SDK. Run Setup again when you are ready to install it.' }
    Note 'Downloading and installing the .NET 8 SDK (a few minutes)...'
    $ProgressPreference = 'SilentlyContinue'
    # Make sure Windows PowerShell uses TLS 1.2, which Microsoft's download servers require.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    try {
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
        & $installer -Channel 8.0 -InstallDir (Join-Path $env:LOCALAPPDATA 'dotnet8') -NoPath | Out-Null
    } catch {
        Fail "Couldn't download the .NET 8 SDK ($($_.Exception.Message)). Check your internet connection and run Setup again."
    }
    if (-not (Has-Net8)) { Fail 'The .NET 8 SDK did not install correctly. Check your internet connection and run Setup again.' }
}
Note 'Installed: OK'

Step 'Building and installing the app (a couple of minutes the first time)'
& (Join-Path $PSScriptRoot 'build.ps1') -Install

Step 'Launching Lightsaber Cursor'
Start-Process (Join-Path $env:LOCALAPPDATA 'Programs\LightsaberCursor\LightsaberCursor.exe')

Write-Host "`nDone! Your pointer is now a lightsaber." -ForegroundColor Green
Note 'The first time the app starts it will ask whether to start at sign-in'
Note 'and whether to keep its icon always visible on the taskbar.'
Note 'Toggle the saber any time with Ctrl+Alt+Shift+L. Find it later in the Start menu: Lightsaber Cursor.'
