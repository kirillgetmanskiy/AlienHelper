using System.Drawing;

namespace AlienHelper;

/// <summary>
/// Visual layout of the Area-51 16 keyboard and its LED ids. LED ids follow the controller's 20-wide matrix
/// (id = row * 20 + column), taken from an Area-51 2025 map shared in T-Troll/alienfx-tools#588 and confirmed
/// row-by-row on this laptop. Individual ids can be corrected by the user (calibration) and are saved in settings.
/// </summary>
public static class KeyboardLayout
{
    /// <param name="Name">Stable identifier used in settings (labels repeat: Shift, Alt, Ctrl).</param>
    /// <param name="Name">Stable identifier used in settings (labels repeat: Shift, Alt, Win).</param>
    /// <param name="ExtraIds">More LEDs under wide keys; lit together with the main id.</param>
    public sealed record Key(int Index, string Name, string Label, int DefaultId, float X, float Y, float W, int Row, int[] ExtraIds)
    {
        public float H => Row == 0 ? 0.8f : 1f;
    }

    public const float Width = 16f;

    // wide keys light through several LEDs (found on this laptop: both Shifts are double)
    static readonly Dictionary<string, int[]> Extra = new()
    {
        ["Shift"] = new[] { 80, 81 },
        ["RShift"] = new[] { 93 },
        ["Backspace"] = new[] { 33, 34 },
        ["Enter"] = new[] { 73, 75 },
        ["Caps"] = new[] { 60 },
        ["Space"] = new[] { 105, 107, 108 },
        ["Copilot"] = new[] { 113 },
    };

    // after Extra: static fields initialise in declaration order
    public static readonly IReadOnlyList<Key> Keys = Build();

    static List<Key> Build()
    {
        var keys = new List<Key>();
        void Row(int row, float y, float x, params (string label, int id, float w)[] items)
        {
            foreach (var (label, id, w) in items)
            {
                string name = label.Length == 0 ? "Space" : label;
                if (keys.Any(k => k.Name == name)) name = "R" + name;   // right-hand twin
                keys.Add(new Key(keys.Count, name, label, id, x, y, w, row, Extra.GetValueOrDefault(name, Array.Empty<int>())));
                x += w;
            }
        }

        // Layout of the Area-51 16 (AA16250) taken from a photo of the keyboard: main block 15 units wide plus a
        // media column (mic mute, mute, vol−, vol+) on the right. Ids that differ from the 18" map were found on this
        // laptop: Esc/F1–F3 = 140–143, Backspace 35, Caps 61, left Shift 82, right Shift 94, Win 103, Copilot 112.
        float f = 15f / 16f;
        var r0 = new List<(string, int, float)> { ("Esc", 140, f), ("F1", 141, f), ("F2", 142, f), ("F3", 143, f) };
        for (int i = 4; i <= 12; i++) r0.Add(($"F{i}", i, f));
        r0.AddRange(new[] { ("Home", 13, f), ("End", 14, f), ("Del", 15, f) });
        Row(0, 0f, 0, r0.ToArray());

        Row(1, 0.9f, 0, ("`", 20, 1), ("1", 21, 1), ("2", 22, 1), ("3", 23, 1), ("4", 24, 1), ("5", 25, 1), ("6", 26, 1), ("7", 27, 1),
            ("8", 28, 1), ("9", 29, 1), ("0", 30, 1), ("-", 31, 1), ("=", 32, 1), ("Backspace", 35, 2));
        Row(2, 1.9f, 0, ("Tab", 40, 1.5f), ("Q", 42, 1), ("W", 43, 1), ("E", 44, 1), ("R", 45, 1), ("T", 46, 1), ("Y", 47, 1), ("U", 48, 1),
            ("I", 49, 1), ("O", 50, 1), ("P", 51, 1), ("[", 52, 1), ("]", 53, 1), ("\\", 55, 1.5f));
        Row(3, 2.9f, 0, ("Caps", 61, 1.75f), ("A", 62, 1), ("S", 63, 1), ("D", 64, 1), ("F", 65, 1), ("G", 66, 1), ("H", 67, 1), ("J", 68, 1),
            ("K", 69, 1), ("L", 70, 1), (";", 71, 1), ("'", 72, 1), ("Enter", 74, 2.25f));
        Row(4, 3.9f, 0, ("Shift", 82, 2.25f), ("Z", 83, 1), ("X", 84, 1), ("C", 85, 1), ("V", 86, 1), ("B", 87, 1), ("N", 88, 1), ("M", 89, 1),
            (",", 90, 1), (".", 91, 1), ("/", 92, 1), ("Shift", 94, 1.75f), ("↑", 114, 1));
        Row(5, 4.9f, 0, ("Ctrl", 100, 1.25f), ("Fn", 101, 1), ("Win", 103, 1), ("Alt", 104, 1.25f), ("", 106, 5.5f), ("Alt", 111, 1),
            ("Win", 109, 1), ("Copilot", 112, 1), ("←", 133, 1), ("↓", 134, 1), ("→", 135, 1));

        // media column; mic-mute id is a guess (19) — fix it with "Fix key mapping" if it stays dark
        Row(1, 0.9f, 15, ("Mic", 19, 1));
        Row(2, 1.9f, 15, ("Mute", 16, 1));
        Row(3, 2.9f, 15, ("Vol−", 18, 1));
        Row(4, 3.9f, 15, ("Vol+", 17, 1));
        return keys;
    }

    public static float Height => 5.9f;

    // ---------------- templates ----------------

