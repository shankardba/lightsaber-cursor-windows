# Lightsaber Cursor (Windows)

The Windows version of Lightsaber Cursor. It's a system-tray app that turns the mouse pointer into a lightsaber, or a golden sword. It's kept separate from the macOS app ([`shankardba/lightsaber-cursor`](https://github.com/shankardba/lightsaber-cursor)), and the two share no code.

- The blade points up-left like the normal arrow, and the click point is the blade tip.
- The blade retracts after N idle seconds and re-ignites when you move. Clicking throws a spark, and fast swings leave a motion trail.
- The customizer covers 17 hilts, 6 finishes, accent and blade colors, blade style (standard / unstable / Darksaber / Metal Sword / Plasma Sword), shimmer, core, length, thickness and glow.
- 45 character presets (Jedi, Sith, Grey), and you can save your own to "My Sabers".
- The randomizer can change the hilt, the color and the side. It can also pick a new random saber every time the blade re-ignites.
- Auto-switch: an after-dark saber (following Windows dark mode or set hours) and per-app sabers.
- Only the arrow, text and link-hand pointers become the saber. Resize arrows, the busy spinner and apps' own cursors stay normal Windows pointers.
- Toggle it with **Ctrl+Alt+Shift+L** or from the tray icon.

---

## Setup, step by step

### What you need

- **Windows 10 or 11**, on a regular Intel/AMD PC (x64) or an ARM PC. The build detects which.
- About 10 minutes. No administrator rights are needed for the app itself.

### Step 1: Install the .NET 8 SDK

Open **Terminal** or **PowerShell** (Start → type *PowerShell*) and run:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Or download the **.NET 8 SDK** installer from Microsoft's .NET download page and run it. **Close and reopen PowerShell** afterwards so the `dotnet` command is found. To check it worked:

```powershell
dotnet --list-sdks
```

You should see a line starting with `8.0`.

### Step 2: Get the code

Pick one of these:

- **With Git** (install it with `winget install Git.Git` if needed). This asks you to sign in to GitHub, because the repo is private.
  ```powershell
  cd $HOME
  git clone https://github.com/shankardba/lightsaber-cursor-windows.git
  cd lightsaber-cursor-windows
  ```
- **Without Git:** on GitHub, open the repo while signed in, then choose **Code → Download ZIP**. Right-click the ZIP → **Extract All…**, then `cd` into the extracted folder in PowerShell.

### Step 3: Build and install

In PowerShell, inside the project folder, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Install
```

The first build downloads a few packages and takes a couple of minutes. When it finishes it prints:

```
Installed C:\Users\<you>\AppData\Local\Programs\LightsaberCursor\LightsaberCursor.exe (Start menu: Lightsaber Cursor)
```

The result is a single self-contained `LightsaberCursor.exe`, so no separate .NET runtime is needed to run it. The script also adds a **Lightsaber Cursor** entry to the Start menu.

### Step 4: Launch it

Press **Start**, type **Lightsaber Cursor** and press Enter. On first launch the **Customizer** window opens, and your pointer becomes a lightsaber (Obi-Wan's by default).

> If Windows shows a **"Windows protected your PC"** (SmartScreen) box, click **More info → Run anyway**. This normally only happens if the exe was copied from another computer or downloaded. A local build doesn't trigger it.

### Step 5: Keep the tray icon visible

Windows may tuck the saber icon into the hidden-icons area (the **^** arrow next to the clock). To keep it always visible:

1. Right-click the taskbar → **Taskbar settings**.
2. Open **Other system tray icons** (on Windows 10: *Select which icons appear on the taskbar*).
3. Turn **Lightsaber Cursor** on.

### Step 6: Pick your saber

1. Double-click the tray icon, or right-click it → **Customize…**.
2. On the **Saber** tab, pick a preset on the left (for example *Agamemnon (Golden Sword)* or *Sabine Wren (Darksaber)*), or build your own with the controls on the right. Changes apply to your pointer immediately.
3. Enter a name and click **Save New** to keep a custom saber under *My Sabers*.

### Step 7: Launch at login (optional)

Customizer → **Behavior** tab → **System** → tick **Launch at login**.

### Everyday use

| To… | Do this |
|---|---|
| Turn the saber on or off | Press **Ctrl+Alt+Shift+L**, or tray icon → **Lightsaber Cursor** |
| Switch sabers quickly | Right-click the tray icon → **Sabers** |
| Get a random saber | Tray icon → **Randomize** |
| Turn effects on or off | Tray icon → Retract When Idle / Click Spark / Motion Trail / After-Dark Switch / Per-App Sabers |
| Change settings | Tray icon → **Customize…** (or double-click it) |
| Quit (normal pointer comes back) | Tray icon → **Quit Lightsaber Cursor** |

### Troubleshooting

- **The pointer is invisible or stuck blank:** the app restores your normal pointer when it quits, crashes or you sign out, and the next launch repairs a killed session. If it ever sticks anyway, run this in PowerShell:
  ```powershell
  & "$env:LOCALAPPDATA\Programs\LightsaberCursor\LightsaberCursor.exe" --restore-cursors
  ```
  Signing out and back in also resets it.
- **Can't find the app window:** it lives in the tray. Double-click the tray icon, or launch it again from the Start menu. A second launch just brings up the Customizer.
- **The saber disappears on window edges, busy spinners or in some apps:** this is intentional. Those pointers stay standard Windows pointers so you can see what you're doing.
- **`dotnet` is not recognized:** reopen PowerShell after Step 1, or install the SDK from Microsoft's .NET download page.

### Uninstall

1. Customizer → Behavior → untick **Launch at login** (if enabled).
2. Tray icon → **Quit Lightsaber Cursor**.
3. Delete the folder `%LOCALAPPDATA%\Programs\LightsaberCursor`, and the Start menu shortcut at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Lightsaber Cursor.lnk`.
4. To remove saved settings too, delete `%APPDATA%\LightsaberCursor`.

---

## Developer notes

- `scripts\build.ps1 [-Runtime win-x64|win-arm64] [-Install]` copies the source to a local folder and publishes a self-contained single-file exe. It uses `%LOCALAPPDATA%\dotnet8\dotnet.exe` if present, otherwise `dotnet` on `PATH`.
- From the Mac, `./scripts/vm-build.sh -Install` runs that build inside the Parallels "Windows 11" VM, where iCloud Drive is `Y:`.
- Flags: `--customize` opens the Customizer on launch, `--log` writes timings to `%APPDATA%\LightsaberCursor\log.txt`, `--render-sheet out.png` renders every preset, and `--restore-cursors` resets the system pointers.

### How it works

1. `SetSystemCursor` swaps the arrow, I-beam and hand pointers for a transparent cursor.
2. A click-through, topmost, per-pixel-alpha layered window (`UpdateLayeredWindow`) draws the saber, spark and trail at the pointer. The drawing is done with SkiaSharp, ported from the macOS renderer.
3. Each frame, `GetCursorInfo` shows which pointer Windows wants. If it isn't one of the replaced ones, the saber hides and the native pointer shows.
4. The original cursors are restored on quit, on crash and at logoff. If the process is killed outright, the next launch restores them, using a marker file in `%APPDATA%\LightsaberCursor`.
