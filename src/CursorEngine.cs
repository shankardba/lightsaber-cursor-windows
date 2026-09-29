using System.Diagnostics;
using SkiaSharp;

namespace LightsaberCursor;

/// Frame loop: tracks the pointer, animates idle retract / ignite, click sparks and the motion trail,
/// resolves auto-switch rules, and draws the result into the overlay.
internal sealed class CursorEngine : IDisposable
{
    readonly AppSettings settings;
    readonly Overlay overlay = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    readonly Stopwatch clock = Stopwatch.StartNew();

    double lastTick, lastMoveTime, lastResolve, lastTopmost;
    Native.POINT lastMouse;
    double speed;
    double ext;
    bool wasRetracted = true;
    bool running;

    SaberConfig displayed;
    string displayedReason = "base";
    (SaberConfig Saber, string Reason)? pending;
    public string ActiveDescription { get; private set; } = "";
    public event Action? ActiveChanged;

    double? sparkStart;
    SKPoint sparkPoint;
    bool mouseWasDown;
    readonly List<(double T, SKPoint Emitter, SKPoint Tip)> history = new();

    RenderedImage? cached;
    bool cachedOwned;
    (SaberConfig Config, int Ext, int Frame, float Scale)? cacheKey;

    // Frame rate follows what's on screen: full speed while the pointer moves or the blade animates, 24 Hz for a
    // flickering blade at rest (the rate its frame loop plays at), and 10 Hz when nothing changes, to save battery.
    enum Pace { Fast, Shimmer, Rest }
    Pace pace = Pace.Fast;
    bool fineTimer, listening;

    // What the overlay shows now, so unchanged frames aren't redrawn.
    bool presented, trailShown;
    (int X, int Y)? lastPresentedMouse;

    // A fully lit flickering blade replays a loop of frames drawn once, instead of redrawing every frame.
    // 46 frames at 24 fps span two turns of the Inquisitor ring's three-fold symmetry, so its spin loops seamlessly.
    const int LoopFrames = 46;
    static readonly double LoopDuration = 2 * (2 * Math.PI / 3) / 2.2;
    readonly Dictionary<int, RenderedImage> loop = new();
    (SaberConfig Config, float Scale)? loopKey;

    public CursorEngine(AppSettings settings)
    {
        this.settings = settings;
        displayed = settings.Prefs.Saber;
        timer.Tick += (_, _) => Tick();
        overlay.MouseInput += Wake;
        settings.Changed += () =>
        {
            if (settings.Prefs.Enabled && !running) Start();
            else if (!settings.Prefs.Enabled && running) Stop();
            lastResolve = 0;
        };
    }

    double Now => clock.Elapsed.TotalSeconds;
    public bool IsRunning => running;

    public void Start()
    {
        if (running) return;
        running = true;
        overlay.Show();
        Native.GetCursorPos(out lastMouse);
        lastMoveTime = Now;
        ext = 0;
        wasRetracted = true;
        presented = false;
        SystemCursors.Apply();
        SetPace(Pace.Fast);
        timer.Start();
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        timer.Stop();
        if (fineTimer) Native.timeEndPeriod(1);
        fineTimer = false;
        if (listening) overlay.ListenForMouse(false);
        listening = false;
        overlay.Hide();
        SystemCursors.Restore();
    }

    void SetPace(Pace p)
    {
        pace = p;
        timer.Interval = p switch { Pace.Fast => 8, Pace.Shimmer => 42, _ => 100 };
        // 8 ms frames need Windows' 1 ms timer resolution; holding it all the time drains laptop batteries.
        bool fine = p == Pace.Fast;
        if (fine != fineTimer)
        {
            if (fine) Native.timeBeginPeriod(1); else Native.timeEndPeriod(1);
            fineTimer = fine;
        }
        // While resting, any mouse movement or click wakes the loop straight away.
        bool listen = p != Pace.Fast;
        if (listen != listening)
        {
            overlay.ListenForMouse(listen);
            listening = listen;
        }
    }

    void Wake()
    {
        if (!running || pace == Pace.Fast) return;
        SetPace(Pace.Fast);
        Tick();
    }

    /// Picks the frame rate for what's happening now.
    void UpdatePace(double now, bool settled, bool saberShowing)
    {
        var cfg = displayed;
        var want = !settled || now - lastMoveTime < 0.6 || mouseWasDown || sparkStart != null ? Pace.Fast
            : ext > 0 && saberShowing && (cfg.Animated || cfg.BladeStyle == BladeStyle.Unstable) ? Pace.Shimmer
            : Pace.Rest;
        if (want != pace) SetPace(want);
    }

