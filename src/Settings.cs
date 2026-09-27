using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace LightsaberCursor;

public enum AfterDarkMode { SystemTheme, Hours }

public sealed class AppRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// Process name without ".exe", compared case-insensitively.
    public string ExeName { get; set; } = "";
    public string AppName { get; set; } = "";
    public SaberConfig Saber { get; set; } = Presets.Default;
}

/// Missing keys in the saved file keep these defaults, so new options never reset existing settings.
public sealed class Prefs
{
    public bool Enabled { get; set; } = true;
    public SaberConfig Saber { get; set; } = Presets.Default;
    public List<SaberConfig> CustomSabers { get; set; } = new();
    public double Scale { get; set; } = 0.8;

    public bool RetractWhenIdle { get; set; } = true;
    public double IdleSeconds { get; set; } = 4;
    public bool ClickSpark { get; set; } = true;
    public bool MotionTrail { get; set; } = true;

    public bool AfterDark { get; set; }
    public AfterDarkMode AfterDarkMode { get; set; } = AfterDarkMode.SystemTheme;
    public int NightStart { get; set; } = 19;
    public int NightEnd { get; set; } = 7;
    public SaberConfig NightSaber { get; set; } = Presets.Vader.Copy();

    public bool PerApp { get; set; }
    public List<AppRule> AppRules { get; set; } = new();

    public bool RandomHilt { get; set; } = true;
    public bool RandomColor { get; set; } = true;
    public RandomSide RandomSide { get; set; } = RandomSide.Any;
    public bool RandomOnIgnite { get; set; }
}

public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightsaberCursor");
    static string FilePath => Path.Combine(Folder, "settings.json");

    public Prefs Prefs { get; private set; } = new();

    /// Raised after any change is saved.
    public event Action? Changed;

    public AppSettings()
    {
        try
        {
            if (File.Exists(FilePath))
                Prefs = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath), Json) ?? new Prefs();
        }
        catch
        {
            Prefs = new Prefs();
        }
    }

    /// Apply a change and persist it.
    public void Update(Action<Prefs> change)
    {
        change(Prefs);
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Prefs, Json));
        }
        catch
        {
            // A failed save must never take the cursor down with it.
        }
        Changed?.Invoke();
    }

    public IEnumerable<SaberConfig> AllSabers => Presets.All.Concat(Prefs.CustomSabers);

    public void Randomize() => Update(p =>
        p.Saber = Randomizer.Make(p.Saber, p.RandomHilt, p.RandomColor, p.RandomSide));

    public void SaveCustom(string name) => Update(p =>
    {
        var c = p.Saber.Copy();
        if (!string.IsNullOrWhiteSpace(name)) c.Name = name.Trim();
        c.Id = "custom." + Guid.NewGuid();
        p.CustomSabers.Add(c);
        p.Saber = c;
    });

    public void UpdateCustom() => Update(p =>
    {
        int i = p.CustomSabers.FindIndex(s => s.Id == p.Saber.Id);
        if (i >= 0) p.CustomSabers[i] = p.Saber.Copy();
    });

    public void DeleteCustom(string id) => Update(p => p.CustomSabers.RemoveAll(s => s.Id == id));

    public bool IsNight()
    {
        if (Prefs.AfterDarkMode == AfterDarkMode.SystemTheme)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        int h = DateTime.Now.Hour, s = Prefs.NightStart, e = Prefs.NightEnd;
        if (s == e) return false;
        return s > e ? (h >= s || h < e) : (h >= s && h < e);
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("LightsaberCursor") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("LightsaberCursor", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("LightsaberCursor", false);
        }
    }
}
