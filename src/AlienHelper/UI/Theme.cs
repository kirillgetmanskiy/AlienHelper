using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AlienHelper.UI;

public static class Theme
{
    /// <summary>The app's looks; chosen in Settings, applied before windows are (re)created.</summary>
    public enum Look { Dark, Light, Strict }

    sealed record Palette(Color Bg, Color Card, Color CardHi, Color Border, Color Track, Color Fg, Color Muted,
        Color Accent, Color Warm, Color Good, Color Warn);

    static readonly Dictionary<Look, Palette> Palettes = new()
    {
        [Look.Dark] = new(Hex("#0E1014"), Hex("#171A20"), Hex("#1F232B"), Hex("#262B34"), Hex("#2A303B"),
            Hex("#E8EAED"), Hex("#8A93A3"), Hex("#1FB6FF"), Hex("#FF6A3D"), Hex("#3DDC97"), Hex("#FFC14D")),
        // same layout on light surfaces; accents darkened enough to read on white
        [Look.Light] = new(Hex("#EEF0F3"), Hex("#FFFFFF"), Hex("#F2F4F7"), Hex("#D8DDE4"), Hex("#D3D9E1"),
            Hex("#1A1D22"), Hex("#66707E"), Hex("#0A84E0"), Hex("#E2542A"), Hex("#139A62"), Hex("#C98A00")),
        // plain graphite, square corners, flat fills, quieter accent
        [Look.Strict] = new(Hex("#161616"), Hex("#1F1F1F"), Hex("#2A2A2A"), Hex("#383838"), Hex("#3C3C3C"),
            Hex("#ECECEC"), Hex("#9A9A9A"), Hex("#5AA0E6"), Hex("#E2683C"), Hex("#4CC38A"), Hex("#E0B040")),
    };

