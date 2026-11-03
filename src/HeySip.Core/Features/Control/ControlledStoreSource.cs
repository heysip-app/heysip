using HeySip.Core.Features.Companion.Store;

namespace HeySip.Core.Features.Control;

/// <summary>
/// The online list, at whatever addresses the signed control file currently gives for this version of the app, tried in order. Nothing
/// else tells the app where it is. The list must be signed with a key the app trusts (a list on this very computer is exempt).
/// With no address yet it contributes nothing and says nothing. A list that can't be reached fails fast for a few minutes instead of
/// making every visit to the Characters page wait again. Packs the control file has revoked are not offered.
/// </summary>
public sealed class ControlledStoreSource(ControlService control, string userAgent, TimeProvider time, HttpMessageHandler? handler = null) : IStoreSource, IDisposable
{
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly Lock _gate = new();
    private readonly Dictionary<Uri, HttpStoreSource> _sources = [];
    private HttpStoreSource? _active;
    private DateTimeOffset _failedUntil;

    public async Task<StoreCatalogResult> LoadAsync(CancellationToken cancel)
    {
        var urls = control.CatalogUrls();
        if (urls.Count == 0)
        {
            return new StoreCatalogResult([], 0, null);
        }

        lock (_gate)
        {
            if (time.GetUtcNow() < _failedUntil)
            {
                return StoreCatalogResult.Failure("The online list couldn't be reached");
            }
        }

        foreach (var url in urls)
        {
            var source = SourceFor(url);
            var result = await source.LoadAsync(cancel);
            if (!result.Ok)
            {
                continue;
            }

            lock (_gate)
            {
                _active = source;
            }

            return result with { Entries = [.. result.Entries.Where(entry => !control.IsRevoked(entry.Id, entry.Sha256))] };
        }

        lock (_gate)
        {
            _failedUntil = time.GetUtcNow() + RetryAfterFailure;
        }

        return StoreCatalogResult.Failure("The online list couldn't be reached");
    }

    public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
    {
        HttpStoreSource? source;
        lock (_gate)
        {
            source = _active;
        }

        if (source is null || control.IsRevoked(entry.Id, entry.Sha256))
        {
            throw new IOException("This item isn't available");
        }

        return source.FetchPackAsync(entry, cancel);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var source in _sources.Values)
            {
                source.Dispose();
            }

            _sources.Clear();
            _active = null;
        }
    }

    private HttpStoreSource SourceFor(Uri url)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(url, out var source))
            {
                source = new HttpStoreSource(url, userAgent, handler, RequestTimeout, control.IsSignedByTrustedKey);
                _sources[url] = source;
            }

            return source;
        }
    }
}
