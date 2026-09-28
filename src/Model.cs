using SkiaSharp;

namespace LightsaberCursor;

public readonly record struct RGB(double R, double G, double B)
{
    public static RGB Hex(uint h) => new(((h >> 16) & 0xFF) / 255.0, ((h >> 8) & 0xFF) / 255.0, (h & 0xFF) / 255.0);
    public static readonly RGB White = new(1, 1, 1);
    public static readonly RGB Black = new(0, 0, 0);

    public RGB Mix(RGB o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);

    static byte B8(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    public SKColor Sk(double alpha = 1) => new(B8(R), B8(G), B8(B), B8(alpha));
    public Color ToColor() => Color.FromArgb(B8(R), B8(G), B8(B));
    public static RGB From(Color c) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0);
}

public enum Faction { Jedi, Sith, Grey }

public enum HiltStyle { Classic, Ribbed, Slim, Curved, Crossguard, Shoto, Jagged, Angular, Ornate, Banded, Worn, Inquisitor, Staff, Clawed, Darksaber, Ancient, Plasma }

public enum HiltFinish { Silver, Black, Gunmetal, Brass, Bronze, White }

public enum BladeStyle { Standard, Unstable, Darksaber, Sword, Plasma }

public enum RandomSide { Any, Jedi, Sith }

public static class ModelExtensions
{
    public static string DisplayName(this Faction f) => f switch
    {
        Faction.Jedi => "Jedi",
        Faction.Sith => "Sith & Dark Side",
        _ => "Grey & Other",
    };

    public static float Length(this HiltStyle h) => h switch
    {
        HiltStyle.Classic => 25.5f,
        HiltStyle.Ribbed => 25f,
        HiltStyle.Slim => 27f,
        HiltStyle.Curved => 25f,
        HiltStyle.Crossguard => 22f,
        HiltStyle.Shoto => 16f,
        HiltStyle.Jagged => 25f,
        HiltStyle.Angular => 23.2f,
        HiltStyle.Ornate => 24f,
        HiltStyle.Banded => 20.2f,
        HiltStyle.Worn => 23.6f,
        HiltStyle.Inquisitor => 16f,
        HiltStyle.Staff => 33.4f,
        HiltStyle.Clawed => 25f,
        HiltStyle.Ancient => 17.6f,
        HiltStyle.Plasma => 15.4f,
        _ => 22.2f,
    };

    public static RGB Base(this HiltFinish f) => f switch
    {
        HiltFinish.Silver => RGB.Hex(0xB8BCC4),
        HiltFinish.Black => RGB.Hex(0x2A2B2F),
        HiltFinish.Gunmetal => RGB.Hex(0x5B6068),
        HiltFinish.Brass => RGB.Hex(0xB8923A),
        HiltFinish.Bronze => RGB.Hex(0x9C6A3C),
        _ => RGB.Hex(0xE4E2DC),
    };

    public static RGB Light(this HiltFinish f) => f switch
    {
        HiltFinish.Silver => RGB.Hex(0xF4F6F9),
        HiltFinish.Black => RGB.Hex(0x74767D),
        HiltFinish.Gunmetal => RGB.Hex(0xA8AEB6),
        HiltFinish.Brass => RGB.Hex(0xF3D98F),
        HiltFinish.Bronze => RGB.Hex(0xDDAA78),
        _ => RGB.Hex(0xFFFFFF),
    };

    public static RGB Dark(this HiltFinish f) => f switch
    {
        HiltFinish.Silver => RGB.Hex(0x575C65),
        HiltFinish.Black => RGB.Hex(0x0C0C0E),
        HiltFinish.Gunmetal => RGB.Hex(0x272A2F),
        HiltFinish.Brass => RGB.Hex(0x664A14),
        HiltFinish.Bronze => RGB.Hex(0x4A2E17),
        _ => RGB.Hex(0x96938B),
    };

