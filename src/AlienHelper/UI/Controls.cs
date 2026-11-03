using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using static AlienHelper.UI.Theme;

namespace AlienHelper.UI;

/// <summary>Base for owner-drawn controls: double buffered, transparent to the parent's color.</summary>
public abstract class DrawnControl : Control
{
    protected bool Hover { get; private set; }

    protected DrawnControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        ForeColor = Fg;
    }

    protected override void OnParentBackColorChanged(EventArgs e) { BackColor = Parent!.BackColor; base.OnParentBackColorChanged(e); }
    protected override void OnParentChanged(EventArgs e) { if (Parent != null) BackColor = Parent.BackColor; base.OnParentChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Smooth(e.Graphics);
        Draw(e.Graphics);
    }

    protected abstract void Draw(Graphics g);
}

/// <summary>Rounded card with an optional caption.</summary>
public sealed class Card : Panel
{
    public string Caption { get; set; } = "";
    public string? Hint { get; set; }
    public int ContentTop => Caption.Length > 0 ? S(40) : S(14);

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        ForeColor = Fg;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Bg);
        Smooth(g);
        using var path = Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Sf(12));
        using var fill = new SolidBrush(Theme.Card);
        using var pen = new Pen(Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        if (Caption.Length > 0)
        {
            using var f = Semibold(10.5f);
            TextRenderer.DrawText(g, Caption, f, new Point(S(16), S(12)), Fg);
            if (Hint != null)
            {
                using var hf = Theme.Font(8.5f);
                var sz = TextRenderer.MeasureText(Hint, hf);
                TextRenderer.DrawText(g, Hint, hf, new Point(Width - S(16) - sz.Width, S(14)), Muted);
            }
        }
    }
}

public sealed class PillButton : DrawnControl
{
    bool _active;
    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    public Color AccentColor { get; set; } = Accent;
    public bool Subtle { get; set; }
    /// <summary>Draws an icon in the given square (with the text color) instead of the text.</summary>
    public Action<Graphics, RectangleF, Color>? Glyph { get; set; }