    (SaberConfig, string, string) Resolve()
    {
        var p = settings.Prefs;
        if (p.PerApp && ForegroundExe() is string exe)
        {
            var rule = p.AppRules.FirstOrDefault(r => string.Equals(r.ExeName, exe, StringComparison.OrdinalIgnoreCase));
            if (rule != null) return (rule.Saber, "app:" + exe, $"{rule.Saber.Name} (rule for {rule.AppName})");
        }
        if (p.AfterDark && settings.IsNight()) return (p.NightSaber, "night", $"{p.NightSaber.Name} (after dark)");
        return (p.Saber, "base", p.Saber.Name);
    }

    static string? ForegroundExe()
    {
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    void UpdateActive()
    {
        var (cfg, reason, desc) = Resolve();
        if (desc != ActiveDescription)
        {
            ActiveDescription = desc;
            ActiveChanged?.Invoke();
        }
        if (reason != displayedReason)
        {
            if (ext > 0.05) pending = (cfg, reason);
            else { displayed = cfg; displayedReason = reason; }
        }
        else if (pending == null && cfg != displayed)
        {
            displayed = cfg;
        }
    }

    void Tick()
    {
        var sw = Stopwatch.StartNew();
        TickCore();
        Log.Time("tick", sw.Elapsed.TotalMilliseconds);
    }

    void TickCore()
    {
        double now = Now;
        double dt = Math.Clamp(now - lastTick, 1.0 / 240, 0.05);
        lastTick = now;
        var p = settings.Prefs;

        Native.GetCursorPos(out var mouse);
        double dist = Math.Sqrt(Math.Pow(mouse.X - lastMouse.X, 2) + Math.Pow(mouse.Y - lastMouse.Y, 2));
        if (dist > 0) lastMoveTime = now;
        double dpi = Native.DpiScaleAt(mouse);
        speed += (dist / dpi / dt - speed) * 0.35;
        lastMouse = mouse;

        bool down = Native.IsDown(Native.VK_LBUTTON) || Native.IsDown(Native.VK_RBUTTON) || Native.IsDown(Native.VK_MBUTTON);
        if (down && !mouseWasDown)
        {
            lastMoveTime = now;
            if (p.ClickSpark)
            {
                sparkStart = now;
                sparkPoint = new SKPoint(mouse.X, mouse.Y);
            }
        }
        mouseWasDown = down;

        if (now - lastResolve > 0.5 || (pending == null && p.Saber != displayed && displayedReason == "base"))
        {
            lastResolve = now;
            UpdateActive();
        }
        if (now - lastTopmost > 1)
        {
            lastTopmost = now;
            overlay.KeepOnTop();
        }

        bool idle = p.RetractWhenIdle && now - lastMoveTime > p.IdleSeconds;
        double target = (idle || pending != null) ? 0 : 1;
        if (ext < target)
        {
            if (wasRetracted)
            {
                wasRetracted = false;
                if (p.RandomOnIgnite && displayedReason == "base" && lastTick > 1)
                {
                    settings.Randomize();
                    displayed = settings.Prefs.Saber;
                }
            }
            ext = Math.Min(target, ext + dt / 0.18);
        }
        else if (ext > target)
        {
            ext = Math.Max(target, ext - dt / (pending != null ? 0.14 : 0.35));
        }
        if (ext <= 0.001)
        {
            wasRetracted = true;
            if (pending is { } next)
            {
                displayed = next.Saber;
                displayedReason = next.Reason;
                pending = null;
            }
        }

        // Native pointers (resize, busy, app-specific) take over from the saber whenever they appear.
        if (!SystemCursors.SaberCursorShowing())
        {
            history.Clear();
            if (presented) overlay.Present(mouse.X, mouse.Y, 1, 1, _ => { });
            presented = false;
            UpdatePace(now, true, false);
            return;
        }
        Render(now, mouse, (float)(p.Scale * dpi), p, dpi);
        UpdatePace(now, ext == target && pending == null, true);
    }

    void SetCached(RenderedImage? image, bool owned)
    {
        if (cachedOwned) cached?.Dispose();
        cached = image;
        cachedOwned = owned;
    }

    void ClearLoop()
    {
        if (!cachedOwned && cached != null) cached = null;
        foreach (var img in loop.Values) img.Dispose();
        loop.Clear();
    }

    void Render(double now, Native.POINT mouse, float scale, Prefs p, double dpi)
    {
        var cfg = displayed;
        bool animated = cfg.Animated || cfg.BladeStyle == BladeStyle.Unstable;
        bool looping = animated && ext >= 1;
        int frame = !animated || ext <= 0 ? 0
            : looping ? (int)(now / LoopDuration * LoopFrames) % LoopFrames
            : (int)(now * 40);
        var key = (cfg, (int)(ext * 120), frame, scale);
        bool keyChanged = cached == null || cacheKey != key;
        // Nothing moved and the image is the same: leave the overlay alone.
        if (!keyChanged && presented && lastPresentedMouse == (mouse.X, mouse.Y) && sparkStart == null && !trailShown)
            return;
        if (keyChanged)
        {
            if (looping)
            {
                if (loopKey != (cfg, scale))
                {
                    ClearLoop();
                    loopKey = (cfg, scale);
                }
                if (!loop.TryGetValue(frame, out var img))
                {
                    img = SaberRenderer.Render(cfg, new SaberState(1, frame * LoopDuration / LoopFrames), scale);
                    loop[frame] = img;
                }
                SetCached(img, owned: false);
            }
            else
            {
                SetCached(SaberRenderer.Render(cfg, new SaberState(ext, now), scale), owned: true);
            }
            cacheKey = key;
        }
        if (cached == null) return;

        var tipFull = new SKPoint(mouse.X, mouse.Y);
        var saberRect = SKRect.Create(mouse.X - cached.Anchor.X, mouse.Y - cached.Anchor.Y, cached.Width, cached.Height);
        var bounds = saberRect;

        // Motion trail: quads between successive blade positions, fading with age and scaled by speed.
        float L = SaberRenderer.BladeLength(cfg) * scale;
        var d = SaberRenderer.Direction;
        var emitter = new SKPoint(tipFull.X - d.X * L, tipFull.Y - d.Y * L);
        float e = SaberRenderer.Smooth(ext);
        var tip = new SKPoint(emitter.X + d.X * L * e, emitter.Y + d.Y * L * e);
        const double window = 0.09;
        history.Add((now, emitter, tip));
        history.RemoveAll(h => now - h.T > window);
        double strength = Math.Clamp((speed - 350) / 1600, 0, 1);
        bool trail = p.MotionTrail && ext > 0.6 && strength > 0.01 && history.Count >= 2;
        if (trail)
            foreach (var h in history)
            {
                bounds.Union(SKRect.Create(h.Emitter.X - 8 * scale, h.Emitter.Y - 8 * scale, 16 * scale, 16 * scale));
                bounds.Union(SKRect.Create(h.Tip.X - 8 * scale, h.Tip.Y - 8 * scale, 16 * scale, 16 * scale));
            }

        double sparkProgress = sparkStart is double s ? (now - s) / 0.32 : 1;
        if (sparkProgress >= 1) sparkStart = null;
        float sparkR = 30 * scale;
        if (sparkStart != null)
            bounds.Union(SKRect.Create(sparkPoint.X - sparkR, sparkPoint.Y - sparkR, 2 * sparkR, 2 * sparkR));

        int ox = (int)MathF.Floor(bounds.Left), oy = (int)MathF.Floor(bounds.Top);
        int w = (int)MathF.Ceiling(bounds.Right) - ox, h2 = (int)MathF.Ceiling(bounds.Bottom) - oy;
        var bladeColor = cfg.BladeStyle == BladeStyle.Darksaber ? new RGB(0.9, 0.94, 1) : cfg.Blade;

        overlay.Present(ox, oy, w, h2, canvas =>
        {
            if (trail)
            {
                using var paint = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f * scale) };
                for (int i = 0; i + 1 < history.Count; i++)
                {
                    var a = history[i];
                    var b = history[i + 1];
                    double age = (now - a.T) / window;
                    paint.Color = bladeColor.Sk(Math.Max(0, (1 - age) * 0.6 * strength) * 0.8);
                    using var quad = new SKPath();
                    quad.MoveTo(a.Emitter.X - ox, a.Emitter.Y - oy);
                    quad.LineTo(a.Tip.X - ox, a.Tip.Y - oy);
                    quad.LineTo(b.Tip.X - ox, b.Tip.Y - oy);
                    quad.LineTo(b.Emitter.X - ox, b.Emitter.Y - oy);
                    quad.Close();
                    canvas.DrawPath(quad, paint);
                }
            }
            canvas.DrawImage(cached.Image, saberRect.Left - ox, saberRect.Top - oy);
            if (sparkStart != null)
                SaberRenderer.DrawSpark(canvas, new SKPoint(sparkPoint.X - ox, sparkPoint.Y - oy), sparkProgress, bladeColor, scale);
        });
        presented = true;
        lastPresentedMouse = (mouse.X, mouse.Y);
        trailShown = trail;
    }

    public void Dispose()
    {
        Stop();
        timer.Dispose();
        SetCached(null, false);
        ClearLoop();
        overlay.Dispose();
    }
}
