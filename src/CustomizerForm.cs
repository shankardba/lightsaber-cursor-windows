using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace LightsaberCursor;

internal sealed class CustomizerForm : Form
{
    readonly AppSettings settings;
    readonly CursorEngine engine;
    bool syncing;

    // Header
    readonly CheckBox enabledBox = new() { Text = "Lightsaber cursor", AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
    readonly Label showingLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(12, 3, 0, 0) };

    // Saber tab
    readonly ListView presetList = new() { View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    readonly ImageList thumbs = new() { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
    readonly SKControl preview = new() { Dock = DockStyle.Fill };
    readonly ComboBox previewMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    readonly CheckBox lightBackground = new() { Text = "Light background", AutoSize = true };
    readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 33 };
    readonly Stopwatch previewClock = Stopwatch.StartNew();

    readonly CheckBox randHilt = new() { Text = "Hilt", AutoSize = true };
    readonly CheckBox randColor = new() { Text = "Color", AutoSize = true };
    readonly ComboBox randSide = Combo(Enum.GetNames<RandomSide>());
    readonly TextBox nameBox = new() { Width = 260 };
    readonly ComboBox factionBox = Combo(Enum.GetNames<Faction>());
    readonly ComboBox hiltBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, Width = 260 };
    readonly ComboBox finishBox = Combo(Enum.GetNames<HiltFinish>());
    readonly Button accentButton = new() { Text = "Accent color…", AutoSize = true };
    readonly Button bladeColorButton = new() { Text = "Custom blade color…", AutoSize = true };
    readonly ComboBox styleBox = Combo(Enum.GetNames<BladeStyle>());
    readonly CheckBox animatedBox = new() { Text = "Animated shimmer (hum flicker)", AutoSize = true };
    readonly Slider coreSlider = new("Core brightness", 0, 1);
    readonly Slider lengthSlider = new("Length", 0.6, 1.4);
    readonly Slider thicknessSlider = new("Thickness", 0.6, 1.8);
    readonly Slider glowSizeSlider = new("Glow size", 0.3, 2);
    readonly Slider glowStrengthSlider = new("Glow strength", 0.2, 1.6);
    readonly TextBox saveName = new() { Width = 170, PlaceholderText = "Name for My Sabers" };
    readonly Button updateButton = new() { Text = "Update", AutoSize = true };
    readonly Button deleteButton = new() { Text = "Delete", AutoSize = true };

    // Behavior tab
    readonly Slider sizeSlider = new("Cursor size", 0.45, 1.6);
    readonly CheckBox retractBox = new() { Text = "Retract the blade when the mouse is idle", AutoSize = true };
    readonly Slider idleSlider = new("Idle time before retracting", 1, 30, v => $"{Math.Round(v)}s");
    readonly CheckBox sparkBox = new() { Text = "Spark on click", AutoSize = true };
    readonly CheckBox trailBox = new() { Text = "Motion trail on fast swings", AutoSize = true };
    readonly CheckBox igniteRandomBox = new() { Text = "New random saber every time the blade re-ignites after idle", AutoSize = true };
    readonly CheckBox loginBox = new() { Text = "Launch at login", AutoSize = true };