    public static Look Current { get; private set; } = Look.Dark;
    public static Color Bg { get; private set; }
    public static Color Card { get; private set; }
    public static Color CardHi { get; private set; }
    public static Color Border { get; private set; }
    public static Color Track { get; private set; }
    public static Color Fg { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Accent { get; private set; }
    public static Color Warm { get; private set; }
    public static Color Good { get; private set; }
    public static Color Warn { get; private set; }

    /// <summary>Main window shows current values only: no sparklines, compact tiles (Settings → Main window).</summary>
    public static bool NumbersOnly { get; set; }

    static Theme() { Set(Look.Dark); }

    static void Set(Look look)
    {
        var p = Palettes[look];
        Current = look;
        (Bg, Card, CardHi, Border, Track, Fg, Muted, Accent, Warm, Good, Warn) =
            (p.Bg, p.Card, p.CardHi, p.Border, p.Track, p.Fg, p.Muted, p.Accent, p.Warm, p.Good, p.Warn);
    }

    /// <summary>Applies a look by its settings name ("dark", "light", "strict").</summary>
    public static void Apply(string? name) => Set(name switch { "light" => Look.Light, "strict" => Look.Strict, _ => Look.Dark });

    public static string Name(Look l) => l.ToString().ToLowerInvariant();

    public static bool IsDark => Current != Look.Light;
    /// <summary>Slider and switch knobs: light text color on dark looks, white on the light one.</summary>
    public static Color Knob => IsDark ? Fg : Color.White;
    /// <summary>Strict look: square corners and flat fills.</summary>
    public static bool Square => Current == Look.Strict;

    /// <summary>
    /// Paints with another look for a moment (UI thread only). The game overlay and the mode hint always use the dark
    /// look: they sit over games, where a light panel would glare.
    /// </summary>
    public static IDisposable Use(Look look)
    {
        var was = Current;
        Set(look);
        return new Restore(() => Set(was));
    }

    sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    /// <summary>A dot (knob, swatch, legend marker): round, or a small square in the strict look.</summary>
    public static void FillDot(Graphics g, Brush b, RectangleF r)
    {
        if (!Square) { g.FillEllipse(b, r); return; }
        using var p = Round(r, Sf(1.5f));
        g.FillPath(b, p);
    }

    public static void DrawDot(Graphics g, Pen pen, RectangleF r)
    {
        if (!Square) { g.DrawEllipse(pen, r); return; }
        using var p = Round(r, Sf(1.5f));
        g.DrawPath(pen, p);
    }

    /// <summary>Fill under a graph line: a fading gradient, or flat in the strict look.</summary>
    public static Brush AreaBrush(RectangleF bounds, Color c, int alpha) => Square
        ? new SolidBrush(Color.FromArgb(alpha / 2, c))
        : new LinearGradientBrush(bounds, Color.FromArgb(alpha, c), Color.FromArgb(0, c), 90f);

    public static float Scale { get; set; } = 1f;
    public static int S(int v) => (int)Math.Round(v * Scale);
    public static float Sf(float v) => v * Scale;

    public static Font Font(float pt, FontStyle st = FontStyle.Regular) => new("Segoe UI", pt, st);
    public static Font Semibold(float pt) => new("Segoe UI Semibold", pt);

    public static Color Hex(string h) => ColorTranslator.FromHtml(h);

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>Cool → warm color for a temperature reading.</summary>
    public static Color TempColor(int t) => t switch
    {
        < 0 => Muted,
        < 60 => Good,
        < 80 => Warn,
        _ => Warm,
    };

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        if (Square) radius = Math.Min(radius, Sf(2));   // strict look: crisp, nearly square corners
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Windows 11 title bar matching the window background (dark or light, round or square corners).</summary>
    public static void DarkTitleBar(Form f)
    {
        int on = IsDark ? 1 : 0;
        DwmSetWindowAttribute(f.Handle, 20, ref on, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
        int color = Bg.R | (Bg.G << 8) | (Bg.B << 16);
        DwmSetWindowAttribute(f.Handle, 35, ref color, 4);           // DWMWA_CAPTION_COLOR
        int text = Fg.R | (Fg.G << 8) | (Fg.B << 16);
        DwmSetWindowAttribute(f.Handle, 36, ref text, 4);            // DWMWA_TEXT_COLOR
        int round = Square ? 1 : 2;
        DwmSetWindowAttribute(f.Handle, 33, ref round, 4);           // DWMWA_WINDOW_CORNER_PREFERENCE: 1 square, 2 round
    }

    /// <summary>The embedded app icon (Assetspp.ico) at the requested size.</summary>
    public static Icon AppIcon(Size size)
    {
        using var s = typeof(Theme).Assembly.GetManifestResourceStream("AlienHelper.app.ico")!;
        return new Icon(s, size);
    }

    public static void Style(Form f)
    {
        f.Icon = AppIcon(SystemInformation.SmallIconSize);
        f.BackColor = Bg;
        f.ForeColor = Fg;
        f.Font = Font(9.5f);
        f.HandleCreated += (_, _) => DarkTitleBar(f);
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Fg : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            Smooth(g);
            var r = e.ImageRectangle;
            float d = Math.Min(r.Width, r.Height) * 0.45f;
            using var b = new SolidBrush(Accent);
            g.FillEllipse(b, r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Muted;
            base.OnRenderArrow(e);
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Card;
            public override Color ImageMarginGradientBegin => Card;
            public override Color ImageMarginGradientMiddle => Card;
            public override Color ImageMarginGradientEnd => Card;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => CardHi;
            public override Color MenuItemSelected => CardHi;
            public override Color MenuItemSelectedGradientBegin => CardHi;
            public override Color MenuItemSelectedGradientEnd => CardHi;
            public override Color MenuItemPressedGradientBegin => CardHi;
            public override Color MenuItemPressedGradientEnd => CardHi;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Card;
            public override Color CheckBackground => Card;
            public override Color CheckSelectedBackground => CardHi;
            public override Color CheckPressedBackground => CardHi;
        }
    }
}
