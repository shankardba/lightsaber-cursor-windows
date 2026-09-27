using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace LightsaberCursor;

/// System-tray app: owns the engine, the tray icon/menu, the global hotkey and the customizer window.
internal sealed class TrayApp : ApplicationContext
{
    readonly AppSettings settings = new();
    readonly CursorEngine engine;
    readonly NotifyIcon tray;
    readonly HotKeyWindow hotKey;
    CustomizerForm? customizer;

    public const string HotKeyText = "Ctrl+Alt+Shift+L";

    public TrayApp(bool openCustomizer)
    {
        engine = new CursorEngine(settings);
        tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Lightsaber Cursor",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu(tray.ContextMenuStrip);
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowCustomizer(); };
        BuildMenu(tray.ContextMenuStrip);

        hotKey = new HotKeyWindow(() => settings.Update(p => p.Enabled = !p.Enabled));
        if (settings.Prefs.Enabled) engine.Start();
        if (openCustomizer || !File.Exists(Path.Combine(AppSettings.Folder, "settings.json"))) ShowCustomizer();
        if (!File.Exists(Path.Combine(AppSettings.Folder, "settings.json"))) settings.Update(_ => { });
    }

    public void ShowCustomizer()
    {
        if (customizer == null || customizer.IsDisposed)
            customizer = new CustomizerForm(settings, engine);
        customizer.Show();
        if (customizer.WindowState == FormWindowState.Minimized) customizer.WindowState = FormWindowState.Normal;
        customizer.Activate();
    }

    static Icon MakeIcon()
    {
        using var bmp = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var glow = new SKPaint { IsAntialias = true, Color = new SKColor(80, 160, 255, 150), StrokeWidth = 5, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.6f) };
            using var blade = new SKPaint { IsAntialias = true, Color = new SKColor(225, 240, 255), StrokeWidth = 2.6f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            using var hilt = new SKPaint { IsAntialias = true, Color = new SKColor(190, 195, 205), StrokeWidth = 5.5f, Style = SKPaintStyle.Stroke };
            using var guard = new SKPaint { IsAntialias = true, Color = new SKColor(60, 62, 70), StrokeWidth = 2f, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(6, 6, 20, 20, glow);
            canvas.DrawLine(6, 6, 20, 20, blade);
            canvas.DrawLine(20, 20, 28, 28, hilt);
            canvas.DrawLine(17.5f, 22.5f, 22.5f, 17.5f, guard);
        }
        using var gdi = bmp.ToBitmap();
        return Icon.FromHandle(gdi.GetHicon());
    }

    void BuildMenu(ContextMenuStrip menu)
    {
        var p = settings.Prefs;
        menu.Items.Clear();

        var toggle = new ToolStripMenuItem("Lightsaber Cursor") { Checked = p.Enabled, ShortcutKeyDisplayString = HotKeyText };
        toggle.Click += (_, _) => settings.Update(x => x.Enabled = !x.Enabled);
        menu.Items.Add(toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Now: " + (engine.ActiveDescription.Length > 0 ? engine.ActiveDescription : p.Saber.Name)) { Enabled = false });

        var sabers = new ToolStripMenuItem("Sabers");
        foreach (var f in Enum.GetValues<Faction>())
        {
            sabers.DropDownItems.Add(new ToolStripMenuItem(f.DisplayName()) { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
            foreach (var s in Presets.All.Where(s => s.Faction == f)) sabers.DropDownItems.Add(SaberItem(s, p.Saber.Id));
        }
        if (p.CustomSabers.Count > 0)
        {
            sabers.DropDownItems.Add(new ToolStripMenuItem("My Sabers") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
            foreach (var s in p.CustomSabers) sabers.DropDownItems.Add(SaberItem(s, p.Saber.Id));
        }
        menu.Items.Add(sabers);
        var random = new ToolStripMenuItem("Randomize");
        random.Click += (_, _) => settings.Randomize();
        menu.Items.Add(random);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Check("Retract When Idle", p.RetractWhenIdle, x => x.RetractWhenIdle = !x.RetractWhenIdle));
        menu.Items.Add(Check("Click Spark", p.ClickSpark, x => x.ClickSpark = !x.ClickSpark));
        menu.Items.Add(Check("Motion Trail", p.MotionTrail, x => x.MotionTrail = !x.MotionTrail));
        menu.Items.Add(Check("After-Dark Switch", p.AfterDark, x => x.AfterDark = !x.AfterDark));
        menu.Items.Add(Check("Per-App Sabers", p.PerApp, x => x.PerApp = !x.PerApp));
        menu.Items.Add(Check("Randomize on Re-ignite", p.RandomOnIgnite, x => x.RandomOnIgnite = !x.RandomOnIgnite));
        menu.Items.Add(new ToolStripSeparator());

        var customize = new ToolStripMenuItem("Customize…") { Font = new Font(menu.Font, FontStyle.Bold) };
        customize.Click += (_, _) => ShowCustomizer();
        menu.Items.Add(customize);
        menu.Items.Add(new ToolStripSeparator());
        var quit = new ToolStripMenuItem("Quit Lightsaber Cursor");
        quit.Click += (_, _) => ExitThread();
        menu.Items.Add(quit);
    }

    ToolStripMenuItem SaberItem(SaberConfig s, string currentId)
    {
        var item = new ToolStripMenuItem(s.Name) { Checked = s.Id == currentId, Image = ColorDot(s) };
        item.Click += (_, _) => settings.Update(x => x.Saber = s.Copy());
        return item;
    }

    static Bitmap ColorDot(SaberConfig s)
    {
        var bmp = new Bitmap(12, 12);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(s.BladeStyle == BladeStyle.Darksaber ? Color.Black : s.Blade.ToColor());
        g.FillEllipse(brush, 1, 1, 10, 10);
        g.DrawEllipse(Pens.Gray, 1, 1, 10, 10);
        return bmp;
    }

    ToolStripMenuItem Check(string title, bool on, Action<Prefs> toggle)
    {
        var item = new ToolStripMenuItem(title) { Checked = on };
        item.Click += (_, _) => settings.Update(toggle);
        return item;
    }

    protected override void ExitThreadCore()
    {
        engine.Dispose();
        hotKey.Dispose();
        tray.Visible = false;
        tray.Dispose();
        customizer?.Dispose();
        base.ExitThreadCore();
    }

    /// Hidden window that receives the global toggle hotkey.
    sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        const int Id = 0x4C53;
        readonly Action onPress;

        public HotKeyWindow(Action onPress)
        {
            this.onPress = onPress;
            CreateHandle(new CreateParams());
            Native.RegisterHotKey(Handle, Id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, (uint)Keys.L);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)Id) onPress();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Native.UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }
    }
}
