using SkiaSharp;

namespace LightsaberCursor;

public struct SaberState
{
    public double Ext;
    public double Time;
    public SaberState(double ext, double time = 0) { Ext = ext; Time = time; }
}

/// A rendered saber; Anchor is the hotspot (fully extended blade tip) in pixels from the top-left.
public sealed class RenderedImage : IDisposable
{
    public required SKImage Image { get; init; }
    public required SKPointI Anchor { get; init; }
    public int Width => Image.Width;
    public int Height => Image.Height;
    public void Dispose() => Image.Dispose();
}

/// Port of the macOS renderer. Drawing happens in a local frame where x runs across the saber and +y runs
/// from the emitter (y = 0) toward the blade tip; the hilt occupies negative y. The canvas is flipped so
/// this frame is y-up, matching the original CoreGraphics code.
public static class SaberRenderer
{
    public const float Angle = 35f * MathF.PI / 180f;

    public static float BladeLength(SaberConfig c) =>
        44f * (float)c.BladeLength * (c.BladeStyle == BladeStyle.Darksaber ? 1.15f : 1f);

    /// Unit vector in y-down screen space from the emitter toward the tip.
    public static SKPoint Direction => new(-MathF.Sin(Angle), -MathF.Cos(Angle));

    public static float Smooth(double x)
    {
        var t = (float)Math.Clamp(x, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// Size and hotspot in pixels for a given pixels-per-unit scale.
    public static (SKSizeI Size, SKPointI Hotspot) Layout(SaberConfig c, float scale, bool tight = false)
    {
        float total = BladeLength(c) + c.Hilt.Length() + 2;
        float pad = tight ? 7 : 18 + 26 * (float)c.GlowRadius;
        int w = (int)MathF.Ceiling((total * MathF.Sin(Angle) + 2 * pad) * scale);
        int h = (int)MathF.Ceiling((total * MathF.Cos(Angle) + 2 * pad) * scale);
        return (new SKSizeI(w, h), new SKPointI((int)MathF.Round(pad * scale), (int)MathF.Round(pad * scale)));
    }

    public static RenderedImage Render(SaberConfig c, SaberState s, float scale, bool tight = false)
    {
        var (size, hot) = Layout(c, scale, tight);
        var info = new SKImageInfo(Math.Max(1, size.Width), Math.Max(1, size.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        Draw(c, s, canvas, new SKPoint(hot.X, hot.Y), scale);
        return new RenderedImage { Image = surface.Snapshot(), Anchor = hot };
    }

    public static void Draw(SaberConfig c, SaberState s, SKCanvas canvas, SKPoint hotspot, float scale)
    {
        float L = BladeLength(c);
        canvas.Save();
        canvas.Translate(hotspot.X, hotspot.Y);
        canvas.Scale(scale, -scale);
        canvas.RotateRadians(Angle);
        canvas.Translate(0, -L);
        DrawBlade(c, s, canvas, L);
        DrawHilt(c, canvas, c.Animated ? s.Time : 0);
        DrawHotspotMarker(c, s, canvas, L);
        canvas.Restore();
    }

    /// Horizontal hilt-only icon (emitter pointing right) for pickers.
    public static SKImage RenderHiltIcon(SaberConfig c, int height)
    {
        float s = height / 12f;
        var info = new SKImageInfo((int)MathF.Ceiling((c.Hilt.Length() + 6) * s), height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(info.Width - 3 * s, height / 2f);
        canvas.Scale(s, -s);
        canvas.RotateRadians(-MathF.PI / 2);
        DrawHilt(c, canvas);
        return surface.Snapshot();
    }

    // MARK: Paint helpers

    static SKPaint Fill(SKColor color, float blurSigma = 0) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = color,
        MaskFilter = blurSigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurSigma) : null,
    };

    static SKPaint Stroke(SKColor color, float width, SKStrokeCap cap = SKStrokeCap.Butt,
        SKStrokeJoin join = SKStrokeJoin.Miter, float blurSigma = 0) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        Color = color,
        StrokeWidth = width,
        StrokeCap = cap,
        StrokeJoin = join,
        MaskFilter = blurSigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurSigma) : null,
    };

    /// CoreGraphics' shadow blur maps roughly to a Gaussian with sigma = blur / 2 (in local units here).
    static float Sigma(float cgBlur) => Math.Max(0.01f, cgBlur / 2);

    static void DrawShadowed(SKCanvas canvas, SKPath path, SKColor fill, SKColor shadow, float cgBlur)
    {
        using (var sp = Fill(shadow, Sigma(cgBlur))) canvas.DrawPath(path, sp);
        using var fp = Fill(fill);
        canvas.DrawPath(path, fp);
    }

    // MARK: Blade

    static SKPath Capsule(float y0, float y1, float w)
    {
        float h = MathF.Max(0.01f, y1 - y0);
        float r = MathF.Min(w / 2, h / 2);
        var p = new SKPath();
        p.AddRoundRect(SKRect.Create(-w / 2, y0, w, h), r, r);
        return p;
    }

    public static float Hash(int i, int seed)
    {
        unchecked
        {
            uint x = (uint)(i * 374_761_393 + seed * 668_265_263 + 1_013_904_223);
            x = (x ^ (x >> 13)) * 1_274_126_177u;
            x ^= x >> 16;
            return (x & 0xFFFF) / 65535f;
        }
    }

    static SKPath UnstablePath(float len, float w, int seed)
    {
        int n = Math.Max(4, (int)(len / 2.2f));
        var p = new SKPath();
        p.MoveTo(0, -0.5f);
        for (int i = 0; i <= n; i++)
        {
            float y = len * i / n;
            float k1 = Hash(i, seed) - 0.3f;
            p.LineTo(-w / 2 * (1 + 0.55f * k1), y);
        }
        p.LineTo(0, len + w * 0.35f);
        for (int i = n; i >= 0; i--)
        {
            float y = len * i / n;
            float k2 = Hash(i + 97, seed) - 0.3f;
            p.LineTo(w / 2 * (1 + 0.55f * k2), y);
        }
        p.Close();
        return p;
    }

    static float DarksaberHalfWidth(float w) => w * 0.92f;

    /// Flat, wide blade with a chisel-cut tip whose point sits on the upper edge, like a katana.
    static SKPath DarksaberPath(float len, float w)
    {
        float hw = DarksaberHalfWidth(w);
        float cut = MathF.Min(len, hw * 2.6f);
        var p = new SKPath();
        p.MoveTo(-hw, -0.5f);
        p.LineTo(-hw, len - cut);
        p.LineTo(hw * 0.55f, len);
        p.LineTo(hw, len - cut * 0.35f);
        p.LineTo(hw, -0.5f);
        p.Close();
        return p;
    }

    static void DarksaberCrackle(SKCanvas canvas, float len, float w, int seed)
    {
        float hw = DarksaberHalfWidth(w);
        float top = len - hw * 2.6f;
        if (top <= 2) return;
        int n = Math.Max(6, (int)(top / 0.9f));
        for (int strand = 0; strand < 2; strand++)
        {
            using var path = new SKPath();
            path.MoveTo((Hash(strand, seed) - 0.5f) * hw, 1);
            for (int i = 1; i <= n; i++)
            {
                float y = 1 + (top - 1) * i / n;
                float x = (Hash(i * 3 + strand * 101, seed) - 0.5f) * hw * 0.95f;
                path.LineTo(x, y);
            }
            using (var glow = Stroke(new RGB(0.85, 0.92, 1).Sk(0.9), 0.26f, SKStrokeCap.Round, blurSigma: Sigma(1.6f)))
                canvas.DrawPath(path, glow);
            using var line = Stroke(new RGB(0.95, 0.97, 1).Sk(0.9), 0.26f, SKStrokeCap.Round);
            canvas.DrawPath(path, line);
        }
    }

    /// Wide, flat metal blade with a symmetric point so the hotspot stays exactly at the tip.
    static SKPath SwordPath(float len, float w)
    {
        float hw = w * 0.95f;
        float taper = MathF.Min(len, hw * 3.2f);
        var p = new SKPath();
        p.MoveTo(-hw, -0.5f);
        p.LineTo(-hw, len - taper);
        p.LineTo(0, len);
        p.LineTo(hw, len - taper);
        p.LineTo(hw, -0.5f);
        p.Close();
        return p;
    }

    /// Polished metal blade: sheen across the width, etched fishbone lines, and (when animated) a gleam that travels up it.
    static void DrawSword(SaberConfig c, SKCanvas canvas, float len, float w, double t)
    {
        float hw = w * 0.95f;
        float taper = MathF.Min(len, hw * 3.2f);
        using var body = SwordPath(len, w);
        var baseColor = c.Blade;
        var light = baseColor.Mix(RGB.White, 0.5);
        var dark = baseColor.Mix(RGB.Black, 0.45);

        DrawShadowed(canvas, body, baseColor.Sk(), baseColor.Mix(RGB.White, 0.3).Sk(Math.Min(1, 0.4 * c.GlowIntensity)),
            3 + 5 * (float)c.GlowRadius);

        canvas.Save();
        canvas.ClipPath(body, antialias: true);
        using (var sheen = new SKPaint { IsAntialias = true })
        {
            sheen.Shader = SKShader.CreateLinearGradient(new SKPoint(-hw, 0), new SKPoint(hw, 0),
                new[] { dark.Sk(), light.Sk(), baseColor.Sk(), light.Mix(baseColor, 0.5).Sk(), dark.Sk() },
                new[] { 0f, 0.22f, 0.5f, 0.78f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(-hw - 1, -1, 2 * hw + 2, len + 2), sheen);
        }

        float etchTop = len - taper * 0.8f;
        if (etchTop > 2)
        {
            using var etch = Stroke(dark.Mix(RGB.Black, 0.2).Sk(0.75), 0.22f);
            foreach (float side in new[] { -1f, 1f })
            {
                using var zig = new SKPath();
                float y = 1;
                bool outward = true;
                zig.MoveTo(side * hw * 0.42f, y);
                while (y < etchTop)
                {
                    y = MathF.Min(etchTop, y + 0.7f);
                    zig.LineTo(side * hw * (outward ? 0.64f : 0.2f), y);
                    outward = !outward;
                }
                canvas.DrawPath(zig, etch);
            }
            using var ridge = Stroke(light.Sk(0.6), 0.18f);
            canvas.DrawLine(0, 0.5f, 0, len - taper * 0.6f, ridge);
        }

        if (c.Animated)
        {
            float travel = len + 8;
            float gy = (float)(t * 0.55 % 1) * travel - 4;
            using var gleam = new SKPaint { IsAntialias = true };
            gleam.Shader = SKShader.CreateLinearGradient(new SKPoint(0, gy - 2.5f), new SKPoint(0, gy + 2.5f),
                new[] { RGB.White.Sk(0), RGB.White.Sk(0.55), RGB.White.Sk(0) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(-hw - 1, gy - 2.5f, 2 * hw + 2, 5), gleam);
        }
        canvas.Restore();

        using var outline = Stroke(dark.Mix(RGB.Black, 0.35).Sk(0.95), 0.35f);
        canvas.DrawPath(body, outline);
    }

    static void GlowShape(SKCanvas canvas, SKPath body, SKPath halo, SKPath? core, SaberConfig c, float I, float R)
    {
        bool dark = c.BladeStyle == BladeStyle.Darksaber;
        var glowRGB = dark ? new RGB(0.9, 0.94, 1) : c.Blade;
        float haloAlpha = dark ? 0.35f : 0.85f;

        DrawShadowed(canvas, halo, glowRGB.Sk(haloAlpha), glowRGB.Sk(MathF.Min(1, 0.6f * I) * haloAlpha), R * 2.2f);
        DrawShadowed(canvas, body, dark ? new RGB(0.12, 0.13, 0.15).Sk() : c.Blade.Sk(), glowRGB.Sk(MathF.Min(1, 0.95f * I)), R * 0.8f);

        if (dark)
        {
            using (var g = Stroke(new RGB(0.85, 0.92, 1).Sk(MathF.Min(1, 0.9f * I)), 0.6f, blurSigma: Sigma(2.5f)))
                canvas.DrawPath(body, g);
            using var edge = Stroke(new RGB(0.9, 0.93, 0.97).Sk(0.95), 0.6f);
            canvas.DrawPath(body, edge);
        }
        else if (core != null)
        {
            double whiten = Math.Min(1, 0.5 + 0.45 * c.CoreWhiteness);
            using var cp = Fill(c.Blade.Mix(RGB.White, whiten).Sk());
            canvas.DrawPath(core, cp);
        }
    }

    static void DrawBlade(SaberConfig c, SaberState s, SKCanvas canvas, float L)
    {
        float e = Smooth(s.Ext);
        if (e <= 0.005f) return;
        float len = L * e;
        double t = s.Time;
        bool unstable = c.BladeStyle == BladeStyle.Unstable;
        bool dark = c.BladeStyle == BladeStyle.Darksaber;
        float flick = 1;
        if (c.Animated || unstable)
            flick = (float)(1 + 0.08 * Math.Sin(t * 29) + 0.05 * Math.Sin(t * 67 + 1.3) + 0.04 * Math.Sin(t * 143 + 0.7));
        float w = 3.3f * (float)c.Thickness;
        float I = (float)c.GlowIntensity * flick;
        float R = 4 + 8 * (float)c.GlowRadius;
        int seed = (c.Animated || unstable) ? (int)(t * 24) : 0;

        if (c.BladeStyle == BladeStyle.Sword)
        {
            DrawSword(c, canvas, len, w, t);
            return;
        }

        using var body = dark ? DarksaberPath(len, w) : unstable ? UnstablePath(len, w, seed) : Capsule(-0.5f, len, w);
        using var core = Capsule(0, len - w * 0.2f, w * (0.4f + 0.22f * (float)c.CoreWhiteness));
        using var halo = dark ? new SKPath(body) : Capsule(-0.5f, len, w * 0.9f);
        GlowShape(canvas, body, halo, core, c, I, R);

        if (dark) DarksaberCrackle(canvas, len, w, seed);

        if (unstable)
        {
            using var path = new SKPath();
            for (int i = 0; i < 5; i++)
            {
                float y = len * (0.12f + 0.8f * Hash(i, seed + 7));
                float side = Hash(i, seed + 3) > 0.5f ? 1 : -1;
                float x0 = side * w * 0.5f;
                path.MoveTo(x0, y);
                path.LineTo(x0 + side * (1.2f + 2.2f * Hash(i, seed + 11)), y + 1.5f * (Hash(i, seed + 5) - 0.5f));
            }
            using (var g = Stroke(c.Blade.Sk(), 0.45f, SKStrokeCap.Round, blurSigma: Sigma(3)))
                canvas.DrawPath(path, g);
            using var line = Stroke(c.Blade.Mix(RGB.White, 0.35).Sk(0.9), 0.45f, SKStrokeCap.Round);
            canvas.DrawPath(path, line);
        }

        if (c.Hilt == HiltStyle.Crossguard)
        {
            float qlen = 7.5f * e;
            float qw = w * 0.72f;
            foreach (float sign in new[] { -1f, 1f })
            {
                float x0 = sign * 4f, x1 = sign * (4f + qlen);
                var rect = SKRect.Create(MathF.Min(x0, x1), -3.2f - qw / 2, MathF.Abs(x1 - x0), qw);
                float r = MathF.Min(qw / 2, rect.Width / 2);
                using var qb = new SKPath();
                qb.AddRoundRect(rect, r, r);
                var cr = SKRect.Inflate(rect, -0.3f, -qw * 0.28f);
                float cr2 = MathF.Max(0, MathF.Min(cr.Height / 2, cr.Width / 2));
                using var qc = new SKPath();
                if (cr.Width > 0 && cr.Height > 0) qc.AddRoundRect(cr, cr2, cr2);
                GlowShape(canvas, qb, qb, qc, c, I * 0.8f, R * 0.7f);
            }
        }
    }

    static void DrawHotspotMarker(SaberConfig c, SaberState s, SKCanvas canvas, float L)
    {
        float e = Smooth(s.Ext);
        if (e >= 0.98f) return;
        float a = (1 - e) * 0.75f;
        var color = c.BladeStyle == BladeStyle.Darksaber ? new RGB(0.9, 0.94, 1) : c.Blade;
        using (var g = Fill(color.Sk(a * a), Sigma(4))) canvas.DrawOval(0, L, 1.5f, 1.5f, g);
        using (var f = Fill(color.Sk(a))) canvas.DrawOval(0, L, 1.5f, 1.5f, f);
        using var w = Fill(RGB.White.Sk(a));
        canvas.DrawOval(0, L, 0.6f, 0.6f, w);
    }

    // MARK: Hilt helpers

    static readonly RGB Rubber = new(0.07, 0.07, 0.08);
    static readonly RGB Leather = new(0.36, 0.22, 0.12);
    static readonly RGB Rag = new(0.38, 0.33, 0.27);

    static void Metal(SKCanvas canvas, SKPath path, HiltFinish f, float hw = 4)
    {
        var colors = new[] { f.Dark().Sk(), f.Light().Sk(), f.Base().Sk(), f.Dark().Mix(f.Base(), 0.5).Sk(), f.Dark().Sk() };
        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(-hw, 0), new SKPoint(hw, 0), colors,
                new[] { 0f, 0.28f, 0.55f, 0.8f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawPath(path, paint);
        }
        using var outline = Stroke(f.Dark().Mix(RGB.Black, 0.5).Sk(0.9), 0.3f);
        canvas.DrawPath(path, outline);
    }

    static SKPath RRect(float x, float top, float w, float h, float r = 0.6f)
    {
        var p = new SKPath();
        p.AddRoundRect(SKRect.Create(x, top - h, w, h), MathF.Min(r, w / 2), MathF.Min(r, h / 2));
        return p;
    }

    static void Seg(SKCanvas canvas, float top, float h, float w, HiltFinish f, float r = 0.6f)
    {
        using var p = RRect(-w / 2, top, w, h, r);
        Metal(canvas, p, f, w / 2);
    }

    static SKPath Poly(params (float X, float Y)[] pts)
    {
        var p = new SKPath();
        p.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i].X, pts[i].Y);
        p.Close();
        return p;
    }

    static void MetalPoly(SKCanvas canvas, HiltFinish f, params (float, float)[] pts)
    {
        using var p = Poly(pts);
        Metal(canvas, p, f);
    }

    static void FillPath(SKCanvas canvas, SKPath path, RGB color, double a = 1)
    {
        using var paint = Fill(color.Sk(a));
        canvas.DrawPath(path, paint);
    }

    static void FillRRect(SKCanvas canvas, float x, float top, float w, float h, float r, RGB color, double a = 1)
    {
        using var p = RRect(x, top, w, h, r);
        FillPath(canvas, p, color, a);
    }

    static void HRidges(SKCanvas canvas, float top, float bottom, float w, int count, RGB color, float thickness = 0.6f)
    {
        float step = (top - bottom) / count;
        for (int i = 0; i < count; i++)
        {
            float y = top - step * (i + 0.5f);
            FillRRect(canvas, -w / 2 - 0.05f, y + thickness / 2, w + 0.1f, thickness, 0.2f, color, 0.85);
        }
    }

    static void VStrips(SKCanvas canvas, float top, float bottom, float[] xs, float sw, RGB color)
    {
        foreach (var x in xs) FillRRect(canvas, x - sw / 2, top, sw, top - bottom, sw / 2, color, 0.92);
    }

    static void DiagWraps(SKCanvas canvas, float top, float bottom, float w, int count, RGB color, SKPath clip)
    {
        canvas.Save();
        canvas.ClipPath(clip, antialias: true);
        using var path = new SKPath();
        float step = (top - bottom) / count;
        for (int i = 0; i <= count; i++)
        {
            float y = top - step * i;
            path.MoveTo(-w / 2 - 0.5f, y + 1.1f);
            path.LineTo(w / 2 + 0.5f, y - 1.1f);
        }
        using var paint = Stroke(color.Sk(0.85), 0.55f);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    static void Scratches(SKCanvas canvas, float top, float bottom, float w, int seed)
    {
        using var path = new SKPath();
        for (int i = 0; i < 5; i++)
        {
            float y = bottom + (top - bottom) * Hash(i, seed);
            float x = (Hash(i, seed + 1) - 0.5f) * w * 0.8f;
            path.MoveTo(x, y);
            path.LineTo(x + 1.2f * (Hash(i, seed + 2) - 0.3f), y - 0.8f);
        }
        using var paint = Stroke(RGB.White.Sk(0.28), 0.18f);
        canvas.DrawPath(path, paint);
    }

    static void Dot(SKCanvas canvas, float x, float y, float r, RGB color)
    {
        using (var p = Fill(color.Sk())) canvas.DrawOval(x, y, r, r, p);
        using var hi = Fill(RGB.White.Sk(0.45));
        canvas.DrawOval(x - r * 0.15f, y + r * 0.35f, r * 0.3f, r * 0.3f, hi);
    }

    static void StrokeOval(SKCanvas canvas, float cx, float cy, float r, RGB color, float width)
    {
        using var p = Stroke(color.Sk(), width);
        canvas.DrawOval(cx, cy, r, r, p);
    }

    // MARK: Hilts

    public static void DrawHilt(SaberConfig c, SKCanvas canvas, double time = 0)
    {
        var f = c.Finish;
        var a = c.Accent;
        switch (c.Hilt)
        {
            case HiltStyle.Classic:
                Seg(canvas, 0, 4.6f, 7.2f, f, 1);
                foreach (var x in new[] { -2.6f, -0.6f, 1.4f }) FillRRect(canvas, x, -1.2f, 1.2f, 1.8f, 0.3f, Rubber, 0.8);
                Seg(canvas, -4.6f, 1.8f, 5.0f, f);
                Seg(canvas, -6.4f, 5.2f, 6.0f, f);
                using (var clamp = RRect(2.9f, -6.9f, 2.3f, 4.2f, 0.4f)) Metal(canvas, clamp, f.Alt(), 1.2f);
                Dot(canvas, 1.2f, -8.8f, 0.8f, a);
                Seg(canvas, -11.6f, 9.4f, 5.6f, f);
                VStrips(canvas, -12.0f, -20.6f, new[] { -1.9f, 0f, 1.9f }, 0.95f, Rubber);
                Seg(canvas, -21.0f, 3.0f, 5.0f, f, 1);
                StrokeOval(canvas, 0, -24.2f, 1.1f, f.Dark(), 0.55f);
                break;

            case HiltStyle.Ribbed:
                MetalPoly(canvas, f, (-3.7f, 0.3f), (3.7f, -1.5f), (3.7f, -5.2f), (-3.7f, -5.2f));
                Seg(canvas, -5.2f, 5.4f, 6.2f, f.Alt());
                var ys = new[] { -6.6f, -8.1f, -9.6f };
                for (int i = 0; i < ys.Length; i++) Dot(canvas, 1.7f, ys[i], 0.55f, i == 1 ? RGB.Hex(0xC8CCD2) : a);
                FillRRect(canvas, -2.6f, -6.0f, 2.2f, 3.8f, 0.3f, Rubber, 0.8);
                Seg(canvas, -10.6f, 10.6f, 5.4f, f.Alt());
                HRidges(canvas, -11.0f, -20.8f, 5.4f, 8, Rubber, 0.75f);
                Seg(canvas, -21.2f, 3.8f, 6.2f, f, 0.8f);
                break;

            case HiltStyle.Slim:
                MetalPoly(canvas, f, (-3.1f, 0), (3.1f, 0), (2.2f, -4), (-2.2f, -4));
                Seg(canvas, -4, 3, 4.4f, f);
                Seg(canvas, -7, 1.1f, 5.2f, f.Alt());
                Seg(canvas, -8.1f, 5.4f, 4.4f, f);
                FillRRect(canvas, 0.9f, -9.2f, 1.2f, 1.6f, 0.3f, a);
                Seg(canvas, -13.5f, 9.5f, 4.7f, f);
                HRidges(canvas, -14, -22.6f, 4.7f, 9, Rubber, 0.5f);
                MetalPoly(canvas, f, (-2.35f, -23), (2.35f, -23), (1.8f, -27), (-1.8f, -27));
                break;

            case HiltStyle.Curved:
            {
                Seg(canvas, 0, 3.6f, 5.8f, f);
                FillRRect(canvas, -2.9f, -2.8f, 5.8f, 0.8f, 0.2f, a);
                using var curve = new SKPath();
                curve.MoveTo(0, -3.6f);
                curve.CubicTo(0, -11, 1.4f, -17, 4.8f, -21.5f);
                using (var sp = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 5.2f, StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round })
                using (var body = sp.GetFillPath(curve))
                    Metal(canvas, body, f, 7);
                using (var dp = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 4.4f, StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round, PathEffect = SKPathEffect.CreateDash(new[] { 0.55f, 0.85f }, 0) })
                using (var ribs = dp.GetFillPath(curve))
                using (var clip = RRect(-8, -8, 20, 12))
                {
                    canvas.Save();
                    canvas.ClipPath(clip, antialias: true);
                    FillPath(canvas, ribs, a, 0.55);
                    canvas.Restore();
                }
                using (var cap = new SKPath())
                {
                    cap.AddOval(SKRect.Create(4.8f - 2.9f, -21.5f - 2.9f, 5.8f, 5.8f));
                    canvas.Save();
                    canvas.Translate(4.8f, -21.5f);
                    using var local = new SKPath(cap);
                    local.Transform(SKMatrix.CreateTranslation(-4.8f, 21.5f));
                    Metal(canvas, local, f.Alt(), 3);
                    canvas.Restore();
                }
                Dot(canvas, 4.8f, -21.5f, 0.9f, a);
                break;
            }

            case HiltStyle.Crossguard:
            {
                MetalPoly(canvas, f, (-4.2f, 0.3f), (4.3f, 0), (4.4f, -6.8f), (-4.3f, -7.0f));
                using (var l = RRect(-5.4f, -2.1f, 1.3f, 2.3f, 0.2f)) Metal(canvas, l, f.Alt(), 5);
                using (var r = RRect(4.1f, -2.1f, 1.3f, 2.3f, 0.2f)) Metal(canvas, r, f.Alt(), 5);
                FillRRect(canvas, -1.4f, -1.6f, 2.8f, 3.6f, 0.4f, Rubber, 0.7);
                Scratches(canvas, 0, -7, 8, 3);
                using var grip = RRect(-2.8f, -7, 5.6f, 10.6f);
                Metal(canvas, grip, f, 2.8f);
                DiagWraps(canvas, -7.4f, -17.2f, 5.6f, 7, Rubber, grip);
                MetalPoly(canvas, f, (-3.2f, -17.6f), (3.3f, -17.6f), (3.5f, -20), (2.1f, -21.8f), (-2.2f, -21.6f), (-3.4f, -19.8f));
                Scratches(canvas, -17.6f, -21.6f, 6, 9);
                break;
            }

            case HiltStyle.Shoto:
                Seg(canvas, 0, 3.4f, 6.2f, f, 0.9f);
                Seg(canvas, -3.4f, 8.6f, 5.2f, f);
                HRidges(canvas, -6.2f, -11.6f, 5.2f, 5, Rubber, 0.7f);
                Dot(canvas, 1.4f, -4.8f, 0.6f, a);
                Seg(canvas, -12, 3.8f, 6.0f, f, 1.8f);
                break;

            case HiltStyle.Jagged:
            {
                MetalPoly(canvas, f, (-2.6f, -3), (-4.7f, 2.8f), (-1.1f, -0.6f));
                MetalPoly(canvas, f, (2.6f, -3), (4.7f, 2.8f), (1.1f, -0.6f));
                Seg(canvas, -0.6f, 10.2f, 5.2f, f);
                FillRRect(canvas, -1.3f, -2.4f, 0.5f, 6.8f, 0.25f, a);
                FillRRect(canvas, 0.8f, -2.4f, 0.5f, 6.8f, 0.25f, a);
                using var grip = RRect(-2.3f, -10.8f, 4.6f, 8.4f);
                Metal(canvas, grip, f.Alt(), 2.3f);
                DiagWraps(canvas, -11.2f, -18.8f, 4.6f, 6, Rubber, grip);
                MetalPoly(canvas, f, (-2.8f, -19.2f), (2.8f, -19.2f), (0, -25));
                Dot(canvas, 0, -20.6f, 0.6f, a);
                break;
            }

            case HiltStyle.Angular:
                MetalPoly(canvas, f, (-3.1f, 1.0f), (3.1f, -1.4f), (3.1f, -5), (-3.1f, -5));
                Seg(canvas, -5, 12.6f, 6.2f, f, 0.25f);
                FillRRect(canvas, -1.8f, -6.4f, 3.6f, 5, 0.3f, f.Dark(), 0.75);
                FillRRect(canvas, -0.3f, -6.9f, 0.6f, 4, 0.3f, a, 0.9);
                HRidges(canvas, -12.2f, -17.4f, 6.2f, 5, Rubber, 0.7f);
                MetalPoly(canvas, f, (-3.1f, -17.6f), (3.1f, -17.6f), (3.1f, -21.6f), (-3.1f, -23.2f));
                break;

            case HiltStyle.Ornate:
            {
                Seg(canvas, 0, 4, 6.2f, f, 0.9f);
                FillRRect(canvas, -3.1f, -0.4f, 6.2f, 0.9f, 0.2f, a);
                using var hg = new SKPath();
                hg.MoveTo(-2.9f, -4);
                hg.QuadTo(-1.1f, -12, -2.9f, -20);
                hg.LineTo(2.9f, -20);
                hg.QuadTo(1.1f, -12, 2.9f, -4);
                hg.Close();
                Metal(canvas, hg, f, 2.9f);
                canvas.Save();
                canvas.ClipPath(hg, antialias: true);
                FillRRect(canvas, -3, -8, 6, 1, 0.2f, a);
                FillRRect(canvas, -3, -15.6f, 6, 1, 0.2f, a);
                canvas.Restore();
                Seg(canvas, -20, 4, 5.8f, f, 1.2f);
                FillRRect(canvas, -2.9f, -23.2f, 5.8f, 0.8f, 0.3f, a);
                break;
            }

            case HiltStyle.Banded:
            {
                Seg(canvas, 0, 4, 6.4f, f, 1);
                FillRRect(canvas, -3.3f, -4, 6.6f, 1.4f, 0.3f, a);
                using var wrap = RRect(-2.95f, -5.4f, 5.9f, 8.2f, 0.4f);
                FillPath(canvas, wrap, Leather);
                DiagWraps(canvas, -5.6f, -13.4f, 5.9f, 6, Leather.Mix(RGB.Black, 0.45), wrap);
                FillRRect(canvas, -3.2f, -13.6f, 6.4f, 1.2f, 0.3f, a);
                Seg(canvas, -14.8f, 5.4f, 6.4f, f, 1.4f);
                StrokeOval(canvas, 0, -17.5f, 1.3f, a, 0.4f);
                Dot(canvas, 0, -17.5f, 0.4f, a);
                break;
            }

            case HiltStyle.Worn:
            {
                MetalPoly(canvas, f, (-3.6f, 0.1f), (-1, 0.6f), (3.5f, 0.2f), (3.9f, -4.6f), (-3.4f, -4.9f));
                Seg(canvas, -4.9f, 5.2f, 5.8f, f);
                using (var dent = Fill(f.Dark().Sk(0.7)))
                {
                    canvas.DrawOval(-1.55f, -7.15f, 0.65f, 0.45f, dent);
                    canvas.DrawOval(1.55f, -8.85f, 0.45f, 0.35f, dent);
                }
                Dot(canvas, 1.6f, -6.4f, 0.5f, a);
                using var wrap = RRect(-2.95f, -10.1f, 5.9f, 9.6f, 0.5f);
                FillPath(canvas, wrap, Rag);
                DiagWraps(canvas, -10.3f, -19.5f, 5.9f, 7, Rag.Mix(RGB.Black, 0.5), wrap);
                MetalPoly(canvas, f, (-3, -19.7f), (3.1f, -19.7f), (3.3f, -22.4f), (-2.6f, -23.6f));
                Scratches(canvas, 0, -10, 6, 21);
                break;
            }

            case HiltStyle.Darksaber:
            {
                using (var hook = new SKPath())
                {
                    hook.MoveTo(3.2f, -0.4f);
                    hook.CubicTo(6.4f, -1.2f, 6.2f, -5.6f, 3.0f, -6.2f);
                    using (var outer = Stroke(f.Dark().Mix(RGB.Black, 0.4).Sk(), 0.75f, SKStrokeCap.Round)) canvas.DrawPath(hook, outer);
                    using var inner = Stroke(f.Light().Sk(), 0.45f, SKStrokeCap.Round);
                    canvas.DrawPath(hook, inner);
                }
                Seg(canvas, 0.3f, 2.6f, 7.2f, f, 0.3f);
                foreach (var x in new[] { -2.4f, -0.8f, 0.8f, 2.4f }) FillRRect(canvas, x - 0.18f, 0, 0.36f, 2.0f, 0.1f, f.Dark(), 0.8);
                Seg(canvas, -2.3f, 9.2f, 5.8f, f, 0.3f);
                using (var lines = new SKPath())
                {
                    lines.MoveTo(-1.6f, -3.2f); lines.LineTo(-1.6f, -7.4f); lines.LineTo(0.9f, -8.4f); lines.LineTo(0.9f, -11.0f);
                    lines.MoveTo(-0.4f, -3.2f); lines.LineTo(-0.4f, -6.6f); lines.LineTo(2.0f, -7.6f); lines.LineTo(2.0f, -11.0f);
                    using var lp = Stroke(new RGB(0.06, 0.06, 0.07).Sk(0.9), 0.4f);
                    canvas.DrawPath(lines, lp);
                }
                using var grip = RRect(-2.8f, -11.5f, 5.6f, 8.0f, 0.3f);
                Metal(canvas, grip, HiltFinish.Black, 2.8f);
                HRidges(canvas, -11.8f, -14.6f, 5.6f, 3, Rubber, 0.5f);
                foreach (var y in new[] { -15.6f, -16.5f, -17.4f }) FillRRect(canvas, -2.8f, y, 5.6f, 0.32f, 0.1f, a, 0.95);
                Seg(canvas, -19.5f, 2.6f, 5.3f, f, 0.9f);
                break;
            }

            case HiltStyle.Ancient:
            {
                // Gold crossguard with upturned curls, tapered brown grip with gold scrollwork, flared crescent pommel.
                var grip = new RGB(0.33, 0.19, 0.09);
                var gold = f.Base().Mix(f.Light(), 0.3);
                foreach (float sx in new[] { -1f, 1f })
                {
                    using var curl = new SKPath();
                    curl.MoveTo(sx * 3.0f, -0.4f);
                    curl.CubicTo(sx * 4.6f, -0.6f, sx * 4.9f, 0.6f, sx * 4.5f, 1.6f);
                    curl.CubicTo(sx * 4.2f, 2.4f, sx * 3.8f, 2.5f, sx * 3.7f, 2.3f);
                    using (var o = Stroke(f.Dark().Mix(RGB.Black, 0.3).Sk(), 0.95f, SKStrokeCap.Round)) canvas.DrawPath(curl, o);
                    using (var g = Stroke(gold.Sk(), 0.6f, SKStrokeCap.Round)) canvas.DrawPath(curl, g);
                    Dot(canvas, sx * 3.7f, 2.3f, 0.45f, gold);
                }
                using (var bar = RRect(-3.4f, 0.3f, 6.8f, 1.6f, 0.6f)) Metal(canvas, bar, f, 3.4f);
                using var gp = Poly((-2.1f, -1.3f), (2.1f, -1.3f), (1.75f, -12.6f), (-1.75f, -12.6f));
                using (var gpaint = new SKPaint { IsAntialias = true })
                {
                    gpaint.Shader = SKShader.CreateLinearGradient(new SKPoint(-2.1f, 0), new SKPoint(2.1f, 0),
                        new[] { grip.Mix(RGB.Black, 0.4).Sk(), grip.Mix(RGB.White, 0.25).Sk(), grip.Sk(), grip.Mix(RGB.Black, 0.3).Sk() },
                        new[] { 0f, 0.3f, 0.6f, 1f }, SKShaderTileMode.Clamp);
                    canvas.DrawPath(gp, gpaint);
                }
                using (var scroll = Stroke(a.Sk(0.95), 0.35f, SKStrokeCap.Round))
                {
                    canvas.DrawOval(0, -3.0f, 0.8f, 0.8f, scroll);
                    canvas.DrawOval(0, -4.5f, 0.8f, 0.8f, scroll);
                    foreach (var top in new[] { -6.3f, -9.3f })
                    {
                        using var sPath = new SKPath();
                        sPath.MoveTo(-1.1f, top);
                        sPath.CubicTo(1.6f, top - 0.2f, -1.6f, top - 2.2f, 1.1f, top - 2.4f);
                        canvas.DrawPath(sPath, scroll);
                    }
                }
                using (var go = Stroke(grip.Mix(RGB.Black, 0.6).Sk(0.9), 0.3f)) canvas.DrawPath(gp, go);
                using (var collar = RRect(-2.0f, -12.4f, 4.0f, 1.0f, 0.4f)) Metal(canvas, collar, f, 2);
                using var pommel = new SKPath();
                pommel.MoveTo(-1.5f, -13.2f);
                pommel.LineTo(-1.8f, -14.2f);
                pommel.CubicTo(-2.6f, -14.6f, -3.4f, -15.4f, -3.4f, -16.4f);
                pommel.CubicTo(-2.2f, -16.6f, -1.0f, -15.6f, 0, -15.6f);
                pommel.CubicTo(1.0f, -15.6f, 2.2f, -16.6f, 3.4f, -16.4f);
                pommel.CubicTo(3.4f, -15.4f, 2.6f, -14.6f, 1.8f, -14.2f);
                pommel.LineTo(1.5f, -13.2f);
                pommel.Close();
                Metal(canvas, pommel, f, 3.4f);
                break;
            }

            case HiltStyle.Inquisitor:
            {
                var center = new SKPoint(0, -8.2f);
                using var ring = new SKPath { FillType = SKPathFillType.EvenOdd };
                ring.AddCircle(center.X, center.Y, 7.4f);
                ring.AddCircle(center.X, center.Y, 5.2f);
                using (var paint = new SKPaint { IsAntialias = true })
                {
                    paint.Shader = SKShader.CreateLinearGradient(new SKPoint(-7, center.Y + 7), new SKPoint(7, center.Y - 7),
                        new[] { f.Light().Sk(), f.Base().Sk(), f.Dark().Sk() }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
                    canvas.DrawPath(ring, paint);
                }
                using (var o = Stroke(f.Dark().Mix(RGB.Black, 0.5).Sk(0.9), 0.3f)) canvas.DrawPath(ring, o);
                float spin = (float)(time * 2.2);
                for (int i = 0; i < 6; i++)
                {
                    float ang = spin + i * MathF.PI / 3;
                    Dot(canvas, center.X + MathF.Cos(ang) * 6.3f, center.Y + MathF.Sin(ang) * 6.3f, i % 2 == 0 ? 0.75f : 0.5f, i % 2 == 0 ? a : f.Dark());
                }
                using (var bar = RRect(-1.6f, -1.6f, 3.2f, 13.2f, 0.8f)) Metal(canvas, bar, f.Alt(), 1.6f);
                HRidges(canvas, -5, -11.4f, 3.2f, 5, Rubber, 0.55f);
                Seg(canvas, 0.2f, 2.8f, 4.6f, f, 0.6f);
                break;
            }

            case HiltStyle.Staff:
                Seg(canvas, 0, 3.6f, 6.4f, f, 0.8f);
                FillRRect(canvas, -2.2f, -1.0f, 4.4f, 1.2f, 0.3f, Rubber, 0.8);
                Seg(canvas, -3.6f, 26.2f, 5.4f, f);
                foreach (var y in new[] { -6.0f, -9.0f, -24.0f, -27.0f }) FillRRect(canvas, -2.85f, y, 5.7f, 0.9f, 0.3f, a, 0.9);
                HRidges(canvas, -11.6f, -21.4f, 5.4f, 9, Rubber, 0.6f);
                Dot(canvas, 1.4f, -7.5f, 0.55f, RGB.Hex(0xE0332B));
                Seg(canvas, -29.8f, 3.6f, 6.4f, f, 0.8f);
                FillRRect(canvas, -2.2f, -31.4f, 4.4f, 1.2f, 0.3f, Rubber, 0.8);
                break;

            case HiltStyle.Clawed:
            {
                using var left = new SKPath();
                left.MoveTo(-2.2f, -3.2f);
                left.CubicTo(-4.6f, -2.2f, -5.2f, 0.6f, -4.2f, 2.4f);
                left.CubicTo(-3.4f, 0.8f, -2.4f, -0.2f, -1.6f, -1.0f);
                left.Close();
                using var right = new SKPath(left);
                right.Transform(SKMatrix.CreateScale(-1, 1));
                Metal(canvas, left, f, 4.5f);
                Metal(canvas, right, f, 4.5f);
                Seg(canvas, 0, 3.4f, 4.2f, f, 0.6f);
                Seg(canvas, -3.4f, 6.2f, 5.6f, f);
                using (var gem = Poly((0, -4.4f), (1.5f, -6.5f), (0, -8.6f), (-1.5f, -6.5f))) Metal(canvas, gem, f.Alt(), 1.5f);
                Dot(canvas, 0, -6.5f, 0.5f, a);
                using var grip = RRect(-2.5f, -9.6f, 5.0f, 10);
                Metal(canvas, grip, f.Alt(), 2.5f);
                DiagWraps(canvas, -9.8f, -19.4f, 5.0f, 7, Rubber, grip);
                MetalPoly(canvas, f, (-2.8f, -19.6f), (2.8f, -19.6f), (2.1f, -22.8f), (0, -25), (-2.1f, -22.8f));
                FillRRect(canvas, -2.8f, -19.6f, 5.6f, 0.8f, 0.2f, a, 0.9);
                break;
            }
        }
    }

    // MARK: Click spark

    /// Draws the click spark centred on `center` (pixels) with `scale` pixels per unit.
    public static void DrawSpark(SKCanvas canvas, SKPoint center, double progress, RGB color, float scale)
    {
        float q = (float)Math.Clamp(progress, 0, 1);
        float fade = 1 - q;
        var hot = color.Mix(RGB.White, 0.55);
        using var path = new SKPath();
        for (int i = 0; i < 10; i++)
        {
            float ang = i * (MathF.PI / 5) + (Hash(i, 42) - 0.5f) * 0.5f;
            float r0 = (2 + 12 * q) * scale;
            float r1 = r0 + (7 * (1 - q) + 2 + 3 * Hash(i, 8)) * scale;
            path.MoveTo(center.X + MathF.Cos(ang) * r0, center.Y + MathF.Sin(ang) * r0);
            path.LineTo(center.X + MathF.Cos(ang) * r1, center.Y + MathF.Sin(ang) * r1);
        }
        float width = (1.4f * (1 - q) + 0.3f) * scale;
        using (var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = color.Sk(fade), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f * scale) })
            canvas.DrawPath(path, glow);
        using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = hot.Sk(fade) })
            canvas.DrawPath(path, line);
        float fr = 5.5f * scale * (1 - q);
        if (fr > 0.1f)
        {
            using var flash = new SKPaint { IsAntialias = true, Color = RGB.White.Sk(0.9 * fade) };
            canvas.DrawCircle(center, fr, flash);
        }
    }
}
