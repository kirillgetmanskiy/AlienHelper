using System.Runtime.InteropServices;
using Windows.Services.Store;

namespace AlienHelper;

/// <summary>A paid plugin sold as an add-on of the app in the Microsoft Store.</summary>
public sealed record StoreAddon(string StoreId, string Title, string Description, string Price, bool Owned);

/// <summary>
/// Plugins sold as Microsoft Store add-ons (durables with a package): buying one installs its optional package, which
/// <see cref="PluginManager"/> then loads like a plugin from the plugins folder. Only in the Store build: without
/// package identity there is no Store to buy from.
/// </summary>
public static class StoreAddons
{
    public static bool Available => Autostart.IsPackaged();

    static StoreContext? _ctx;

    /// <summary>The user's add-on licenses changed (bought, refunded); raised on a thread of the Store's.</summary>
    public static event Action? LicensesChanged;

    /// <summary>
    /// The Store. With a window, the Store's dialogs belong to it (a desktop app has to say which): a context of its
    /// own each time, since the window may be closed later. Without one, the shared context for licenses and events.
    /// </summary>
    static StoreContext Context(IntPtr owner)
    {
        if (owner != IntPtr.Zero)
        {
            var ctx = StoreContext.GetDefault();
            try { WinRT.Interop.InitializeWithWindow.Initialize(ctx, owner); }
            catch (Exception ex) { Log.Warn($"store: window: {ex.Message}"); }   // already tied to a window: dialogs still work
            return ctx;
        }
        if (_ctx == null)
        {
            _ctx = StoreContext.GetDefault();
            _ctx.OfflineLicensesChanged += (_, _) => LicensesChanged?.Invoke();
        }
        return _ctx;
    }

    /// <summary>The app's add-ons with their prices; empty outside the Store build or when the Store cannot be reached.</summary>
    public static async Task<(List<StoreAddon> list, string? error)> ListAsync(IntPtr owner)
    {
        if (!Available) return (new(), null);
        try
        {
            var r = await Context(owner).GetAssociatedStoreProductsAsync(new[] { "Durable" });
            if (r.ExtendedError != null) return (new(), r.ExtendedError.Message);
            var list = r.Products.Values
                .Select(p => new StoreAddon(p.StoreId, p.Title, p.Description, p.Price.FormattedPrice, p.IsInUserCollection))
                .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            return (list, null);
        }
        catch (Exception ex) { Log.Error("store: add-ons", ex); return (new(), ex.Message); }
    }

    /// <summary>
    /// The Store IDs of the add-ons the user owns now; null when the Store cannot say (outside the Store build, or an
    /// error). A durable without a package is not in the app license's AddOnLicenses (it stays empty), only in the
    /// user's collection, so both are asked. A license key is "&lt;Store ID&gt;/&lt;SKU&gt;".
    /// </summary>
    public static async Task<HashSet<string>?> OwnedAsync()
    {
        if (!Available) return null;
        try
        {
            var ctx = Context(IntPtr.Zero);
            var license = await ctx.GetAppLicenseAsync();
            var owned = license.AddOnLicenses
                .Where(kv => kv.Value.IsActive)
                .Select(kv => (kv.Value.SkuStoreId is { Length: > 0 } sku ? sku : kv.Key).Split('/')[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var collection = await ctx.GetUserCollectionAsync(new[] { "Durable" });
            if (collection.ExtendedError != null)
            {
                Log.Warn($"store: collection: {collection.ExtendedError.Message}");
                if (owned.Count == 0) return null;   // offline, say: not known, rather than "not bought"
            }
            foreach (var p in collection.Products.Values.Where(p => p.IsInUserCollection || p.Skus.Any(s => s.IsInUserCollection)))
                owned.Add(p.StoreId);
            Log.Info($"store: owned add-ons: {(owned.Count > 0 ? string.Join(", ", owned) : "none")}");
            return owned;
        }
        catch (Exception ex) { Log.Error("store: licenses", ex); return null; }
    }

    /// <summary>
    /// Buys the add-on (the Store's own purchase dialog) unless already owned, then, with <paramref name="install"/>,
    /// downloads and installs its package (a plugin that ships inside the app's package has none).
    /// Null when done; otherwise why not. False in <paramref name="bought"/> when the user closed the dialog.
    /// </summary>
    public static async Task<(bool bought, string? error)> BuyAndInstallAsync(string storeId, IntPtr owner, bool install = true)
    {
        try
        {
            var ctx = Context(owner);
            var p = await ctx.RequestPurchaseAsync(storeId);
            if (p.ExtendedError != null) return (false, p.ExtendedError.Message);
            if (p.Status is StorePurchaseStatus.NotPurchased) return (false, null);
            if (p.Status is not (StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)) return (false, p.Status.ToString());
            Log.Info($"store: add-on {storeId} {p.Status}");
            if (!install) return (true, null);
            var result = await ctx.RequestDownloadAndInstallStorePackagesAsync(new[] { storeId });
            if (result.OverallState != StorePackageUpdateState.Completed) return (true, result.OverallState.ToString());
            Log.Info($"store: add-on {storeId} installed");
            return (true, null);
        }
        catch (Exception ex) { Log.Error($"store: buying {storeId}", ex); return (false, ex.Message); }
    }

    /// <summary>
    /// The folders of the app's installed optional packages (add-ons with a package), each one plugin with its
    /// plugin.json at the root. Only those of the app's own publisher: the Store lets nobody else make them.
    /// </summary>
    public static IEnumerable<string> PackageFolders()
    {
        if (!Available) return Array.Empty<string>();
        try
        {
            var me = Windows.ApplicationModel.Package.Current;
            return me.Dependencies
                .Where(d => d.IsOptional && d.Id.PublisherId == me.Id.PublisherId)
                .Select(d => d.InstalledLocation.Path)
                .ToList();
        }
        catch (Exception ex) { Log.Error("store: optional packages", ex); return Array.Empty<string>(); }
    }
}
