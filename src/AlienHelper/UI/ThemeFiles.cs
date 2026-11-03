using System.Drawing;
using System.Drawing.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlienHelper.UI;

/// <summary>
/// Themes anyone can make: JSON files in the "themes" folder next to the settings. A file names its colors on top of a
/// built-in look ("base"), so it may list only the ones it changes. Format (all fields but "colors" optional):
/// <code>
/// {
///   "format": 1,
///   "name": "My theme",                    // or { "en": "My theme", "ru": "Моя тема" }
///   "author": "…",
///   "base": "dark",                        // dark or light: the title bar, and the colors the file leaves out
///   "corners": "round",                    // or "square"
///   "font": "Consolas",                    // any installed font; Segoe UI when left out or missing
///   "tint": "#3CFF7A",                     // recolors every chart and overlay row to this hue (one-color themes)
///   "overlay": true,                       // the game overlay uses the theme too (dark themes only)
///   "colors": { "background": "#0E1014", "card": "…", "cardHover": "…", "border": "…", "track": "…", "text": "…",
///               "muted": "…", "accent": "…", "hot": "…", "good": "…", "warning": "…", "extra": "…" }
/// }
/// </code>
/// A broken file never breaks the app: it is skipped, and the reason goes to the log and to the import message.
/// </summary>
public static class ThemeFiles
{
    public const string Prefix = "file:";
    const int MaxBytes = 64 * 1024;

    public static string Dir => Path.Combine(Log.Dir, "themes");

    public sealed record Entry(string Name, string Title);

    static readonly string[] ColorKeys =
        { "background", "card", "cardHover", "border", "track", "text", "muted", "accent", "hot", "good", "warning", "extra" };

    static readonly Regex HexColor = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");