    public PillButton(string text) { Text = text; Cursor = Cursors.Hand; Height = S(34); }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Round(r, Sf(9));
        Color fill = Active ? Blend(Theme.Card, AccentColor, 0.22f) : Hover ? CardHi : Subtle ? Theme.Card : Blend(Theme.Card, CardHi, 0.5f);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(Active ? AccentColor : Hover ? Track : Border, Active ? Sf(1.4f) : 1f)) g.DrawPath(p, path);
        Color fg = Active || Hover ? Fg : Blend(Muted, Fg, 0.6f);
        if (Glyph != null)
        {
            float side = Math.Min(Width, Height) * 0.56f;
            Glyph(g, new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side), fg);
            return;
        }
        using var f = Active ? Semibold(9.5f) : Theme.Font(9.5f);
        TextRenderer.DrawText(g, Text, f, Rectangle.Round(r), !Enabled ? Blend(Theme.Card, Muted, 0.6f) : Active ? Fg : Blend(Muted, Fg, 0.6f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

public sealed class Slider : DrawnControl
{
    int _value;
    bool _drag;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public Color AccentColor { get; set; } = Accent;
    public int Value { get => _value; set { _value = Math.Clamp(value, Minimum, Maximum); Invalidate(); } }
    /// <summary>True while the user is dragging the thumb.</summary>
    public bool Dragging => _drag;
    /// <summary>Fires continuously while dragging.</summary>
    public event EventHandler? ValueChanged;
    /// <summary>Fires once when the user releases the thumb or uses the keyboard.</summary>
    public event EventHandler? ValueCommitted;

    public Slider() { Height = S(28); Cursor = Cursors.Hand; TabStop = true; }

    float Pad => Sf(10);
    float X(int v) => Pad + (Width - 2 * Pad) * (v - Minimum) / Math.Max(1, Maximum - Minimum);

    protected override void Draw(Graphics g)
    {
        float cy = Height / 2f, h = Sf(5);
        using (var p = Round(new RectangleF(Pad, cy - h / 2, Width - 2 * Pad, h), h / 2))
        using (var b = new SolidBrush(Track)) g.FillPath(b, p);
        float x = X(Value);
        var accent = Enabled ? AccentColor : Blend(Track, Muted, 0.6f);   // locked (e.g. driven by a fan curve)
        if (x - Pad > 1)
            using (var p = Round(new RectangleF(Pad, cy - h / 2, x - Pad, h), h / 2))
            using (var b = new SolidBrush(accent)) g.FillPath(b, p);
        float r = Sf(_drag || Hover ? 8.5f : 7.5f);
        var knob = new RectangleF(x - r, cy - r, 2 * r, 2 * r);
        using (var b = new SolidBrush(Knob)) FillDot(g, b, knob);
        if (!IsDark) using (var p = new Pen(Border, Sf(1f))) DrawDot(g, p, knob);
        using (var b = new SolidBrush(accent)) FillDot(g, b, new RectangleF(x - r / 2.4f, cy - r / 2.4f, r / 1.2f, r / 1.2f));
    }

    void SetFromMouse(int mx)
    {
        float t = (mx - Pad) / Math.Max(1f, Width - 2 * Pad);
        int v = (int)Math.Round(Minimum + t * (Maximum - Minimum));
        v = Math.Clamp(v, Minimum, Maximum);
        if (v != _value) { Value = v; ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Focus(); SetFromMouse(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromMouse(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag) { _drag = false; Invalidate(); ValueCommitted?.Invoke(this, EventArgs.Empty); }
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Math.Max(1, (Maximum - Minimum) / 20);
        int v = e.KeyCode switch
        {
            Keys.Left or Keys.Down => Value - step,
            Keys.Right or Keys.Up => Value + step,
            _ => Value,
        };
        if (v != Value) { Value = v; ValueChanged?.Invoke(this, EventArgs.Empty); ValueCommitted?.Invoke(this, EventArgs.Empty); }
        base.OnKeyDown(e);
    }
}

public sealed class Swatch : DrawnControl
{
    bool _selected;
    public Color Color { get; set; }
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    public bool IsCustom { get; set; }

    public Swatch(Color c) { Color = c; Size = new Size(S(26), S(26)); Cursor = Cursors.Hand; }

    protected override void Draw(Graphics g)
    {
        float pad = Sf(3);
        var r = new RectangleF(pad, pad, Width - 2 * pad - 1, Height - 2 * pad - 1);
        if (IsCustom)
        {
            using var br = new LinearGradientBrush(r, Color.Red, Color.Blue, 45f)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta },
                    Positions = new[] { 0f, .2f, .4f, .6f, .8f, 1f },
                }
            };
            FillDot(g, br, r);
        }
        else
            using (var b = new SolidBrush(Color)) FillDot(g, b, r);
        if (!IsDark) using (var p = new Pen(Border, Sf(1f))) DrawDot(g, p, r);   // a white swatch on a white card
        if (Selected || Hover)
            using (var p = new Pen(Selected ? Fg : Muted, Sf(1.6f)))
                DrawDot(g, p, new RectangleF(0.8f, 0.8f, Width - 2.6f, Height - 2.6f));
    }
}

public sealed class Toggle : DrawnControl
{
    bool _on;
    public bool On { get => _on; set { _on = value; Invalidate(); } }
    public event EventHandler? Toggled;

    public Toggle(string text) { Text = text; Height = S(28); Cursor = Cursors.Hand; }

    protected override void Draw(Graphics g)
    {
        float w = Sf(38), h = Sf(20), y = (Height - h) / 2;
        var r = new RectangleF(Width - w - 1, y, w, h);
        // disabled: dimmed track and knob, muted text
        var track = !Enabled ? Blend(Theme.Card, Track, 0.6f) : On ? Accent : Track;
        using (var p = Round(r, h / 2))
        using (var b = new SolidBrush(track)) g.FillPath(b, p);
        float d = h - Sf(6);
        float x = On && Enabled ? r.Right - d - Sf(3) : r.X + Sf(3);
        using (var b = new SolidBrush(!Enabled ? Muted : On ? Knob : KnobOff)) FillDot(g, b, new RectangleF(x, y + Sf(3), d, d));
        using var f = Theme.Font(9.5f);
        TextRenderer.DrawText(g, Text, f, new Rectangle(0, 0, (int)(r.X - S(8)), Height), !Enabled ? Blend(Theme.Card, Muted, 0.7f) : Hover ? Fg : Blend(Muted, Fg, 0.6f),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    protected override void OnClick(EventArgs e) { if (!Enabled) return; On = !On; Toggled?.Invoke(this, EventArgs.Empty); base.OnClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Big temperature number with a sparkline of recent history.</summary>
public sealed class TempTile : DrawnControl
{
    public string Title { get; set; } = "";
    public int Value { get; set; } = -1;
    public IReadOnlyList<float> History { get; set; } = Array.Empty<float>();

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(r, Sf(10)))
        using (var b = new SolidBrush(Blend(Theme.Card, CardHi, 0.6f))) g.FillPath(b, p);

        var c = TempColor(Value);
        if (History.Count > 1)
        {
            float top = Height * 0.45f, bottom = Height - Sf(6);
            float lo = 30, hi = 100;
            var pts = new PointF[History.Count];
            for (int i = 0; i < History.Count; i++)
            {
                float x = Sf(6) + (Width - Sf(12)) * i / (History.Count - 1);
                float v = Math.Clamp(History[i], lo, hi);
                pts[i] = new PointF(x, bottom - (bottom - top) * (v - lo) / (hi - lo));
            }
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, bottom));
            area.AddLine(new PointF(pts[^1].X, bottom), new PointF(pts[0].X, bottom));
            area.CloseFigure();
            using (var b = AreaBrush(new RectangleF(0, top - 1, Width, bottom - top + 2), c, 70))
                g.FillPath(b, area);
            using var pen = new Pen(c, Sf(1.6f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }

        using var tf = Theme.Font(9f);
        TextRenderer.DrawText(g, Title, tf, new Point(S(12), S(10)), Muted);
        using var vf = Semibold(22f);
        string txt = Value < 0 ? "—" : $"{Value}°";
        TextRenderer.DrawText(g, txt, vf, new Point(S(9), S(26)), c);
    }
}

/// <summary>Fan name, rpm and a fill bar against max rpm.</summary>
public sealed class FanBar : DrawnControl
{
    public string Title { get; set; } = "";
    public int Rpm { get; set; } = -1;
    public int MaxRpm { get; set; }

    /// <summary>Samples that fill the width (the history window); fewer samples start from the right.</summary>
    public int Capacity { get; set; }

    /// <summary>Recent rpm, drawn as a faint area chart behind the text.</summary>
    public IReadOnlyList<float> History { get; set; } = Array.Empty<float>();

    public FanBar() { Height = S(28); }

    protected override void Draw(Graphics g)
    {
        if (!NumbersOnly && History.Count > 1 && MaxRpm > 0)
        {
            float bottom = Height - Sf(9), top = Sf(1);
            int cap = Math.Max(2, Capacity > 0 ? Capacity : History.Count);
            var pts = LineChart.Points(History, Width - 1, (Width - 1f) / (cap - 1), Width - 1, 0,
                v => bottom - (bottom - top) * Math.Clamp(v / MaxRpm, 0, 1));
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, bottom));
            area.AddLine(new PointF(pts[^1].X, bottom), new PointF(pts[0].X, bottom));
            area.CloseFigure();
            // fill only: a line through the middle of the text would read like a strikethrough
            using var b = new SolidBrush(Color.FromArgb(34, Accent));
            g.FillPath(b, area);
        }
        using var f = Theme.Font(9f);
        using var vf = Semibold(9.5f);
        TextRenderer.DrawText(g, Title, f, new Point(0, 0), Muted);
        string rpm = Rpm < 0 ? "—" : $"{Rpm:N0} {L.T("u.rpm")}".Replace(' ', ' ');
        var sz = TextRenderer.MeasureText(rpm, vf);
        TextRenderer.DrawText(g, rpm, vf, new Point(Width - sz.Width, 0), Fg);

        float y = Height - Sf(7), h = Sf(4);
        using (var p = Round(new RectangleF(0, y, Width - 1, h), h / 2))
        using (var b = new SolidBrush(Track)) g.FillPath(b, p);
        if (Rpm > 0 && MaxRpm > 0)
        {
            float t = Math.Clamp(Rpm / (float)MaxRpm, 0, 1);
            var c = Blend(Accent, Warm, Math.Clamp((t - 0.5f) * 2, 0, 1));
            using var p = Round(new RectangleF(0, y, Math.Max(h, (Width - 1) * t), h), h / 2);
            using var b = new SolidBrush(c);
            g.FillPath(b, p);
        }
    }
}

public sealed class Series
{
    public string Name { get; init; } = "";
    public Color Color { get; init; }
    public string Unit { get; init; } = "";
    public List<float> Values { get; } = new();
}

/// <summary>Multi-series line chart with grid, Y labels and a legend with current values.</summary>
public sealed class LineChart : DrawnControl
{
    public string Title { get; set; } = "";
    public List<Series> Series { get; } = new();
    public float? FixedMin { get; set; }
    public float? FixedMax { get; set; }
    public int Capacity { get; set; } = 150;
    /// <summary>Time the full width covers (for the "last N min" caption).</summary>
    public int SpanSeconds { get; set; } = 300;
    /// <summary>Fraction of a sample period since the newest sample (0..1): the history slides left by that much.</summary>
    public float Phase { get; set; }

    /// <summary>
    /// Screen points for a history, newest at the right edge. Long histories are averaged down to about one point per
    /// pixel column (a 0.1 s refresh keeps 3000 samples for 5 min); with <paramref name="phase"/> > 0 everything slides
    /// left by that part of a step and the newest value is held out to the right edge, so the chart glides.
    /// </summary>
    internal static PointF[] Points(IReadOnlyList<float> values, float right, float step, float width, float phase, Func<float, float> y)
    {
        int n = values.Count;
        // samples per point: fixed by the spacing (not by how much history there is yet), so the graph never stretches
        // or squeezes while it fills; buckets are aligned to the global sample count, so a finished bucket keeps its
        // value and only the newest one changes as samples arrive
        int stride = Math.Max(1, (int)Math.Ceiling(1f / Math.Max(0.01f, step)));
        int first = (int)(AlienHelper.SensorMonitor.SampleNo % stride);
        if (first == 0) first = stride;
        var pts = new List<PointF>(n / stride + 3);
        for (int end = n, size = first; end > 0; end -= size, size = stride)
        {
            int start = Math.Max(0, end - size);
            float sum = 0;
            for (int i = start; i < end; i++) sum += values[i];
            pts.Add(new PointF(right - step * (n - end) - step * phase, y(sum / (end - start))));
        }
        pts.Reverse();
        if (phase > 0 && n > 0) pts.Add(new PointF(right, pts[^1].Y));
        // a line needs two points: a lone first bucket is drawn as a short flat segment
        if (pts.Count == 1) pts.Insert(0, new PointF(pts[0].X - Math.Max(1f, step), pts[0].Y));
        return pts.ToArray();
    }
    public string AxisFormat { get; set; } = "0";

    protected override void Draw(Graphics g)
    {
        var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(card, Sf(12)))
        using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
        using (var p = Round(card, Sf(12)))
        using (var pen = new Pen(Border)) g.DrawPath(pen, p);

        using var tf = Semibold(10.5f);
        TextRenderer.DrawText(g, Title, tf, new Point(S(16), S(12)), Fg);

        // legend, right aligned beside the title; with many series (several GPUs or drives) it wraps onto
        // more lines below the title and the plot moves down
        using var lf = Theme.Font(9f);
        int titleRight = S(16) + TextRenderer.MeasureText(Title, tf).Width + S(24);
        var lines = new List<List<(Series s, string label, Size sz)>> { new() };
        int used = 0;
        foreach (var s in Series)
        {
            string label = s.Values.Count > 0 ? $"{s.Name}  {s.Values[^1].ToString(AxisFormat)}{s.Unit}" : s.Name;
            var sz = TextRenderer.MeasureText(label, lf);
            int w = sz.Width + S(26), room = Width - S(16) - (lines.Count == 1 ? titleRight : S(16));
            if (used > 0 && used + w > room) { lines.Add(new()); used = 0; }
            lines[^1].Add((s, label, sz));
            used += w;
        }
        int lineH = S(20);
        for (int li = 0; li < lines.Count; li++)
        {
            int lx = Width - S(16), ly = S(14) + li * lineH;
            for (int i = lines[li].Count - 1; i >= 0; i--)
            {
                var (s, label, sz) = lines[li][i];
                lx -= sz.Width;
                TextRenderer.DrawText(g, label, lf, new Point(lx, ly), Fg);
                using var b = new SolidBrush(s.Color);
                FillDot(g, b, new RectangleF(lx - S(12), ly + sz.Height / 2f - Sf(4), Sf(8), Sf(8)));
                lx -= S(26);
            }
        }
        int extra = (lines.Count - 1) * lineH;

        var plot = new RectangleF(S(48), S(44) + extra, Width - S(64), Height - S(64) - extra);
        if (plot.Width < 10 || plot.Height < 10) return;

        var all = Series.SelectMany(s => s.Values).ToList();
        float min = FixedMin ?? (all.Count > 0 ? all.Min() : 0);
        float max = FixedMax ?? (all.Count > 0 ? all.Max() : 1);
        if (FixedMin == null) min = (float)Math.Floor(min / 10) * 10;
        if (FixedMax == null) max = (float)Math.Ceiling(max / 10) * 10;
        if (max - min < 1) max = min + 10;

        using var grid = new Pen(Color.FromArgb(40, Muted)) { DashStyle = DashStyle.Dot };
        using var af = Theme.Font(8f);
        // fewer grid lines on a short plot, so the axis labels never run into each other
        int steps = plot.Height >= S(110) ? 4 : plot.Height >= S(50) ? 2 : 1;
        for (int i = 0; i <= steps; i++)
        {
            float y = plot.Bottom - plot.Height * i / steps;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            string v = (min + (max - min) * i / steps).ToString(AxisFormat);
            var sz = TextRenderer.MeasureText(v, af);
            TextRenderer.DrawText(g, v, af, new Point((int)plot.Left - sz.Width - S(4), (int)(y - sz.Height / 2f)), Muted);
        }
        string span = L.T("ch.last", SpanSeconds / 60);
        TextRenderer.DrawText(g, span, af, new Point((int)plot.Left, (int)plot.Bottom + S(4)), Muted);

        var clip = g.Clip;
        g.SetClip(new RectangleF(plot.X - 2, plot.Y - 4, plot.Width + 4, plot.Height + 8));
        foreach (var s in Series)
        {
            if (s.Values.Count < 2) continue;
            float Y(float v) => plot.Bottom - plot.Height * (Math.Clamp(v, min, max) - min) / (max - min);
            var pts = Points(s.Values, plot.Right, plot.Width / (Capacity - 1), plot.Width, Phase, Y);
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, plot.Bottom));
            area.AddLine(new PointF(pts[^1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom));
            area.CloseFigure();
            using (var b = AreaBrush(new RectangleF(plot.X, plot.Y - 1, plot.Width, plot.Height + 2), s.Color, 55))
                g.FillPath(b, area);
            using var pen = new Pen(s.Color, Sf(2f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
        g.Clip = clip;
    }
}

/// <summary>Small per-core tile: load area chart + current load and clock.</summary>
public sealed class CoreTile : DrawnControl
{
    public string Title { get; set; } = "";
    public bool Performance { get; set; }
    public float Load { get; set; }
    public float Mhz { get; set; }
    public List<float> History { get; } = new();
    public int Capacity { get; set; } = 60;
    /// <summary>Core temperature, °C (-1: unknown), and its history drawn as a line over the load graph (30…105 °C).</summary>
    public int Temp { get; set; } = -1;
    public List<float> Temps { get; } = new();

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(r, Sf(10)))
        using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
        using (var p = Round(r, Sf(10)))
        using (var pen = new Pen(Border)) g.DrawPath(pen, p);

        var c = Performance ? Accent : Extra;
        float top = S(40), bottom = Height - S(8), left = S(8), right = Width - S(8);
        using (var gp = new Pen(Color.FromArgb(30, Muted)))
            for (int i = 0; i <= 2; i++) { float y = bottom - (bottom - top) * i / 2; g.DrawLine(gp, left, y, right, y); }

        if (History.Count > 1)
        {
            var pts = LineChart.Points(History, right, (right - left) / (Capacity - 1), right - left, 0,
                v => bottom - (bottom - top) * Math.Clamp(v, 0, 100) / 100f);
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, bottom));
            area.AddLine(new PointF(pts[^1].X, bottom), new PointF(pts[0].X, bottom));
            area.CloseFigure();
            using (var b = new SolidBrush(Color.FromArgb(60, c))) g.FillPath(b, area);
            using var pen = new Pen(c, Sf(1.4f));
            g.DrawLines(pen, pts);
        }
        if (Temps.Count > 1)
        {
            var tp = LineChart.Points(Temps, right, (right - left) / (Capacity - 1), right - left, 0,
                v => bottom - (bottom - top) * (Math.Clamp(v, 30, 105) - 30) / 75f);
            using var tpen = new Pen(Color.FromArgb(220, Warm), Sf(1.3f)) { LineJoin = LineJoin.Round };
            g.DrawLines(tpen, tp);
        }

        using var tf = Theme.Font(8.5f);
        using var vf = Semibold(11f);
        TextRenderer.DrawText(g, Title, tf, new Point(S(10), S(6)), Muted);
        if (Temp > 0)
        {
            // temperature on the title line, right aligned, colored like the other temperatures
            using var hf = Semibold(9f);
            string t = $"{Temp}°";
            var ts = TextRenderer.MeasureText(t, hf);
            TextRenderer.DrawText(g, t, hf, new Point(Width - S(10) - ts.Width, S(5)), TempColor(Temp));
        }
        string load = $"{Load:0}%";
        TextRenderer.DrawText(g, load, vf, new Point(S(9), S(19)), Fg);
        string mhz = Mhz > 0 ? $"{Mhz / 1000:0.0} {L.T("u.ghz")}" : "";
        var sz = TextRenderer.MeasureText(mhz, tf);
        TextRenderer.DrawText(g, mhz, tf, new Point(Width - S(10) - sz.Width, S(22)), Muted);
    }
}

