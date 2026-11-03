namespace HeySip.Core.Features.Companion.Store;

/// <summary>
/// The list that comes with the app, plus whatever an online list adds. Online wins for an item that is in both, so a
/// newer version can be offered. If the online list can't be had, the built-in one is shown with a note saying so; the page
/// is never emptier because the network is down. Each download goes to the source its item came from.
/// </summary>
/// <param name="useOnline">Asked each time: false (the user switched online lookups off) means the online list is not touched at all.</param>
public sealed class LayeredStoreSource(IStoreSource online, IStoreSource builtIn, Func<bool>? useOnline = null) : IStoreSource
{
    private readonly Lock _gate = new();
    private Dictionary<string, IStoreSource> _origin = [];

    private bool OnlineAllowed => useOnline?.Invoke() ?? true;

    public async Task<StoreCatalogResult> LoadAsync(CancellationToken cancel)
    {
        var local = await builtIn.LoadAsync(cancel);
        if (!local.Ok)
        {
            return local;
        }

        StoreCatalogResult? remote = null;
        string? notice = null;
        if (!OnlineAllowed)
        {
            notice = "Looking online is turned off in Settings, so this is only what comes with the app.";
        }
        else
        {
            try
            {
                remote = await online.LoadAsync(cancel);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                remote = StoreCatalogResult.Failure("The online list couldn't be loaded");
            }

            if (!remote.Ok)
            {
                notice = "The online list couldn't be reached, so this is only what comes with the app.";
                remote = null;
            }
        }

        var origin = new Dictionary<string, IStoreSource>(StringComparer.Ordinal);
        var entries = new List<StoreEntry>();
        foreach (var entry in remote?.Entries ?? [])
        {
            origin[entry.Id] = online;
            entries.Add(entry);
        }

        foreach (var entry in local.Entries.Where(entry => !origin.ContainsKey(entry.Id)))
        {
            origin[entry.Id] = builtIn;
            entries.Add(entry);
        }

        lock (_gate)
        {
            _origin = origin;
        }

        return new StoreCatalogResult(entries, local.Skipped + (remote?.Skipped ?? 0), null, notice);
    }

    public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
    {
        IStoreSource? source;
        lock (_gate)
        {
            _origin.TryGetValue(entry.Id, out source);
        }

        if (source is null)
        {
            throw new IOException("This item isn't in the list");
        }

        // The switch may have been turned off since the list was read.
        if (ReferenceEquals(source, online) && !OnlineAllowed)
        {
            throw new IOException("Looking online is turned off in Settings");
        }

        return source.FetchPackAsync(entry, cancel);
    }
}