    /// <param name="Id">Stable id (settings, hidden list, active animation).</param>
    /// <param name="Animate">Color of a key at time t (seconds) for animated templates; null = static.</param>
    public sealed record Template(string Id, string NameKey, Func<Key, Color> ColorOf, Func<Key, double, Color>? Animate = null)
    {
        public Template(string id, string nameKey, Func<Key, double, Color> animate) : this(id, nameKey, k => animate(k, 0), animate) { }
    }

    static float X(Key k) => k.X + k.W / 2;

    /// <summary>Flames rise from the bottom row and flicker.</summary>
    static Color Fire(Key k, double t)
    {
        float x = X(k), up = 1 - k.Row / 5f;          // 0 = bottom row, 1 = function row
        double flicker = 0.45 * Math.Sin(x * 1.9 + t * 7.3) + 0.35 * Math.Sin(x * 0.7 - t * 4.1 + k.Row * 1.3)
                       + 0.2 * Math.Sin(t * 11 + x * 3.1 + k.Row);
        double heat = Math.Clamp(1.05 - up * 0.95 + flicker * 0.3, 0, 1);
        int r = (int)(255 * Math.Clamp(heat * 1.7, 0, 1));
        int g = (int)(215 * Math.Clamp((heat - 0.35) * 1.6, 0, 1));
        int b = (int)(90 * Math.Clamp((heat - 0.88) * 6, 0, 1));
        return Color.FromArgb(r, g, b);
    }

    /// <summary>The spectrum flows across the keyboard from left to right, slightly tilted by row.</summary>
    static Color Rainbow(Key k, double t)
    {
        float x = X(k);
        float hue = (float)(x / Width * 0.85 + k.Row * 0.025 - t * 0.4);
        // a brightness wave with its own step and speed: some keys flare up, others sink into shadow
        double wave = 0.5 + 0.5 * Math.Sin(x * 0.75 + k.Row * 0.9 - t * 2.6);
        float v = (float)(0.18 + 0.82 * Math.Pow(wave, 1.6));
        return Hsv(hue, 1, v);
    }

    /// <summary>Waves roll from left to right with a little foam on the crests.</summary>
    static Color Ocean(Key k, double t)
    {
        float x = X(k);
        double phase = x * 0.55 + k.Row * 0.45 - t * 2.2;
        double wave = Math.Sin(phase);
        float hue = 0.52f + 0.07f * (float)Math.Sin(x * 0.3 + t * 0.7);
        var c = Hsv(hue, 1, (float)(0.45 + 0.55 * (wave * 0.5 + 0.5)));
        if (wave > 0.92) c = Color.FromArgb(Math.Min(255, c.R + 120), Math.Min(255, c.G + 90), Math.Min(255, c.B + 60));
        return c;
    }

    static readonly Color Off = Color.Black;
    static readonly Color Dim = Color.FromArgb(0, 22, 44);

    static Func<Key, Color> Labels(Color rest, params (Color color, string[] labels)[] groups) => k =>
    {
        foreach (var (color, labels) in groups)
            if (labels.Contains(k.Label)) return color;
        return rest;
    };

    static Color Hsv(float h, float s = 1, float v = 1)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)h; float f = h - i;
        float p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    static readonly Color Red = Color.FromArgb(255, 20, 20), Orange = Color.FromArgb(255, 110, 0), Yellow = Color.FromArgb(255, 210, 0);

    public static readonly IReadOnlyList<Template> Templates = new List<Template>
    {
        new("wasd", "tpl.wasd", Labels(Dim, (Red, new[] { "W", "A", "S", "D" }), (Orange, new[] { "Shift", "Ctrl", "" }))),
        new("fps", "tpl.fps", Labels(Dim,
            (Red, new[] { "W", "A", "S", "D" }),
            (Orange, new[] { "1", "2", "3", "4", "5", "Shift", "Ctrl", "", "Tab" }),
            (Yellow, new[] { "Q", "E", "R", "F", "G", "C", "V" }))),
        new("moba", "tpl.moba", Labels(Dim,
            (Color.FromArgb(0, 200, 255), new[] { "Q", "W", "E", "R" }),
            (Color.FromArgb(160, 60, 255), new[] { "D", "F" }),
            (Yellow, new[] { "1", "2", "3", "4", "5", "6", "B" }))),
        // pictures are drawn by key name so they follow the staggered rows exactly
        // kaomoji ^_^ : E-4-R and U-8-I are the "^" eyes, T-Y the "_" mouth
        new("cat", "tpl.cat", Labels(Off, (Orange, new[] { "E", "4", "R", "T", "Y", "U", "8", "I" }))),
        new("heart", "tpl.heart", Labels(Off,
            (Color.FromArgb(255, 0, 60), new[] { "3", "4", "7", "8", "W", "E", "R", "T", "Y", "U", "I", "D", "F", "G", "H", "J", "V", "B" }))),
        new("rainbow", "tpl.rainbow", (Func<Key, double, Color>)Rainbow),
        new("fire", "tpl.fire", (Func<Key, double, Color>)Fire),
        new("ocean", "tpl.ocean", (Func<Key, double, Color>)Ocean),
    };

    /// <summary>Colors of every lit key for a template (at time t for animated ones).</summary>
    public static Dictionary<int, Color> Render(Template t, double time = 0)
    {
        var d = new Dictionary<int, Color>();
        foreach (var k in Keys)
        {
            var c = t.Animate != null ? t.Animate(k, time) : t.ColorOf(k);
            if (c.R + c.G + c.B > 0) d[k.Index] = c;
        }
        return d;
    }

    public static Template? Find(string? id) => Templates.FirstOrDefault(t => t.Id == id);
}
