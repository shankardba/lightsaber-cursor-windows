# Lightsaber Cursor for Windows

The Windows version of Lightsaber Cursor. It's a system-tray app that turns the mouse pointer into a lightsaber. This project is kept separate from the macOS app (`shankardba/lightsaber-cursor`), and the two share no code.

- The blade points up-left like the normal arrow, and the click point is the blade tip.
- The blade retracts after N idle seconds and re-ignites when you move. Clicking throws a spark, and fast swings leave a motion trail.
- The customizer covers 15 hilts (including Inquisitor, Staff, Clawed and Darksaber), 6 finishes, accent and blade colors, blade style (standard / unstable / Darksaber), shimmer, core, length, thickness and glow.
- 43 character presets (Jedi, Sith, Grey), and you can save your own to "My Sabers".
- The randomizer can change the hilt, the color and the side (Any / Jedi / Sith). It can also pick a new random saber every time the blade re-ignites.
- Auto-switch: an after-dark saber (following Windows dark mode or set hours) and per-app sabers. Priority is per-app, then after dark, then your chosen saber.
- **Native pointers where they matter:** only the arrow, text and link-hand pointers are replaced. Resize arrows, the busy spinner and any app's own custom cursors stay standard Windows pointers, and the saber steps aside while they're showing.
- Toggle it from the tray menu or with **Ctrl+Alt+Shift+L**. Launch at login is optional.

## Build

You need the .NET 8 SDK. `build.ps1` looks for it in `%LOCALAPPDATA%\dotnet8` first, then on `PATH`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Install
```

This publishes a self-contained single `LightsaberCursor.exe` for the machine's architecture (`win-arm64` or `win-x64`; override with `-Runtime`). With `-Install`, it copies the exe to `%LOCALAPPDATA%\Programs\LightsaberCursor` and adds a Start menu shortcut.

From the Mac, build inside the Parallels "Windows 11" VM, where iCloud Drive is mapped as `Y:`:

```bash
./scripts/vm-build.sh -Install
```

## How it works

1. `SetSystemCursor` swaps the arrow, I-beam and hand pointers for a transparent cursor.
2. A click-through, topmost, per-pixel-alpha layered window (`UpdateLayeredWindow`) draws the saber, spark and trail at the pointer. The drawing is done with SkiaSharp, ported from the macOS renderer.
3. Each frame, `GetCursorInfo` shows which pointer Windows wants. If it isn't one of the replaced ones, the saber hides and the native pointer shows.

The pointer can never get stuck invisible:
- The original cursors are restored on quit, on crash (unhandled exceptions) and at logoff.
- If the app is killed outright, the next launch restores them, using a marker file in `%APPDATA%\LightsaberCursor`.
- To restore them manually: `LightsaberCursor.exe --restore-cursors`.

Other flags: `--customize` opens the customizer on launch, `--log` writes timings to `%APPDATA%\LightsaberCursor\log.txt`, and `--render-sheet out.png` renders every preset for a visual check.
