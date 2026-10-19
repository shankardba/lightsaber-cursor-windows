# Lightsaber Cursor (Windows)

C# .NET 8 WinForms tray app, SkiaSharp renderer ported from the Mac SaberRenderer,
layered overlay window; SetSystemCursor blanks arrow/I-beam/hand. Separate from the Mac
repo: no shared code.

## Build and test (Parallels VM "Windows 11", ARM64)
- From the Mac: `./scripts/vm-build.sh -Install`. Screenshot with `prlctl capture`.
- .NET 8 SDK is in `%LOCALAPPDATA%\dotnet8` (only .NET 6 is system-wide). The Mac's
  iCloud Drive is `Y:\` in the VM.
- Run VM PowerShell as script files in `vmtest/` (gitignored); inline quoting through
  prlctl breaks. Launch GUI exes with Start-Process from a script (`cmd /c start` hangs).
- The user keeps Sibelius scores open in the VM: never reboot it or close their apps
  without asking. If windows paint blank after resume, ask before restarting.
  `prlctl capture` goes black (and timers pause) when the VM display sleeps.

## Push
Private `shankardba/lightsaber-cursor-windows`: push tested changes without asking.
