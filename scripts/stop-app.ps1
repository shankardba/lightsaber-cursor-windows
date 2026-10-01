# Stops a running Lightsaber Cursor and puts the normal Windows pointers back straight away.
# Stopping it from outside skips the app's own cleanup, so without this the pointer would stay blank.
$ErrorActionPreference = 'Stop'

$running = Get-Process LightsaberCursor -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

if (-not ('LightsaberSetup.Cursors' -as [type])) {
    Add-Type -Namespace LightsaberSetup -Name Cursors -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);
'@
}
# SPI_SETCURSORS reloads the user's pointer scheme, undoing the app's blank pointers.
[LightsaberSetup.Cursors]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 0) | Out-Null
Remove-Item (Join-Path $env:APPDATA 'LightsaberCursor\cursors-replaced') -ErrorAction SilentlyContinue
