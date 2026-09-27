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
    (SaberConfig Config, int Ext, int Frame, float Scale)? cacheKey;

    public CursorEngine(AppSettings settings)
    {
        this.settings = settings;
        displayed = settings.Prefs.Saber;
        timer.Tick += (_, _) => Tick();
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
        Native.timeBeginPeriod(1);
        overlay.Show();
        Native.GetCursorPos(out lastMouse);
        lastMoveTime = Now;
        ext = 0;
        wasRetracted = true;
        SystemCursors.Apply();
        timer.Start();
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        timer.Stop();
        Native.timeEndPeriod(1);
        overlay.Hide();
        SystemCursors.Restore();
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
            overlay.Present(mouse.X, mouse.Y, 1, 1, _ => { });
            return;
        }
        Render(now, mouse, (float)(p.Scale * dpi), p, dpi);
    }

    void Render(double now, Native.POINT mouse, float scale, Prefs p, double dpi)
    {
        var cfg = displayed;
        bool animated = cfg.Animated || cfg.BladeStyle == BladeStyle.Unstable;
        var key = (cfg, (int)(ext * 120), animated && ext > 0 ? (int)(now * 40) : 0, scale);
        if (cached == null || cacheKey != key)
        {
            cached?.Dispose();
            cached = SaberRenderer.Render(cfg, new SaberState(ext, now), scale);
            cacheKey = key;
        }

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
    }

    public void Dispose()
    {
        Stop();
        timer.Dispose();
        cached?.Dispose();
        overlay.Dispose();
    }
}