    // Auto-switch tab
    readonly CheckBox afterDarkBox = new() { Text = "Switch sabers after dark", AutoSize = true };
    readonly ComboBox afterDarkMode = Combo("Follow Windows dark mode", "Set hours");
    readonly NumericUpDown nightStart = new() { Minimum = 0, Maximum = 23, Width = 60 };
    readonly NumericUpDown nightEnd = new() { Minimum = 0, Maximum = 23, Width = 60 };
    readonly ComboBox nightSaberBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly Label nightStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly CheckBox perAppBox = new() { Text = "Use a different saber in specific apps", AutoSize = true };
    readonly ListView rulesList = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Width = 620, Height = 200 };
    readonly ComboBox runningApps = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly ComboBox ruleSaberBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };

    static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        c.Items.AddRange(items);
        return c;
    }

    public CustomizerForm(AppSettings settings, CursorEngine engine)
    {
        this.settings = settings;
        this.engine = engine;
        Text = "Lightsaber Cursor";
        Icon = null;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(1000, 640);

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 10, 12, 0) };
        header.Controls.Add(enabledBox);
        header.Controls.Add(showingLabel);
        header.Controls.Add(new Label { Text = $"Toggle: {TrayApp.HotKeyText}", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(24, 3, 0, 0) });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSaberTab());
        tabs.TabPages.Add(BuildBehaviorTab());
        tabs.TabPages.Add(BuildAutoSwitchTab());
        Controls.Add(tabs);
        Controls.Add(header);

        enabledBox.CheckedChanged += (_, _) => Edit(p => p.Enabled = enabledBox.Checked);
        settings.Changed += OnSettingsChanged;
        engine.ActiveChanged += OnActiveChanged;
        FormClosed += (_, _) =>
        {
            settings.Changed -= OnSettingsChanged;
            engine.ActiveChanged -= OnActiveChanged;
            previewTimer.Dispose();
        };

        var sw = Stopwatch.StartNew();
        FillPresetList();
        SyncFromPrefs();
        Log.Write($"customizer built in {sw.ElapsedMilliseconds}ms");
        Shown += (_, _) => Log.Write("customizer shown");
        previewTimer.Tick += (_, _) => preview.Invalidate();
        previewTimer.Start();
    }

    void OnSettingsChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(SyncFromPrefs); else SyncFromPrefs();
    }

    void OnActiveChanged()
    {
        if (IsDisposed) return;
        BeginInvoke(() => showingLabel.Text = "Showing: " + engine.ActiveDescription);
    }

    /// Apply a UI edit unless we're in the middle of pushing prefs into the controls.
    void Edit(Action<Prefs> change)
    {
        if (!syncing) settings.Update(change);
    }

    void EditSaber(Action<SaberConfig> change) => Edit(p =>
    {
        var c = p.Saber.Copy();
        change(c);
        p.Saber = c;
    });

    // MARK: Saber tab

    TabPage BuildSaberTab()
    {
        var page = new TabPage("Saber") { Padding = new Padding(8) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));

        presetList.Columns.Add("Saber", 230);
        presetList.SmallImageList = thumbs;
        presetList.ItemActivate += (_, _) => PickSelectedPreset();
        presetList.ItemSelectionChanged += (_, e) => { if (e.IsSelected && !syncing) PickSelectedPreset(); };
        var listMenu = new ContextMenuStrip();
        var deleteItem = new ToolStripMenuItem("Delete from My Sabers");
        deleteItem.Click += (_, _) =>
        {
            if (presetList.SelectedItems.Count == 1 && presetList.SelectedItems[0].Tag is SaberConfig s && s.Id.StartsWith("custom."))
                settings.DeleteCustom(s.Id);
        };
        listMenu.Items.Add(deleteItem);
        listMenu.Opening += (_, e) => deleteItem.Enabled = presetList.SelectedItems.Count == 1
            && presetList.SelectedItems[0].Tag is SaberConfig s && s.Id.StartsWith("custom.");
        presetList.ContextMenuStrip = listMenu;
        grid.Controls.Add(presetList, 0, 0);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8, 0, 8, 0) };
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewMode.Items.AddRange(new object[] { "Live loop", "Extended", "Retracted" });
        previewMode.SelectedIndex = 0;
        var previewBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        previewBar.Controls.Add(previewMode);
        previewBar.Controls.Add(lightBackground);
        center.Controls.Add(previewBar, 0, 0);
        preview.PaintSurface += PaintPreview;
        center.Controls.Add(preview, 0, 1);
        center.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(520, 0),
            Text = "The saber replaces the arrow, text and link pointers. Resize, busy and other special pointers stay standard Windows pointers.",
        }, 0, 2);
        grid.Controls.Add(center, 1, 0);

        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        editor.Controls.Add(Group("Randomizer", Row(randHilt, randColor, Labeled("Side", randSide, 90)), ActionButton("Randomize", () => settings.Randomize())));
        editor.Controls.Add(Group("Identity", Labeled("Name", nameBox), Labeled("Side", factionBox)));

        hiltBox.Items.AddRange(Enum.GetNames<HiltStyle>());
        hiltBox.DrawItem += DrawHiltItem;
        var swatches = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(320, 0) };
        foreach (var (name, rgb) in BladeColors.Swatches)
        {
            var b = new Button { Width = 26, Height = 26, BackColor = rgb.ToColor(), FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            b.FlatAppearance.BorderColor = Color.Gray;
            new ToolTip().SetToolTip(b, name);
            b.Click += (_, _) => EditSaber(c =>
            {
                c.Blade = rgb;
                if (c.BladeStyle == BladeStyle.Darksaber) c.BladeStyle = BladeStyle.Standard;
            });
            swatches.Controls.Add(b);
        }
        editor.Controls.Add(Group("Hilt", Labeled("Style", hiltBox), Labeled("Finish", finishBox), accentButton));
        editor.Controls.Add(Group("Blade", swatches, bladeColorButton, Labeled("Style", styleBox), animatedBox, coreSlider, lengthSlider, thicknessSlider));
        editor.Controls.Add(Group("Glow", glowSizeSlider, glowStrengthSlider));
        var saveNew = ActionButton("Save New", () => { settings.SaveCustom(saveName.Text); saveName.Text = ""; });
        updateButton.Click += (_, _) => settings.UpdateCustom();
        deleteButton.Click += (_, _) => settings.DeleteCustom(settings.Prefs.Saber.Id);
        editor.Controls.Add(Group("Save", Row(saveName, saveNew), Row(updateButton, deleteButton)));
        grid.Controls.Add(editor, 2, 0);
        page.Controls.Add(grid);

        randHilt.CheckedChanged += (_, _) => Edit(p => p.RandomHilt = randHilt.Checked);
        randColor.CheckedChanged += (_, _) => Edit(p => p.RandomColor = randColor.Checked);
        randSide.SelectedIndexChanged += (_, _) => Edit(p => p.RandomSide = (RandomSide)randSide.SelectedIndex);
        nameBox.TextChanged += (_, _) => EditSaber(c => c.Name = nameBox.Text);
        factionBox.SelectedIndexChanged += (_, _) => EditSaber(c => c.Faction = (Faction)factionBox.SelectedIndex);
        hiltBox.SelectedIndexChanged += (_, _) => EditSaber(c => c.Hilt = (HiltStyle)hiltBox.SelectedIndex);
        finishBox.SelectedIndexChanged += (_, _) => EditSaber(c => c.Finish = (HiltFinish)finishBox.SelectedIndex);
        styleBox.SelectedIndexChanged += (_, _) => EditSaber(c =>
        {
            c.BladeStyle = (BladeStyle)styleBox.SelectedIndex;
            if (c.BladeStyle == BladeStyle.Darksaber) c.Blade = RGB.Hex(0x0A0A0C);
        });
        animatedBox.CheckedChanged += (_, _) => EditSaber(c => c.Animated = animatedBox.Checked);
        accentButton.Click += (_, _) => PickColor(settings.Prefs.Saber.Accent, rgb => EditSaber(c => c.Accent = rgb));
        bladeColorButton.Click += (_, _) => PickColor(settings.Prefs.Saber.Blade, rgb => EditSaber(c =>
        {
            c.Blade = rgb;
            if (c.BladeStyle == BladeStyle.Darksaber) c.BladeStyle = BladeStyle.Standard;
        }));
        coreSlider.ValueChanged += v => EditSaber(c => c.CoreWhiteness = v);
        lengthSlider.ValueChanged += v => EditSaber(c => c.BladeLength = v);
        thicknessSlider.ValueChanged += v => EditSaber(c => c.Thickness = v);
        glowSizeSlider.ValueChanged += v => EditSaber(c => c.GlowRadius = v);
        glowStrengthSlider.ValueChanged += v => EditSaber(c => c.GlowIntensity = v);
        return page;
    }

    void PickSelectedPreset()
    {
        if (presetList.SelectedItems.Count == 1 && presetList.SelectedItems[0].Tag is SaberConfig s && s.Id != settings.Prefs.Saber.Id)
            settings.Update(p => p.Saber = s.Copy());
    }

    void PickColor(RGB current, Action<RGB> apply)
    {
        using var dlg = new ColorDialog { Color = current.ToColor(), FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) apply(RGB.From(dlg.Color));
    }

    void FillPresetList()
    {
        presetList.BeginUpdate();
        presetList.Items.Clear();
        presetList.Groups.Clear();
        thumbs.Images.Clear();
        var groups = Enum.GetValues<Faction>().ToDictionary(f => f, f => presetList.Groups.Add(f.ToString(), f.DisplayName()));
        var mine = presetList.Groups.Add("mine", "My Sabers");
        foreach (var s in settings.AllSabers)
        {
            thumbs.Images.Add(s.Id, Thumbnail(s, 32));
            bool custom = s.Id.StartsWith("custom.");
            presetList.Items.Add(new ListViewItem(s.Name, s.Id) { Tag = s, Group = custom ? mine : groups[s.Faction] });
        }
        presetList.EndUpdate();
    }

    static Bitmap Thumbnail(SaberConfig s, int size)
    {
        var (lay, _) = SaberRenderer.Layout(s, 1, tight: true);
        float scale = size / (float)Math.Max(lay.Width, lay.Height);
        using var img = SaberRenderer.Render(s, new SaberState(1, 0.3), scale, tight: true);
        var bmp = new Bitmap(size, size);
        using var src = img.Image.ToBitmap();
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(src, (size - src.Width) / 2, (size - src.Height) / 2);
        return bmp;
    }

    readonly Dictionary<string, Bitmap> hiltIcons = new();

    void DrawHiltItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0) return;
        var hilt = (HiltStyle)e.Index;
        var c = settings.Prefs.Saber.Copy();
        c.Hilt = hilt;
        string key = $"{hilt}|{c.Finish}|{c.Accent}";
        if (!hiltIcons.TryGetValue(key, out var icon))
        {
            using var img = SaberRenderer.RenderHiltIcon(c, 20);
            icon = img.ToBitmap();
            hiltIcons[key] = icon;
        }
        e.Graphics.DrawImage(icon, e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2);
        using var brush = new SolidBrush(e.ForeColor);
        e.Graphics.DrawString(hilt.ToString(), e.Font ?? Font, brush, e.Bounds.Left + 90, e.Bounds.Top + 5);
    }

    void PaintPreview(object? sender, SKPaintSurfaceEventArgs e)
    {
        var sw = Stopwatch.StartNew();
        PaintPreviewCore(e);
        Log.Time("preview", sw.Elapsed.TotalMilliseconds);
    }

    void PaintPreviewCore(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(lightBackground.Checked ? new SKColor(237, 237, 237) : new SKColor(10, 13, 20));
        var cfg = settings.Prefs.Saber;
        double t = previewClock.Elapsed.TotalSeconds;
        var state = previewMode.SelectedIndex switch
        {
            1 => new SaberState(1, t),
            2 => new SaberState(0, t),
            _ => LiveLoop(t),
        };
        var (lay, _) = SaberRenderer.Layout(cfg, 1);
        float s = MathF.Min(e.Info.Width / (float)lay.Width, e.Info.Height / (float)lay.Height) * 0.92f;
        using var r = SaberRenderer.Render(cfg, state, s);
        canvas.DrawImage(r.Image, (e.Info.Width - r.Width) / 2f, (e.Info.Height - r.Height) / 2f);
    }

    static SaberState LiveLoop(double t)
    {
        double c = t % 5;
        double ext = c < 2.8 ? 1 : c < 3.15 ? 1 - (c - 2.8) / 0.35 : c < 4.4 ? 0 : c < 4.58 ? (c - 4.4) / 0.18 : 1;
        return new SaberState(ext, t);
    }

    // MARK: Behavior tab

    TabPage BuildBehaviorTab()
    {
        var page = new TabPage("Behavior") { Padding = new Padding(16) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(Group("Cursor", sizeSlider));
        flow.Controls.Add(Group("Idle retract", retractBox, idleSlider));
        flow.Controls.Add(Group("Effects", sparkBox, trailBox, new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Resize, busy and app-specific pointers always show as normal Windows pointers.",
        }));
        var randHilt2 = new CheckBox { Text = "Randomize hilt", AutoSize = true };
        var randColor2 = new CheckBox { Text = "Randomize color", AutoSize = true };
        var randSide2 = Combo(Enum.GetNames<RandomSide>());
        flow.Controls.Add(Group("Randomizer", randHilt2, randColor2, Labeled("Side", randSide2), igniteRandomBox));
        flow.Controls.Add(Group("System", loginBox, new Label { Text = $"Toggle shortcut: {TrayApp.HotKeyText}", AutoSize = true }));
        page.Controls.Add(flow);

        sizeSlider.ValueChanged += v => Edit(p => p.Scale = v);
        retractBox.CheckedChanged += (_, _) => Edit(p => p.RetractWhenIdle = retractBox.Checked);
        idleSlider.ValueChanged += v => Edit(p => p.IdleSeconds = Math.Round(v));
        sparkBox.CheckedChanged += (_, _) => Edit(p => p.ClickSpark = sparkBox.Checked);
        trailBox.CheckedChanged += (_, _) => Edit(p => p.MotionTrail = trailBox.Checked);
        igniteRandomBox.CheckedChanged += (_, _) => Edit(p => p.RandomOnIgnite = igniteRandomBox.Checked);
        randHilt2.CheckedChanged += (_, _) => Edit(p => p.RandomHilt = randHilt2.Checked);
        randColor2.CheckedChanged += (_, _) => Edit(p => p.RandomColor = randColor2.Checked);
        randSide2.SelectedIndexChanged += (_, _) => Edit(p => p.RandomSide = (RandomSide)randSide2.SelectedIndex);
        loginBox.CheckedChanged += (_, _) => { if (!syncing) AppSettings.LaunchAtLogin = loginBox.Checked; };
        behaviorMirrors = (randHilt2, randColor2, randSide2);
        return page;
    }

    (CheckBox Hilt, CheckBox Color, ComboBox Side) behaviorMirrors;

    // MARK: Auto-switch tab

    TabPage BuildAutoSwitchTab()
    {
        var page = new TabPage("Auto-Switch") { Padding = new Padding(16) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        flow.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Priority: a per-app rule wins, then after dark, then your chosen saber. Switches retract the old blade and ignite the new one.",
        });
        flow.Controls.Add(Group("After dark", afterDarkBox, Labeled("Night is", afterDarkMode, 90),
            Row(new Label { Text = "From hour", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, nightStart,
                new Label { Text = "until hour", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, nightEnd),
            Labeled("Night saber", nightSaberBox, 90), nightStatus));

        rulesList.Columns.Add("App", 220);
        rulesList.Columns.Add("Saber", 380);
        var addButton = ActionButton("Add", AddRule);
        var changeButton = ActionButton("Set saber for selected", () =>
        {
            if (rulesList.SelectedItems.Count == 1 && rulesList.SelectedItems[0].Tag is string id && SaberFromCombo(ruleSaberBox) is { } s)
                Edit(p => { var r = p.AppRules.FirstOrDefault(x => x.Id == id); if (r != null) r.Saber = s; });
        });
        var removeButton = ActionButton("Remove selected", () =>
        {
            if (rulesList.SelectedItems.Count == 1 && rulesList.SelectedItems[0].Tag is string id)
                Edit(p => p.AppRules.RemoveAll(x => x.Id == id));
        });
        runningApps.DropDown += (_, _) => FillRunningApps();
        flow.Controls.Add(Group("Per-app sabers", perAppBox, rulesList,
            Row(new Label { Text = "App", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, runningApps),
            Row(new Label { Text = "Saber", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, ruleSaberBox),
            Row(addButton, changeButton, removeButton)));
        page.Controls.Add(flow);

        afterDarkBox.CheckedChanged += (_, _) => Edit(p => p.AfterDark = afterDarkBox.Checked);
        afterDarkMode.SelectedIndexChanged += (_, _) => Edit(p => p.AfterDarkMode = (AfterDarkMode)afterDarkMode.SelectedIndex);
        nightStart.ValueChanged += (_, _) => Edit(p => p.NightStart = (int)nightStart.Value);
        nightEnd.ValueChanged += (_, _) => Edit(p => p.NightEnd = (int)nightEnd.Value);
        nightSaberBox.SelectionChangeCommitted += (_, _) => { if (SaberFromCombo(nightSaberBox) is { } s) Edit(p => p.NightSaber = s); };
        perAppBox.CheckedChanged += (_, _) => Edit(p => p.PerApp = perAppBox.Checked);
        FillRunningApps();
        return page;
    }

    sealed record AppChoice(string Exe, string Title)
    {
        public override string ToString() => $"{Title} ({Exe})";
    }

    void FillRunningApps()
    {
        var apps = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && p.Id != Environment.ProcessId; } catch { return false; } })
            .Select(p => new AppChoice(p.ProcessName, p.MainWindowTitle.Length > 40 ? p.MainWindowTitle[..40] + "…" : p.MainWindowTitle))
            .GroupBy(a => a.Exe, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Exe, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        runningApps.Items.Clear();
        runningApps.Items.AddRange(apps);
    }

    void AddRule()
    {
        if (runningApps.SelectedItem is not AppChoice app) return;
        var saber = SaberFromCombo(ruleSaberBox) ?? settings.Prefs.Saber.Copy();
        Edit(p =>
        {
            if (p.AppRules.Any(r => string.Equals(r.ExeName, app.Exe, StringComparison.OrdinalIgnoreCase))) return;
            p.AppRules.Add(new AppRule { ExeName = app.Exe, AppName = app.Exe, Saber = saber });
        });
    }

    sealed record SaberChoice(SaberConfig Saber)
    {
        public override string ToString() => Saber.Name;
    }

    void FillSaberCombo(ComboBox box, string? selectedId)
    {
        box.BeginUpdate();
        box.Items.Clear();
        foreach (var s in settings.AllSabers) box.Items.Add(new SaberChoice(s));
        box.SelectedIndex = Math.Max(-1, settings.AllSabers.ToList().FindIndex(s => s.Id == selectedId));
        box.EndUpdate();
    }

    static SaberConfig? SaberFromCombo(ComboBox box) => (box.SelectedItem as SaberChoice)?.Saber.Copy();

    // MARK: Sync

    string presetSignature = "";

    void SyncFromPrefs()
    {
        syncing = true;
        try
        {
            var p = settings.Prefs;
            var c = p.Saber;
            enabledBox.Checked = p.Enabled;
            showingLabel.Text = "Showing: " + (engine.ActiveDescription.Length > 0 ? engine.ActiveDescription : c.Name);

            string sig = string.Join("|", settings.AllSabers.Select(s => s.Id + s.Name));
            if (sig != presetSignature)
            {
                presetSignature = sig;
                FillPresetList();
            }
            foreach (ListViewItem item in presetList.Items)
                item.Selected = item.Tag is SaberConfig s && s.Id == c.Id;

            randHilt.Checked = behaviorMirrors.Hilt.Checked = p.RandomHilt;
            randColor.Checked = behaviorMirrors.Color.Checked = p.RandomColor;
            randSide.SelectedIndex = behaviorMirrors.Side.SelectedIndex = (int)p.RandomSide;
            if (nameBox.Text != c.Name) nameBox.Text = c.Name;
            factionBox.SelectedIndex = (int)c.Faction;
            hiltBox.SelectedIndex = (int)c.Hilt;
            finishBox.SelectedIndex = (int)c.Finish;
            styleBox.SelectedIndex = (int)c.BladeStyle;
            animatedBox.Checked = c.Animated;
            coreSlider.Value = c.CoreWhiteness;
            lengthSlider.Value = c.BladeLength;
            thicknessSlider.Value = c.Thickness;
            glowSizeSlider.Value = c.GlowRadius;
            glowStrengthSlider.Value = c.GlowIntensity;
            bool custom = p.CustomSabers.Any(s => s.Id == c.Id);
            updateButton.Enabled = deleteButton.Enabled = custom;
            hiltBox.Invalidate();

            sizeSlider.Value = p.Scale;
            retractBox.Checked = p.RetractWhenIdle;
            idleSlider.Value = p.IdleSeconds;
            idleSlider.Enabled = p.RetractWhenIdle;
            sparkBox.Checked = p.ClickSpark;
            trailBox.Checked = p.MotionTrail;
            igniteRandomBox.Checked = p.RandomOnIgnite;
            loginBox.Checked = AppSettings.LaunchAtLogin;

            afterDarkBox.Checked = p.AfterDark;
            afterDarkMode.SelectedIndex = (int)p.AfterDarkMode;
            nightStart.Value = p.NightStart;
            nightEnd.Value = p.NightEnd;
            nightStart.Enabled = nightEnd.Enabled = p.AfterDarkMode == AfterDarkMode.Hours;
            FillSaberCombo(nightSaberBox, p.NightSaber.Id);
            nightStatus.Text = "It is currently " + (settings.IsNight() ? "night" : "day");
            perAppBox.Checked = p.PerApp;
            if (ruleSaberBox.Items.Count != settings.AllSabers.Count()) FillSaberCombo(ruleSaberBox, c.Id);
            rulesList.BeginUpdate();
            rulesList.Items.Clear();
            foreach (var r in p.AppRules)
                rulesList.Items.Add(new ListViewItem(new[] { r.AppName, r.Saber.Name }) { Tag = r.Id });
            rulesList.EndUpdate();
        }
        finally
        {
            syncing = false;
        }
    }

    // MARK: Layout helpers

    static GroupBox Group(string title, params Control[] children)
    {
        var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        inner.Controls.AddRange(children);
        var box = new GroupBox { Text = title, AutoSize = true, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(330, 0) };
        box.Controls.Add(inner);
        return box;
    }

    static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.AddRange(children);
        return row;
    }

    static FlowLayoutPanel Labeled(string label, Control control, int labelWidth = 60) =>
        Row(new Label { Text = label, Width = labelWidth, Padding = new Padding(0, 6, 0, 0) }, control);

    static Button ActionButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// Labelled trackbar mapped onto a continuous range.
    sealed class Slider : FlowLayoutPanel
    {
        readonly TrackBar bar = new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Width = 300 };
        readonly Label label = new() { AutoSize = true };
        readonly string title;
        readonly double min, max;
        readonly Func<double, string> format;
        bool setting;
        public event Action<double>? ValueChanged;

        public Slider(string title, double min, double max, Func<double, string>? format = null)
        {
            this.title = title;
            this.min = min;
            this.max = max;
            this.format = format ?? (v => $"{Math.Round(v * 100)}%");
            FlowDirection = FlowDirection.TopDown;
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(0, 2, 0, 2);
            Controls.Add(label);
            Controls.Add(bar);
            bar.ValueChanged += (_, _) =>
            {
                UpdateLabel();
                if (!setting) ValueChanged?.Invoke(Value);
            };
            UpdateLabel();
        }

        public double Value
        {
            get => min + (max - min) * bar.Value / 100.0;
            set
            {
                setting = true;
                bar.Value = (int)Math.Round(Math.Clamp((value - min) / (max - min), 0, 1) * 100);
                setting = false;
                UpdateLabel();
            }
        }

        void UpdateLabel() => label.Text = $"{title}: {format(Value)}";
    }
}
