using Microsoft.Win32;

namespace LightsaberCursor;

/// Windows 11 keeps each tray icon's "show on taskbar" choice under HKCU\Control Panel\NotifyIconSettings,
/// keyed by an id with the icon's ExecutablePath. The entry appears once the icon has been shown.
internal static class TrayIconSetting
{
    const string Root = @"Control Panel\NotifyIconSettings";

    public static string? Find()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return null;
        using var root = Registry.CurrentUser.OpenSubKey(Root);
        if (root == null) return null;
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("ExecutablePath") is string path && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                return $@"{Root}\{name}";
        }
        return null;
    }

    public static bool IsPromoted(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue("IsPromoted") is int v && v == 1;
    }

    public static void Promote(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
    }
}
