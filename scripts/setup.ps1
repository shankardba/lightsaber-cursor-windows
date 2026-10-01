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

Step 'Checking for a previous installation'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\LightsaberCursor'
$exePath = Join-Path $installDir 'LightsaberCursor.exe'
$dataDir = Join-Path $env:APPDATA 'LightsaberCursor'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Lightsaber Cursor.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$hasSettings = Test-Path (Join-Path $dataDir 'settings.json')
$hadLogin = $null -ne (Get-ItemProperty $runKey -Name LightsaberCursor -ErrorAction SilentlyContinue)
$running = [bool](Get-Process LightsaberCursor -ErrorAction SilentlyContinue)
$fresh = $false
if ((Test-Path $installDir) -or (Test-Path $shortcut) -or $hasSettings -or $hadLogin -or $running) {
    Note 'Found one. It will be removed and replaced with this version.'
    # LIGHTSABER_SETTINGS=keep|fresh answers the question below without a dialog.
    $choice = $env:LIGHTSABER_SETTINGS
    if (-not $choice -and $hasSettings) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "A previous installation of Lightsaber Cursor was found. It will be removed and replaced with this version.`n`nKeep your saved sabers and settings?`n`n    Yes: keep them`n    No: start fresh",
            'Lightsaber Cursor setup', 'YesNo', 'Question')
        $choice = if ($answer -eq 'No') { 'fresh' } else { 'keep' }
    }
    $fresh = $choice -eq 'fresh'

    & (Join-Path $PSScriptRoot 'stop-app.ps1')
    $buildDir = Join-Path $env:LOCALAPPDATA 'LightsaberCursorBuild'
    Remove-Item $installDir, $shortcut, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($fresh) {
        Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-ItemProperty $runKey -Name LightsaberCursor -ErrorAction SilentlyContinue
        # Unpin the taskbar icon too, so the app asks again. (The entry itself stays: Windows only
        # recreates a deleted one much later, and the app needs it to offer the pin.)
        Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue |
            Where-Object { (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).ExecutablePath -eq $exePath } |
            ForEach-Object { Set-ItemProperty $_.PSPath -Name IsPromoted -Value 0 -Type DWord }
        Note 'Settings cleared; the app will ask its first-run questions again.'
    } else {
        Note 'Your sabers and settings are kept.'
    }
} else {
    Note 'None found: fresh install.'
}

Step 'Building and installing the app (a couple of minutes the first time)'
& (Join-Path $PSScriptRoot 'build.ps1') -Install
if ($hadLogin -and -not $fresh) {
    # Keep "start at sign-in" pointing at the freshly installed copy.
    Set-ItemProperty $runKey -Name LightsaberCursor -Value "`"$exePath`""
}
$firstRun = -not (Test-Path (Join-Path $dataDir 'settings.json'))

Step 'Launching Lightsaber Cursor'
Start-Process $exePath

Write-Host "`nDone! Your pointer is now a lightsaber." -ForegroundColor Green
if ($firstRun) {
    Note 'The first time the app starts it will ask whether to start at sign-in'
    Note 'and whether to keep its icon always visible on the taskbar.'
}
Note 'Toggle the saber any time with Ctrl+Alt+Shift+L. Find it later in the Start menu: Lightsaber Cursor.'
