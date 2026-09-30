namespace LightsaberCursor;

/// Start, Search, Notification Center, Quick Settings, Alt+Tab and the lock screen live in Windows' system
/// z-order bands, above every app's always-on-top windows, so nothing can be drawn over them. The UAC and
/// Ctrl+Alt+Del screens are a separate secure desktop. Over those the normal Windows pointer is shown instead.
internal static class SystemLayers
{
    const uint ZBID_DESKTOP = 1; // the band ordinary and always-on-top windows share
    static bool bandApi = true;

    static uint Band(IntPtr hwnd)
    {
        if (!bandApi) return ZBID_DESKTOP;
        try
        {
            return Native.GetWindowBand(hwnd, out var band) ? band : ZBID_DESKTOP;
        }
        catch (EntryPointNotFoundException)
        {
            bandApi = false;
            return ZBID_DESKTOP;
        }
    }

    static bool Cloaked(IntPtr hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// True when the window under the pointer is in a system band the overlay can't be drawn above.
    public static bool Covers(Native.POINT p)
    {
        var root = Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT);
        return root != IntPtr.Zero && Band(root) > ZBID_DESKTOP;
    }

    /// True while the secure desktop (UAC, Ctrl+Alt+Del, sign-in) has the input; other apps can't open it.
    public static bool SecureDesktopActive()
    {
        var desktop = Native.OpenInputDesktop(0, false, Native.DESKTOP_READOBJECTS);
        if (desktop == IntPtr.Zero) return true;
        Native.CloseDesktop(desktop);
        return false;
    }

    /// True when a visible window in the overlay's own band sits above it, e.g. a menu or popup opened after it.
    public static bool AnythingAbove(IntPtr overlay)
    {
        int n = 0;
        for (var h = Native.GetWindow(overlay, Native.GW_HWNDPREV); h != IntPtr.Zero && n < 512; h = Native.GetWindow(h, Native.GW_HWNDPREV), n++)
        {
            if (Native.IsWindowVisible(h) && Band(h) <= ZBID_DESKTOP && !Cloaked(h)) return true;
        }
        return false;
    }
}
