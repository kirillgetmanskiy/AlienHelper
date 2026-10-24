using System.Drawing;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// Shown before every UAC prompt the app causes (at start, and "Restart as administrator" in limited mode): what the
/// admin rights are for, and what still works without them. It can be turned off with "Don't show again".
/// </summary>
public sealed class ElevationNotice : Form
{
    const int W = 520, M = 22;
    readonly CheckBox _dontShow;

    /// <summary>True: go on to the UAC prompt. False: stay without admin rights (limited mode).</summary>
    public static bool Confirm(IWin32Window? owner, Settings s)
    {
        if (s.ElevationNoticeOff) return true;
        using var f = new ElevationNotice();
        bool go = f.ShowDialog(owner) == DialogResult.OK;
        if (f._dontShow.Checked) { s.ElevationNoticeOff = true; s.Save(); }
        return go;
    }

    internal ElevationNotice()
    {
        Text = Log.AppTitle;
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;   // at sign-in nothing else is in front yet
        int inner = S(W - 2 * M), y = S(M);

        var title = new Label { Text = L.T("elev.title"), Font = Semibold(13f), ForeColor = Fg, AutoSize = true, Location = new Point(S(M), y) };
        Controls.Add(title);
        y += title.PreferredHeight + S(10);

        var font = Theme.Font(9.5f);
        string body = L.T("elev.text");
        var text = new Label { Text = body, Font = font, ForeColor = Blend(Muted, Fg, 0.6f), AutoSize = false };
        text.Bounds = new Rectangle(S(M), y, inner, TextRenderer.MeasureText(body, font, new Size(inner, 0), TextFormatFlags.WordBreak).Height + S(6));
        Controls.Add(text);
        y = text.Bottom + S(12);

        _dontShow = new CheckBox { Text = L.T("elev.dontshow"), Font = font, ForeColor = Muted, AutoSize = true, Location = new Point(S(M), y) };
        Controls.Add(_dontShow);
        y += S(34);

        var go = new PillButton(L.T("elev.continue")) { Active = true, Height = S(32) };
        go.Width = TextRenderer.MeasureText(go.Text, font).Width + S(40);
        go.Location = new Point(S(M) + inner - go.Width, y);
        go.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(go);
        var stay = new PillButton(L.T("elev.limited")) { Height = S(32) };
        stay.Width = TextRenderer.MeasureText(stay.Text, font).Width + S(40);
        stay.Location = new Point(go.Left - S(10) - stay.Width, y);
        stay.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(stay);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
        ClientSize = new Size(S(W), go.Bottom + S(M));
    }
}
