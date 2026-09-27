namespace LightsaberCursor;

/// Swaps the arrow, text and link-hand pointers for a transparent cursor so only the saber is visible.
/// Every other pointer (resize, busy, app-defined cursors) stays native, and the saber steps aside for them.
internal static class SystemCursors
{
    static readonly uint[] Replaced = { Native.OCR_NORMAL, Native.OCR_IBEAM, Native.OCR_HAND };
    static readonly string Marker = Path.Combine(AppSettings.Folder, "cursors-replaced");
    static HashSet<IntPtr>? handles;
    public static bool IsApplied { get; private set; }

    /// Shared handles of the replaced system cursors (they keep their identity after SetSystemCursor).
    static HashSet<IntPtr> Handles => handles ??= Replaced
        .Select(id => Native.LoadCursor(IntPtr.Zero, (IntPtr)id))
        .Where(h => h != IntPtr.Zero)
        .ToHashSet();

    static IntPtr CreateBlank()
    {
        var and = Enumerable.Repeat((byte)0xFF, 32 * 32 / 8).ToArray();
        var xor = new byte[32 * 32 / 8];
        return Native.CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor);
    }

    public static void Apply()
    {
        if (IsApplied) return;
        _ = Handles;
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.WriteAllText(Marker, DateTime.Now.ToString("o"));
        }
        catch { }
        foreach (var id in Replaced)
        {
            var blank = CreateBlank();
            if (blank != IntPtr.Zero && !Native.SetSystemCursor(blank, id)) Native.DestroyCursor(blank);
        }
        IsApplied = true;
    }

    /// Reloads the user's cursor scheme from the registry, undoing every replacement.
    public static void Restore()
    {
        Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        IsApplied = false;
        try { File.Delete(Marker); } catch { }
    }

    /// If a previous run crashed while the pointer was blanked, put it back.
    public static void RecoverFromCrash()
    {
        if (File.Exists(Marker)) Restore();
    }

    /// True when one of the replaced pointers (arrow, text, link hand) is showing, i.e. the saber should draw.
    public static bool SaberCursorShowing()
    {
        var info = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info)) return true;
        if ((info.flags & Native.CURSOR_SHOWING) == 0) return false;
        return Handles.Contains(info.hCursor);
    }
}