    /// Contrasting finish used for secondary hilt sections.
    public static HiltFinish Alt(this HiltFinish f) => f switch
    {
        HiltFinish.Silver => HiltFinish.Black,
        HiltFinish.Black => HiltFinish.Silver,
        HiltFinish.Gunmetal => HiltFinish.Silver,
        HiltFinish.Brass => HiltFinish.Black,
        HiltFinish.Bronze => HiltFinish.Black,
        _ => HiltFinish.Gunmetal,
    };
}

public sealed record SaberConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Faction Faction { get; set; }
    public HiltStyle Hilt { get; set; }
    public HiltFinish Finish { get; set; }
    public RGB Accent { get; set; }
    public RGB Blade { get; set; }
    public BladeStyle BladeStyle { get; set; }
    public double CoreWhiteness { get; set; } = 0.8;
    public double BladeLength { get; set; } = 1;
    public double Thickness { get; set; } = 1;
    public double GlowRadius { get; set; } = 1;
    public double GlowIntensity { get; set; } = 1;
    public bool Animated { get; set; }

    public SaberConfig Copy() => this with { };
}

public static class BladeColors
{
    public static readonly (string Name, RGB Color)[] Swatches =
    {
        ("Blue", RGB.Hex(0x3D8BFF)), ("Sky", RGB.Hex(0x4DC3FF)), ("Green", RGB.Hex(0x39FF4F)),
        ("Lime", RGB.Hex(0x9BFF2E)), ("Yellow", RGB.Hex(0xFFD93B)), ("Orange", RGB.Hex(0xFF8A1F)),
        ("Red", RGB.Hex(0xFF2323)), ("Crimson", RGB.Hex(0xD40A1E)), ("Magenta", RGB.Hex(0xFF2E9A)),
        ("Purple", RGB.Hex(0xB44DFF)), ("Cyan", RGB.Hex(0x2EF2F2)), ("White", RGB.Hex(0xEEF4FF)),
    };
    public static readonly RGB[] Jedi =
    {
        RGB.Hex(0x3D8BFF), RGB.Hex(0x4DA6FF), RGB.Hex(0x39FF4F), RGB.Hex(0x6CFF3A),
        RGB.Hex(0xB44DFF), RGB.Hex(0xFFD93B), RGB.Hex(0x2EF2F2), RGB.Hex(0xEEF4FF),
    };
    public static readonly RGB[] Sith = { RGB.Hex(0xFF2323), RGB.Hex(0xD40A1E), RGB.Hex(0xFF4A12), RGB.Hex(0xE00A4A) };
    public static readonly RGB[] Metals = { RGB.Hex(0xD9B04C), RGB.Hex(0xC9D1DA), RGB.Hex(0xB87333) };
    public static readonly RGB[] Grey = { RGB.Hex(0xFF8A1F), RGB.Hex(0xEEF4FF), RGB.Hex(0xFFD93B), RGB.Hex(0xFF2E9A) };
}

public static class Presets
{
    static SaberConfig P(string name, Faction faction, HiltStyle hilt, HiltFinish finish, uint accent, uint blade,
        BladeStyle style = BladeStyle.Standard, bool animated = false, double core = 0.8, double length = 1, double thickness = 1, double glow = 1)
    {
        var slug = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return new SaberConfig
        {
            Id = "preset." + slug, Name = name, Faction = faction, Hilt = hilt, Finish = finish,
            Accent = RGB.Hex(accent), Blade = RGB.Hex(blade), BladeStyle = style, CoreWhiteness = core,
            BladeLength = length, Thickness = thickness, GlowRadius = glow, Animated = animated,
        };
    }

    public static readonly SaberConfig ObiWan = P("Obi-Wan Kenobi", Faction.Jedi, HiltStyle.Slim, HiltFinish.Silver, 0x222222, 0x4DA6FF, animated: true);
    public static readonly SaberConfig Vader = P("Darth Vader", Faction.Sith, HiltStyle.Ribbed, HiltFinish.Black, 0xE0332B, 0xFF2323, animated: true);

