using System.Drawing;
using System.Runtime.InteropServices;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// Short on-screen hint after the mode hotkey: centred near the bottom of the screen, click-through,
/// never takes focus, fades out after ~1.6 s. Visible over borderless games like the overlay.
/// </summary>
public sealed class ModeOsd : Form
{
    readonly System.Windows.Forms.Timer _hide = new() { Interval = 1600 };
    string _mode = "";
    bool _alien;

    public ModeOsd()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(14, 16, 20);
        Opacity = 0.92;
        DoubleBuffered = true;
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;   // layered, click-through, toolwindow, noactivate, topmost
            return cp;
        }
    }

    public void Flash(string mode, bool alien)
    {
        _mode = mode;
        _alien = alien;
        using var f = Semibold(16f);
        using var lf = Theme.Font(9f);
        var sz = TextRenderer.MeasureText(mode, f);
        var lsz = TextRenderer.MeasureText(L.T("card.mode"), lf);
        Size = new Size(Math.Max(sz.Width, lsz.Width) + S(56), sz.Height + lsz.Height + S(22));
        var b = Screen.PrimaryScreen!.Bounds;
        Location = new Point(b.Left + (b.Width - Width) / 2, b.Bottom - Height - S(90));
        using (var p = Round(new RectangleF(0, 0, Width, Height), Sf(14))) Region = new Region(p);
        if (!IsHandleCreated) CreateHandle();
        ShowWindow(Handle, 4);   // SW_SHOWNOACTIVATE
        Visible = true;
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Invalidate();
        _hide.Stop();
        _hide.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var dark = Theme.Use(Theme.Look.Dark);   // shown over games: always dark
        var g = e.Graphics;
        Smooth(g);
        var accent = _alien ? Warm : Accent;
        using (var bar = new SolidBrush(accent)) g.FillRectangle(bar, 0, 0, S(5), Height);
        using var lf = Theme.Font(9f);
        using var f = Semibold(16f);
        TextRenderer.DrawText(g, L.T("card.mode"), lf, new Point(S(24), S(9)), Muted);
        TextRenderer.DrawText(g, _mode, f, new Point(S(22), S(9) + lf.Height + S(2)), accent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