/// <summary>Compact metric tile: caption, big value, secondary line and an optional sparkline.</summary>
public sealed class StatTile : DrawnControl
{
    public string Title { get; set; } = "";
    public string Value { get; set; } = "—";
    public Color ValueColor { get; set; } = Fg;
    public string Sub { get; set; } = "";
    public float BigSize { get; set; } = 18f;
    public IReadOnlyList<float> History { get; set; } = Array.Empty<float>();
    public float HistoryMin { get; set; } = 30;
    public float HistoryMax { get; set; } = 100;
    /// <summary>Samples that fill the graph's width (the history window): a short history starts from the right instead of
    /// being stretched, and the graph keeps its scale while it fills.</summary>
    public int Capacity { get; set; }
    int Cap => Math.Max(2, Capacity > 0 ? Capacity : History.Count);

    /// <summary>Small tiles: history as a faint full-width fill behind the text instead of a corner sparkline.</summary>
    public bool HistoryBehind { get; set; }
    /// <summary>Extra lines drawn over the main sparkline, each scaled 0..its own max (e.g. watts, clock).</summary>
    public List<(IReadOnlyList<float> Values, Color Color, string Label)> Lines { get; } = new();

    /// <summary>Numbers-only mode: title, value and subtitle, no graphs; the subtitle sits beside the value when it fits.</summary>
    void DrawNumbers(Graphics g)
    {
        using var tf = Theme.Font(8.5f);
        using var vf = Semibold(BigSize);
        TextRenderer.DrawText(g, Title, tf, new Point(S(9), S(5)), Muted);
        var vsz = TextRenderer.MeasureText(Value, vf);
        TextRenderer.DrawText(g, Value, vf, new Point(S(6), S(17)), ValueColor);
        if (Sub.Length == 0) return;
        var ssz = TextRenderer.MeasureText(Sub, tf);
        var sub = Blend(Muted, Fg, 0.35f);
        if (S(6) + vsz.Width + S(6) + ssz.Width <= Width - S(8))
            TextRenderer.DrawText(g, Sub, tf, new Point(S(6) + vsz.Width + S(4), S(17) + vsz.Height - ssz.Height - S(3)), sub);
        else
        {
            // under the value, but never past the tile's bottom edge (narrow RAM / VRAM tiles)
            int y = Math.Min(S(17) + vsz.Height - S(3), Height - ssz.Height - S(6));
            TextRenderer.DrawText(g, Sub, tf, new Rectangle(new Point(S(9), y), new Size(Width - S(15), ssz.Height)), sub,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(r, Sf(9)))
        using (var b = new SolidBrush(Blend(Theme.Card, CardHi, 0.6f))) g.FillPath(b, p);
        if (NumbersOnly) { DrawNumbers(g); return; }

        // sparkline lives in the top-right corner, clear of the value and subtitle
        float left = Width * 0.48f, right = Width - Sf(8), top = Lines.Count > 0 ? Sf(19) : Sf(10), bottom = Height * 0.6f;
        if (HistoryBehind && History.Count > 1)
        {
            // narrow tiles have no free corner: a soft fill along the bottom, under the text
            float l = Sf(1), rt = Width - Sf(1), bt = Height - Sf(1), tp = Height * 0.3f;
            var hp = LineChart.Points(History, rt, (rt - l) / (Cap - 1), rt - l, 0,
                v => bt - (bt - tp) * (Math.Clamp(v, HistoryMin, HistoryMax) - HistoryMin) / (HistoryMax - HistoryMin));
            using var area = new GraphicsPath();
            area.AddLines(hp);
            area.AddLine(hp[^1], new PointF(rt, bt));
            area.AddLine(new PointF(rt, bt), new PointF(hp[0].X, bt));
            area.CloseFigure();
            var save = g.Clip;
            using (var clip = Round(r, Sf(9))) g.SetClip(clip);
            using (var b = new SolidBrush(Color.FromArgb(34, ValueColor))) g.FillPath(b, area);
            g.Clip = save;
        }
        if (Lines.Count > 0)
        {
            // tiny legend: ● ° ● W ● GHz, right aligned above the graph
            using var lf = Theme.Font(7f);
            var items = new List<(string, Color)> { ("°", ValueColor) };
            items.AddRange(Lines.Select(l => (l.Label, l.Color)));
            int x = (int)right;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var (text, color) = items[i];
                var sz = TextRenderer.MeasureText(text, lf);
                x -= sz.Width;
                TextRenderer.DrawText(g, text, lf, new Point(x, S(4)), Muted);
                using var b = new SolidBrush(color);
                FillDot(g, b, new RectangleF(x - Sf(7), S(4) + sz.Height / 2f - Sf(2.5f), Sf(5), Sf(5)));
                x -= S(12);
            }
        }

        if (History.Count > 1 && !HistoryBehind)
        {
            var pts = LineChart.Points(History, right, (right - left) / (Cap - 1), right - left, 0,
                v => bottom - (bottom - top) * (Math.Clamp(v, HistoryMin, HistoryMax) - HistoryMin) / (HistoryMax - HistoryMin));
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, bottom));
            area.AddLine(new PointF(pts[^1].X, bottom), new PointF(pts[0].X, bottom));
            area.CloseFigure();
            using (var b = AreaBrush(new RectangleF(left, top - 1, right - left, bottom - top + 2), ValueColor, 55))
                g.FillPath(b, area);
            using var pen = new Pen(Color.FromArgb(200, ValueColor), Sf(1.4f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }

        foreach (var (values, color, _) in Lines)
        {
            if (values.Count < 2) continue;
            float max = Math.Max(0.001f, values.Max() * 1.15f);
            var pts = LineChart.Points(values, right, (right - left) / (Cap - 1), right - left, 0,
                v => bottom - (bottom - top) * Math.Clamp(v, 0, max) / max);
            using var pen = new Pen(Color.FromArgb(210, color), Sf(1.2f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }

        using var tf = Theme.Font(8.5f);
        TextRenderer.DrawText(g, Title, tf, new Point(S(9), S(6)), Muted);
        using var vf = Semibold(BigSize);
        var vsz = TextRenderer.MeasureText(Value, vf);
        TextRenderer.DrawText(g, Value, vf, new Point(S(6), S(19)), ValueColor);
        if (Sub.Length > 0)
        {
            var pos = new Point(S(9), S(17) + vsz.Height - S(2));
            TextRenderer.DrawText(g, Sub, tf, new Rectangle(pos, new Size(Width - pos.X - S(6), S(18))), Blend(Muted, Fg, 0.35f),
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>
/// Hotkey picker: click, then press a combination. Backspace/Delete clears, Esc cancels.
/// A combination needs Ctrl, Alt or Shift (except F13–F24) so it cannot swallow ordinary typing.
/// </summary>
public sealed class HotkeyBox : DrawnControl
{
    int _value;
    bool _capturing;
    public int Value { get => _value; set { _value = value; Invalidate(); } }
    public string EmptyText { get; set; } = "—";
    public string PromptText { get; set; } = "…";
    public event EventHandler? ValueChanged;

    public HotkeyBox() { Height = S(28); Cursor = Cursors.Hand; TabStop = true; }

    protected override void OnClick(EventArgs e) { _capturing = true; Focus(); Invalidate(); base.OnClick(e); }
    protected override void OnLostFocus(EventArgs e) { _capturing = false; Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => _capturing || base.IsInputKey(keyData);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_capturing) return base.ProcessCmdKey(ref msg, keyData);
        var key = keyData & Keys.KeyCode;
        bool hasMod = (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
        switch (key)
        {
            case Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin:
                return true;                                   // wait for the real key
            case Keys.Escape when !hasMod:
                _capturing = false; Invalidate(); return true;
            case Keys.Back or Keys.Delete when !hasMod:
                Commit(0); return true;
        }
        if (hasMod || key is >= Keys.F13 and <= Keys.F24) Commit((int)keyData);
        return true;
    }

    void Commit(int v)
    {
        _capturing = false;
        Value = v;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(r, Sf(8)))
        {
            using var b = new SolidBrush(_capturing ? Blend(Theme.Card, Accent, 0.18f) : Hover ? CardHi : Blend(Theme.Card, CardHi, 0.5f));
            g.FillPath(b, p);
            using var pen = new Pen(_capturing ? Accent : Border, _capturing ? Sf(1.4f) : 1f);
            g.DrawPath(pen, p);
        }
        string text = _capturing ? PromptText : _value == 0 ? EmptyText : AlienHelper.HotKey.Describe(_value);
        using var f = _capturing || _value == 0 ? Theme.Font(9f) : Semibold(9.5f);
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), _capturing || _value == 0 ? Muted : Fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Small line-art icons for <see cref="PillButton.Glyph"/>.</summary>
public static class Glyphs
{
    /// <summary>Charts: two axes and a rising line ending in a dot.</summary>
    public static void Chart(Graphics g, RectangleF r, Color fg)
    {
        using (var axis = new Pen(fg, Sf(1.3f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(axis, new[] { new PointF(r.Left, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) });
        PointF P(float x, float y) => new(r.Left + r.Width * x, r.Top + r.Height * y);
        var pts = new[] { P(0.2f, 0.72f), P(0.45f, 0.42f), P(0.65f, 0.6f), P(0.92f, 0.18f) };
        using (var line = new Pen(Accent, Sf(1.7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(line, pts);
        float d = Sf(4f);
        using var dot = new SolidBrush(Accent);
        FillDot(g, dot, new RectangleF(pts[^1].X - d / 2, pts[^1].Y - d / 2, d, d));
    }
}

/// <summary>
/// A scrolling area without a system scrollbar: the wheel scrolls smoothly, and a thin thumb in the right margin shows
/// where you are (faint at rest, brighter while scrolling or under the mouse; it can be dragged).
/// </summary>
public sealed class SmoothScroll : Panel, IMessageFilter
{
    /// <summary>Put the scrolled controls here and size it to their full height, then call <see cref="Relayout"/>.</summary>
    public Panel Content { get; } = new();
    readonly Thumb _thumb;
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    float _pos, _target;
    long _litUntil;

    int Max => Math.Max(0, Content.Height - Height);

    public SmoothScroll()
    {
        BackColor = Content.BackColor = Bg;
        Controls.Add(Content);
        _thumb = new Thumb(this);
        Controls.Add(_thumb);
        _thumb.BringToFront();
        _anim.Tick += (_, _) => Step();
        Application.AddMessageFilter(this);
        Disposed += (_, _) => { Application.RemoveMessageFilter(this); _anim.Dispose(); };
    }

    protected override void OnParentBackColorChanged(EventArgs e) { BackColor = Content.BackColor = Parent!.BackColor; base.OnParentBackColorChanged(e); }

    /// <summary>After the content or the view changed size: keeps the position in range and places the thumb.</summary>
    public void Relayout()
    {
        _target = Math.Clamp(_target, 0, Max);
        _pos = Math.Clamp(_pos, 0, Max);
        Content.Width = Width;
        Content.Top = -(int)Math.Round(_pos);
        PlaceThumb();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    void ScrollTo(float target, bool instant = false)
    {
        _target = Math.Clamp(target, 0, Max);
        if (instant) _pos = _target;
        _litUntil = Environment.TickCount64 + 900;
        _anim.Start();
        Step();
    }

    void Step()
    {
        _pos += (_target - _pos) * 0.28f;   // ease out: big jumps start fast and settle softly
        if (Math.Abs(_target - _pos) < 0.5f) _pos = _target;
        int top = -(int)Math.Round(_pos);
        if (Content.Top != top) { Content.Top = top; Update(); }
        PlaceThumb();
        if (_pos == _target && Environment.TickCount64 > _litUntil && !_thumb.Lit) _anim.Stop();
    }

    void PlaceThumb()
    {
        _thumb.Visible = Max > 0;
        if (Max <= 0) return;
        int w = S(4), h = Math.Max(S(28), Height * Height / Math.Max(1, Content.Height));
        int y = (int)((Height - h) * (_pos / Max));
        _thumb.SetBounds(Width - S(3) - w, y, w, h);
        _thumb.Active = Environment.TickCount64 < _litUntil;
        _thumb.Invalidate();
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg != WM_MOUSEWHEEL || !Visible || Max <= 0) return false;
        if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) return false;
        if (FromHandle(m.HWnd)?.FindForm() != FindForm()) return false;
        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
        ScrollTo(_target - delta / 120f * S(90));
        return true;
    }

    /// <summary>The thin position bar; dragging it scrolls proportionally.</summary>
    sealed class Thumb : DrawnControl
    {
        readonly SmoothScroll _owner;
        int _grabY = -1;
        float _grabPos;
        public bool Active { get; set; }
        public bool Lit => Hover || _grabY >= 0;

        public Thumb(SmoothScroll owner) { _owner = owner; Cursor = Cursors.Default; }

        protected override void Draw(Graphics g)
        {
            float a = Lit ? 0.55f : Active ? 0.4f : 0.16f;
            using var b = new SolidBrush(Blend(Theme.Bg, Fg, a));
            using var p = Round(new RectangleF(0, 0, Width, Height), Width / 2f);
            g.FillPath(b, p);
        }

        protected override void OnMouseDown(MouseEventArgs e) { _grabY = Cursor.Position.Y; _grabPos = _owner._pos; Capture = true; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _grabY = -1; Capture = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_grabY < 0) return;
            float track = Math.Max(1, _owner.Height - Height);
            _owner.ScrollTo(_grabPos + (Cursor.Position.Y - _grabY) * _owner.Max / track, instant: true);
        }
    }
}

/// <summary>The notice shown where a reading needs the PawnIO driver and it is missing: the text and a link to its site.</summary>
public static class PawnIoNotice
{
    const string Site = "pawnio.eu", Url = "https://pawnio.eu/";

    public static LinkLabel Make(string textKey)
    {
        string text = L.T(textKey) + " ";
        var l = new LinkLabel
        {
            Text = text + Site, Font = Theme.Font(9f), ForeColor = Warn, AutoSize = false, Visible = false,
            LinkArea = new LinkArea(text.Length, Site.Length), LinkColor = Accent, ActiveLinkColor = Fg, VisitedLinkColor = Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        l.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("open link: " + ex.Message); }
        };
        return l;
    }
}
