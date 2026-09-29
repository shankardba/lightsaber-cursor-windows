using SkiaSharp;

namespace LightsaberCursor;

/// Click-through, always-on-top, per-pixel-alpha window that shows whatever is rendered into it.
/// It resizes and moves every frame to just cover the saber, spark and trail.
internal sealed class Overlay : Form
{
    IntPtr memDC;
    IntPtr dib;
    IntPtr oldBitmap;
    IntPtr bits;
    int capW, capH;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-10, -10, 1, 1);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                          | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    void EnsureCapacity(int w, int h)
    {
        if (w <= capW && h <= capH && dib != IntPtr.Zero) return;
        FreeBuffer();
        capW = Math.Max(w, capW);
        capH = Math.Max(h, capH);
        var screen = Native.GetDC(IntPtr.Zero);
        memDC = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = capW,
            biHeight = -capH,
            biPlanes = 1,
            biBitCount = 32,
        };
        dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldBitmap = Native.SelectObject(memDC, dib);
    }

    void FreeBuffer()
    {
        if (memDC != IntPtr.Zero)
        {
            Native.SelectObject(memDC, oldBitmap);
            Native.DeleteDC(memDC);
            memDC = IntPtr.Zero;
        }
        if (dib != IntPtr.Zero)
        {
            Native.DeleteObject(dib);
            dib = IntPtr.Zero;
        }
    }

    /// Renders via `draw` into a w×h buffer and places it at screen pixel (x, y).
    public void Present(int x, int y, int w, int h, Action<SKCanvas> draw)
    {
        if (w <= 0 || h <= 0) return;
        EnsureCapacity(w, h);
        var info = new SKImageInfo(capW, capH, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surface = SKSurface.Create(info, bits, capW * 4))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.ClipRect(new SKRect(0, 0, w, h));
            draw(canvas);
            canvas.Flush();
        }
        var dst = new Native.POINT { X = x, Y = y };
        var size = new Native.SIZE { CX = w, CY = h };
        var src = new Native.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
    }

    /// Raised on any mouse movement or button while <see cref="ListenForMouse"/> is on, even when another app has focus.
    public event Action? MouseInput;

    /// Raw mouse input (no hook, no permission) lets a resting frame loop wake the moment the mouse is used.
    public void ListenForMouse(bool on)
    {
        var device = new Native.RAWINPUTDEVICE
        {
            UsagePage = 0x01, // generic desktop
            Usage = 0x02, // mouse
            Flags = on ? Native.RIDEV_INPUTSINK : Native.RIDEV_REMOVE,
            Target = on ? Handle : IntPtr.Zero,
        };
        Native.RegisterRawInputDevices(new[] { device }, 1, (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_INPUT) MouseInput?.Invoke();
        base.WndProc(ref m);
    }

    public void KeepOnTop() =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    protected override void Dispose(bool disposing)
    {
        FreeBuffer();
        base.Dispose(disposing);
    }
}