    public static readonly IReadOnlyList<SaberConfig> All = new List<SaberConfig>
    {
        P("Anakin Skywalker", Faction.Jedi, HiltStyle.Classic, HiltFinish.Silver, 0xE0332B, 0x3D8BFF),
        ObiWan,
        P("Luke Skywalker (Return of the Jedi)", Faction.Jedi, HiltStyle.Slim, HiltFinish.Black, 0xC8CCD2, 0x3CFF52),
        P("Luke Skywalker (Empire Strikes Back)", Faction.Jedi, HiltStyle.Classic, HiltFinish.Silver, 0xE0332B, 0x3A86FF),
        P("Rey Skywalker", Faction.Jedi, HiltStyle.Banded, HiltFinish.Bronze, 0xC89B3C, 0xFFD93B, length: 0.9),
        P("Aayla Secura", Faction.Jedi, HiltStyle.Classic, HiltFinish.Gunmetal, 0x2F7BFF, 0x2F7BFF, animated: true),
        P("Ahsoka Tano (Clone Wars)", Faction.Jedi, HiltStyle.Ornate, HiltFinish.Silver, 0x1C1C1C, 0x7CFF3A, animated: true),
        P("Yoda", Faction.Jedi, HiltStyle.Shoto, HiltFinish.Silver, 0x1C1C1C, 0x5CFF5C, length: 0.72),
        P("Mace Windu", Faction.Jedi, HiltStyle.Ornate, HiltFinish.Brass, 0x2A2A2A, 0xB44DFF),
        P("Qui-Gon Jinn", Faction.Jedi, HiltStyle.Slim, HiltFinish.Black, 0xC8CCD2, 0x57FF2E),
        P("Kanan Jarrus", Faction.Jedi, HiltStyle.Slim, HiltFinish.Gunmetal, 0x8A6A40, 0x3F9BFF),
        P("Ezra Bridger", Faction.Jedi, HiltStyle.Classic, HiltFinish.Gunmetal, 0x2FAF6A, 0x4CFF6B),
        P("Kit Fisto", Faction.Jedi, HiltStyle.Slim, HiltFinish.Silver, 0x2FAF6A, 0x3CFF6A),
        P("Luminara Unduli", Faction.Jedi, HiltStyle.Curved, HiltFinish.Silver, 0x2F6F5A, 0x4CFF4C),
        P("Plo Koon", Faction.Jedi, HiltStyle.Classic, HiltFinish.Gunmetal, 0xB05A2A, 0x3D8BFF),
        P("Shaak Ti", Faction.Jedi, HiltStyle.Ornate, HiltFinish.Silver, 0xC04040, 0x3FA0FF),
        P("Ki-Adi-Mundi", Faction.Jedi, HiltStyle.Slim, HiltFinish.Gunmetal, 0x8A6A40, 0x3A7FFF),
        P("Cal Kestis (Blue)", Faction.Jedi, HiltStyle.Classic, HiltFinish.Gunmetal, 0xC06A20, 0x3D9BFF, animated: true),
        P("Cal Kestis (Orange)", Faction.Jedi, HiltStyle.Classic, HiltFinish.Gunmetal, 0xC06A20, 0xFF8A1F, animated: true),
        P("Leia Organa", Faction.Jedi, HiltStyle.Banded, HiltFinish.Silver, 0x2F7BFF, 0x4D9BFF),
        P("Ben Solo", Faction.Jedi, HiltStyle.Classic, HiltFinish.Silver, 0xE0332B, 0x3D8BFF),
        P("Blue Lightsaber", Faction.Jedi, HiltStyle.Classic, HiltFinish.Silver, 0x3D8BFF, 0x3D8BFF, animated: true),

        Vader,
        P("Darth Nihilus", Faction.Sith, HiltStyle.Jagged, HiltFinish.Black, 0xB0101A, 0xD40A1E, animated: true, core: 0.6),
        P("Kylo Ren", Faction.Sith, HiltStyle.Crossguard, HiltFinish.Gunmetal, 0x3A3A3A, 0xFF1A10, BladeStyle.Unstable, true, 0.65, thickness: 1.15),
        P("Count Dooku", Faction.Sith, HiltStyle.Curved, HiltFinish.Black, 0xD9B04C, 0xFF2A2A),
        P("Darth Sidious", Faction.Sith, HiltStyle.Ornate, HiltFinish.Silver, 0xD9B04C, 0xFF2323, animated: true),
        P("Marrok", Faction.Sith, HiltStyle.Worn, HiltFinish.Gunmetal, 0xB0201A, 0xFF3B1F, animated: true),
        P("Shin Hati", Faction.Sith, HiltStyle.Angular, HiltFinish.Black, 0xC04020, 0xFF5418),
        P("Darth Maul", Faction.Sith, HiltStyle.Staff, HiltFinish.Black, 0xC8CCD2, 0xFF1E1E, animated: true),
        P("Savage Opress", Faction.Sith, HiltStyle.Staff, HiltFinish.Gunmetal, 0x9C6A3C, 0xFF2A2A),
        P("Asajj Ventress", Faction.Sith, HiltStyle.Curved, HiltFinish.Gunmetal, 0xC8CCD2, 0xFF2A2A),
        P("Grand Inquisitor", Faction.Sith, HiltStyle.Inquisitor, HiltFinish.Black, 0xE0332B, 0xFF2323, animated: true),
        P("Second Sister", Faction.Sith, HiltStyle.Inquisitor, HiltFinish.Black, 0xC8CCD2, 0xFF2A2A, animated: true),
        P("Reva (Third Sister)", Faction.Sith, HiltStyle.Inquisitor, HiltFinish.Gunmetal, 0xB0201A, 0xFF1A10, animated: true),
        P("Starkiller", Faction.Sith, HiltStyle.Worn, HiltFinish.Gunmetal, 0xE0332B, 0xFF2323, animated: true),
        P("Darth Revan", Faction.Sith, HiltStyle.Clawed, HiltFinish.Black, 0x8A2BE2, 0xFF2323, animated: true),

        P("Ahsoka Tano (White)", Faction.Grey, HiltStyle.Ornate, HiltFinish.White, 0x2A2A2A, 0xEEF4FF, animated: true),
        P("Sabine Wren (Darksaber)", Faction.Grey, HiltStyle.Darksaber, HiltFinish.Silver, 0xC0302A, 0x0A0A0C, BladeStyle.Darksaber, true),
        P("Din Djarin (Darksaber)", Faction.Grey, HiltStyle.Darksaber, HiltFinish.Gunmetal, 0xC0302A, 0x0A0A0C, BladeStyle.Darksaber, true),
        P("Baylan Skoll", Faction.Grey, HiltStyle.Angular, HiltFinish.Gunmetal, 0xD9B04C, 0xFF7A18, thickness: 1.15),
        P("Mara Jade", Faction.Grey, HiltStyle.Slim, HiltFinish.Silver, 0xB02070, 0xFF2E9A),
        P("Revan (Jedi)", Faction.Grey, HiltStyle.Clawed, HiltFinish.Gunmetal, 0x6A4EB0, 0xA24DFF, animated: true),
        P("Agamemnon (Golden Sword)", Faction.Grey, HiltStyle.Ancient, HiltFinish.Brass, 0xF0CF6A, 0xD9B04C, BladeStyle.Sword, true, glow: 0.45),
        P("Plasma Sword", Faction.Grey, HiltStyle.Plasma, HiltFinish.Gunmetal, 0x6FD3FF, 0x3AA8FF, BladeStyle.Plasma, true, glow: 1.2),
        P("Starkiller (Redeemed)", Faction.Grey, HiltStyle.Worn, HiltFinish.Gunmetal, 0x2F7BFF, 0x3D8BFF, animated: true),
    };

