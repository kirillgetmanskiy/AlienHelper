using System.Drawing;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// The plugins sold in the Microsoft Store as add-ons of the app: title, description and price, with a button that
/// buys one and installs it (the Store's own dialogs), after which it is turned on like a plugin from the folder.
/// </summary>
public sealed class StoreAddonsForm : Form
{
    const int W = 520, M = 22;
    readonly PluginManager _plugins;
    readonly Font _font = Theme.Font(9.5f);
    readonly Label _status;
    int _y;

    public StoreAddonsForm(PluginManager plugins)
    {
        _plugins = plugins;
        Text = $"{Log.AppTitle} — {L.T("store.title")}";
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        _y = S(M);
        var title = new Label { Text = L.T("store.title"), Font = Semibold(13f), ForeColor = Fg, AutoSize = true, Location = new Point(S(M), _y) };
        Controls.Add(title);
        _y += title.PreferredHeight + S(12);
        _status = Text_(L.T("store.loading"), Muted);
        ClientSize = new Size(S(W), _y + S(M));
        Shown += async (_, _) => await LoadAsync();
    }

    int Inner => S(W - 2 * M);

    Label Text_(string text, Color color, Font? font = null, int width = 0)
    {
        font ??= _font;
        if (width == 0) width = Inner;
        var l = new Label { Text = text, Font = font, ForeColor = color, AutoSize = false };
        l.Bounds = new Rectangle(S(M), _y, width, TextRenderer.MeasureText(text, font, new Size(width, 0), TextFormatFlags.WordBreak).Height + S(2));
        Controls.Add(l);
        _y = l.Bottom + S(4);
        return l;
    }

    async Task LoadAsync()
    {
        var (list, error) = await StoreAddons.ListAsync(Handle);
        if (IsDisposed) return;
        _y = _status.Top;
        Controls.Remove(_status);
        if (error != null) Text_(L.T("store.error", error), Warn);
        else if (list.Count == 0) Text_(L.T("store.none"), Muted);
        foreach (var a in list) AddRow(a);
        ClientSize = new Size(S(W), _y + S(M));
    }

    void AddRow(StoreAddon a)
    {
        int top = _y;
        bool installed = _plugins.Plugins.Any(p => p.FromStore && p.Manifest?.StoreId is { } id && id.Equals(a.StoreId, StringComparison.OrdinalIgnoreCase));
        var button = new PillButton(installed ? L.T("store.installed") : a.Owned ? L.T("store.install") : L.T("store.buy", a.Price))
            { Height = S(30), Active = !installed, Enabled = !installed };
        button.Width = TextRenderer.MeasureText(button.Text, _font).Width + S(36);
        button.Location = new Point(S(M) + Inner - button.Width, top);
        Controls.Add(button);
        int w = Inner - button.Width - S(12);
        Text_(a.Title, Fg, Semibold(10.5f), w);
        if (a.Description.Length > 0) Text_(a.Description, Blend(Muted, Fg, 0.5f), null, w);
        var state = Text_(" ", Muted, null, w);
        _y = Math.Max(_y, button.Bottom) + S(12);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            state.Text = L.T("store.wait");
            var (bought, error) = await StoreAddons.BuyAndInstallAsync(a.StoreId, Handle);
            if (IsDisposed) return;
            if (error == null && bought)
            {
                // just bought: on at once, as if turned on in Settings
                foreach (var p in _plugins.Rescan()) _plugins.SetEnabled(p, true);
                button.Text = L.T("store.installed");
                state.Text = "";
                return;
            }
            state.Text = error != null ? L.T("store.error", error) : "";
            state.ForeColor = Warn;
            button.Enabled = true;
        };
    }
}
