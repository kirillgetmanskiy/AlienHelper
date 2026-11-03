using System.Diagnostics;
using System.Drawing;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>About: version, compatibility, disclaimer, and clickable links to the source code and the author.</summary>
public sealed class AboutForm : Form
{
    const int W = 560, M = 22;

    public AboutForm()
    {
        var v = typeof(AboutForm).Assembly.GetName().Version;
        Text = Log.AppTitle + " — " + L.T("about");
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        int inner = S(W - 2 * M), y = S(M);

        // icon and name with the version
        var icon = new PictureBox { Image = AppIcon(new Size(S(48), S(48))).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(S(M), y, S(48), S(48)) };
        Controls.Add(icon);
        var title = new Label { Text = Log.AppTitle, Font = Semibold(16f), ForeColor = Fg, AutoSize = true, Location = new Point(S(M) + S(60), y) };
        Controls.Add(title);
        Controls.Add(new Label
        {
            Text = L.T("about.version", v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}"), Font = Theme.Font(9.5f), ForeColor = Muted,
            AutoSize = true, Location = new Point(S(M) + S(62), y + S(30)),
        });
        y += S(48) + S(16);

        // the existing texts: independence and risk, then compatibility (version already shown above)
        string intro = L.T("about.text", "");
        int cut = intro.IndexOf("\n\n");   // its first line repeats the name and version shown above
        string body = (cut >= 0 ? intro[(cut + 2)..] : intro) + "\n\n" + L.T("about.devices", Hardware.SystemInfo.TestedModel + " (AA16250)");
        var text = new Label { Text = body.Trim(), Font = Theme.Font(9.5f), ForeColor = Blend(Muted, Fg, 0.55f), AutoSize = false };
        text.Bounds = new Rectangle(S(M), y, inner, TextRenderer.MeasureText(body.Trim(), text.Font, new Size(inner, 0), TextFormatFlags.WordBreak).Height + S(6));
        Controls.Add(text);
        y = text.Bottom + S(14);

        // links: source code, support and the author
        y = Link(L.T("about.source"), Log.RepoUrl, Log.RepoUrl, y);
        y = Link(L.T("about.support"), "Microsoft Store", Log.StoreUrl, y);
        y = Link(L.T("about.contact", "").TrimEnd(' ', ':', '：'), Log.ContactEmail, "mailto:" + Log.ContactEmail, y);
        y += S(10);

        var ok = new PillButton("OK") { Active = true, Bounds = new Rectangle(S(M) + inner - S(110), y, S(110), S(32)) };
        ok.Click += (_, _) => Close();
        Controls.Add(ok);
        AcceptButton = null;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.Enter) Close(); };
        ClientSize = new Size(S(W), ok.Bottom + S(M));
    }

    /// <summary>A caption and a link on one line; the link opens in the browser or the mail app.</summary>
    int Link(string caption, string text, string target, int y)
    {
        var cap = new Label { Text = caption + ":", Font = Theme.Font(9.5f), ForeColor = Muted, AutoSize = true, Location = new Point(S(M), y) };
        Controls.Add(cap);
        var link = new LinkLabel
        {
            Text = text, Font = Theme.Font(9.5f), AutoSize = true, Location = new Point(S(M) + cap.PreferredWidth + S(6), y),
            LinkColor = Accent, ActiveLinkColor = Fg, VisitedLinkColor = Accent, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Bg,
        };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("open link: " + ex.Message); }
        };
        Controls.Add(link);
        return y + S(26);
    }
}