    public static SaberConfig Default => ObiWan.Copy();
}

public static class Randomizer
{
    static readonly Random Rng = new();
    static T Pick<T>(IReadOnlyList<T> list) => list[Rng.Next(list.Count)];
    static double Between(double a, double b) => a + Rng.NextDouble() * (b - a);

    public static SaberConfig Make(SaberConfig from, bool hilt, bool color, RandomSide side)
    {
        var c = from.Copy();
        double roll = Rng.NextDouble();
        Faction faction = side switch
        {
            RandomSide.Jedi => Faction.Jedi,
            RandomSide.Sith => Faction.Sith,
            _ => roll < 0.4 ? Faction.Sith : roll < 0.52 ? Faction.Grey : Faction.Jedi,
        };
        if (color)
        {
            var palette = faction == Faction.Sith ? BladeColors.Sith : faction == Faction.Grey ? BladeColors.Grey : BladeColors.Jedi;
            c.Blade = Pick(palette).Mix(Pick(palette), Between(0, 0.25));
            c.BladeStyle = BladeStyle.Standard;
            if (faction == Faction.Sith && Rng.NextDouble() < 0.22) c.BladeStyle = BladeStyle.Unstable;
            if (Rng.NextDouble() < (faction == Faction.Grey ? 0.3 : 0.12))
            {
                c.BladeStyle = BladeStyle.Darksaber;
                c.Blade = RGB.Hex(0x0A0A0C);
            }
            else if (Rng.NextDouble() < 0.08)
            {
                c.BladeStyle = BladeStyle.Sword;
                c.Blade = Pick(BladeColors.Metals);
            }
            else if (Rng.NextDouble() < 0.08)
            {
                c.BladeStyle = BladeStyle.Plasma;
            }
            c.CoreWhiteness = Between(0.55, 0.9);
            c.GlowRadius = Between(0.75, 1.3);
            c.GlowIntensity = Between(0.85, 1.2);
            c.Animated = Rng.Next(2) == 0;
        }
        if (hilt)
        {
            HiltStyle[] sith = { HiltStyle.Ribbed, HiltStyle.Jagged, HiltStyle.Curved, HiltStyle.Crossguard, HiltStyle.Angular, HiltStyle.Worn, HiltStyle.Ornate, HiltStyle.Inquisitor, HiltStyle.Staff, HiltStyle.Clawed, HiltStyle.Darksaber, HiltStyle.Ancient, HiltStyle.Plasma };
            HiltStyle[] jedi = { HiltStyle.Classic, HiltStyle.Slim, HiltStyle.Shoto, HiltStyle.Ornate, HiltStyle.Banded, HiltStyle.Worn, HiltStyle.Angular, HiltStyle.Curved, HiltStyle.Clawed, HiltStyle.Darksaber, HiltStyle.Ancient, HiltStyle.Plasma };
            c.Hilt = Pick(faction == Faction.Sith ? sith : jedi);
            HiltFinish[] finishes = faction == Faction.Sith
                ? new[] { HiltFinish.Black, HiltFinish.Black, HiltFinish.Gunmetal, HiltFinish.Silver, HiltFinish.Brass }
                : Enum.GetValues<HiltFinish>();
            c.Finish = Pick(finishes);
            c.Accent = Pick(new[] { RGB.Hex(0xE0332B), RGB.Hex(0xD9B04C), RGB.Hex(0x1C1C1C), RGB.Hex(0xC8CCD2), RGB.Hex(0x2F7BFF), RGB.Hex(0x8A6A40) });
        }
        c.BladeLength = 1;
        c.Thickness = 1;
        if (hilt || color) c.Faction = faction;
        c.Name = Name(c.Faction);
        c.Id = "random." + Guid.NewGuid();
        return c;
    }

    public static string Name(Faction f)
    {
        string[] a = { "Vor", "Kal", "Nyx", "Mal", "Sar", "Zan", "Tyr", "Dre", "Vex", "Mor", "Bael", "Xal" };
        string[] b = { "ius", "ax", "ora", "eth", "is", "ok", "anna", "yr", "os", "ul" };
        string[] first = { "Kel", "Aria", "Tavi", "Oren", "Lysa", "Bren", "Mira", "Dax", "Sela", "Jorin", "Vela", "Cade" };
        string[] last = { "Tavos", "Raan", "Kestar", "Ollin", "Venn", "Sarno", "Dray", "Ithor", "Marek", "Solen" };
        return f switch
        {
            Faction.Sith => "Darth " + Pick(a) + Pick(b),
            Faction.Jedi => "Jedi " + Pick(first) + " " + Pick(last),
            _ => Pick(first) + " the Grey",
        };
    }
}