    /// <summary>The readable theme files, by title. Broken ones are left out (and logged once per scan).</summary>
    public static List<Entry> List()
    {
        var list = new List<Entry>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                var d = Load(name, out var error);
                if (d != null) list.Add(new Entry(name, d.Title));
                else Log.Warn($"theme file '{Path.GetFileName(f)}': {error}");
            }
        }
        catch (Exception ex) { Log.Warn("theme files: " + ex.Message); }
        return list.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static Theme.Def? Load(string name, out string? error)
    {
        error = null;
        try
        {
            var path = Path.Combine(Dir, name + ".json");
            if (!File.Exists(path)) { error = "the file is gone"; return null; }
            if (new FileInfo(path).Length > MaxBytes) { error = "the file is too large"; return null; }
            return Parse(File.ReadAllText(path), name, out error, null);
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>Reads a theme; null and <paramref name="error"/> when it cannot be used. Notes on what was ignored or
    /// is hard to read go to <paramref name="warnings"/>.</summary>
    public static Theme.Def? Parse(string json, string name, out string? error, List<string>? warnings)
    {
        error = null;
        warnings ??= new();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { error = $"not valid JSON (line {ex.LineNumber + 1})"; return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "the file must hold one { … } object"; return null; }
            if (root.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.Number && fmt.GetInt32() > 1)
                warnings.Add($"made for a newer version of the app (format {fmt.GetInt32()}): some of it may be ignored");

            var look = Str(root, "base")?.ToLowerInvariant() switch
            {
                null or "dark" => Theme.Look.Dark,
                "light" => Theme.Look.Light,
                var other => Bad(warnings, $"base '{other}' is unknown, dark is used", Theme.Look.Dark),
            };
            var p = Theme.BuiltIn(look);
            if (!root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
            { error = "\"colors\" is missing"; return null; }

            var c = new Dictionary<string, Color>();
            foreach (var prop in colors.EnumerateObject())
            {
                string? key = ColorKeys.FirstOrDefault(k => k.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));
                if (key == null) { warnings.Add($"color '{prop.Name}' is unknown and ignored"); continue; }
                if (ParseColor(prop.Value) is { } col) c[key] = col;
                else warnings.Add($"color '{prop.Name}' must look like #RRGGBB; ignored");
            }
            Color Get(string k, Color fallback) => c.TryGetValue(k, out var v) ? v : fallback;
            var bg = Get("background", p.Bg);
            var card = Get("card", p.Card);
            // the hover shade and the track follow the card when the file leaves them out
            var pal = new Theme.Palette(bg, card, Get("cardHover", c.ContainsKey("card") ? Theme.Blend(card, Get("text", p.Fg), 0.06f) : p.CardHi),
                Get("border", c.ContainsKey("card") ? Theme.Blend(card, Get("text", p.Fg), 0.12f) : p.Border),
                Get("track", c.ContainsKey("card") ? Theme.Blend(card, Get("text", p.Fg), 0.14f) : p.Track),
                Get("text", p.Fg), Get("muted", p.Muted), Get("accent", p.Accent), Get("hot", p.Warm), Get("good", p.Good),
                Get("warning", p.Warn), Get("extra", p.Extra));

            bool square = Str(root, "corners")?.ToLowerInvariant() == "square";
            string? font = Str(root, "font");
            if (font != null && !Installed(font)) { warnings.Add($"font '{font}' is not installed; Segoe UI is used"); font = null; }
            Color? tint = null;
            if (root.TryGetProperty("tint", out var t))
            {
                tint = ParseColor(t);
                if (tint == null) warnings.Add("tint must look like #RRGGBB; ignored");
            }
            bool overlay = root.TryGetProperty("overlay", out var ov) && ov.ValueKind == JsonValueKind.True;
            if (overlay && look == Theme.Look.Light) { warnings.Add("the overlay stays dark with a light theme"); overlay = false; }

            if (Contrast(pal.Fg, pal.Card) < 4.5) warnings.Add($"text is hard to read on the cards (contrast {Contrast(pal.Fg, pal.Card):0.0}:1, 4.5 or more is best)");
            if (Contrast(pal.Muted, pal.Card) < 2.5) warnings.Add($"secondary text is hard to read (contrast {Contrast(pal.Muted, pal.Card):0.0}:1)");

            return new Theme.Def(Prefix + name, look, pal, square, font, tint, overlay) { Title = Title(root, name) };
        }
    }

    static T Bad<T>(List<string> warnings, string text, T value) { warnings.Add(text); return value; }

    static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s.Trim() : null;

    static Color? ParseColor(JsonElement v) =>
        v.ValueKind == JsonValueKind.String && v.GetString() is { } s && HexColor.IsMatch(s.Trim()) ? ColorTranslator.FromHtml(s.Trim()) : null;

    /// <summary>"name" as text, or by language code; the file name when there is none.</summary>
    static string Title(JsonElement root, string fallback)
    {
        if (!root.TryGetProperty("name", out var n)) return fallback;
        if (n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s) return Cut(s.Trim());
        if (n.ValueKind != JsonValueKind.Object) return fallback;
        foreach (var code in new[] { L.Current, "en" })
            if (n.TryGetProperty(code, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } t) return Cut(t.Trim());
        foreach (var p in n.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } t) return Cut(t.Trim());
        return fallback;
    }

    static string Cut(string s) => s.Length <= 40 ? s : s[..40];

    static HashSet<string>? _fonts;
    static bool Installed(string family)
    {
        if (_fonts == null)
        {
            using var fc = new InstalledFontCollection();
            _fonts = new HashSet<string>(fc.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        }
        return _fonts.Contains(family);
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(int v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Copies a theme file into the themes folder after checking it. Returns its settings key ("file:name"), or null;
    /// <paramref name="message"/> says what was wrong or what to look at.
    /// </summary>
    public static string? Import(string path, out string message)
    {
        message = "";
        try
        {
            if (new FileInfo(path).Length > MaxBytes) { message = "the file is too large"; return null; }
            var json = File.ReadAllText(path);
            string name = Safe(Path.GetFileNameWithoutExtension(path));
            var warnings = new List<string>();
            if (Parse(json, name, out var error, warnings) == null) { message = error ?? "?"; return null; }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, name + ".json"), json);
            message = string.Join("\n", warnings.Select(w => "• " + w));
            Log.Info($"theme imported: {name}" + (warnings.Count > 0 ? $" ({string.Join("; ", warnings)})" : ""));
            return Prefix + name;
        }
        catch (Exception ex) { message = ex.Message; return null; }
    }

    static string Safe(string name)
    {
        var s = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ' ? ch : '_').ToArray()).Trim();
        return s.Length == 0 ? "theme" : s.Length > 60 ? s[..60] : s;
    }

    /// <summary>Writes the current theme as a file: a ready template to change and import.</summary>
    public static void Export(string path)
    {
        var d = Theme.Current;
        string H(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var p = d.P;
        var colors = new Dictionary<string, string>
        {
            ["background"] = H(p.Bg), ["card"] = H(p.Card), ["cardHover"] = H(p.CardHi), ["border"] = H(p.Border),
            ["track"] = H(p.Track), ["text"] = H(p.Fg), ["muted"] = H(p.Muted), ["accent"] = H(p.Accent),
            ["hot"] = H(p.Warm), ["good"] = H(p.Good), ["warning"] = H(p.Warn), ["extra"] = H(p.Extra),
        };
        var o = new Dictionary<string, object?>
        {
            ["format"] = 1,
            ["name"] = d.IsFile ? d.Title : "My theme",
            ["author"] = "",
            ["base"] = d.Base == Theme.Look.Light ? "light" : "dark",
            ["corners"] = d.SquareCorners ? "square" : "round",
        };
        if (d.FontFamily != null) o["font"] = d.FontFamily;
        if (d.Tint is { } t) o["tint"] = H(t);
        if (d.Overlay) o["overlay"] = true;
        o["colors"] = colors;
        File.WriteAllText(path, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
