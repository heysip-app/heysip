using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Core.Features.Companion.Store;

/// <summary>
/// The list and the pack files from a web address. The list is one JSON file; each item's <c>pack</c> is found relative to
/// it (or is a full https address). Nothing is sent but a plain request with a product name: no identifiers, no cookies.
/// Every answer is size-limited and time-limited, and a failure is an <see cref="IOException"/> or a failed result, never a crash.
/// </summary>
public sealed class HttpStoreSource : IStoreSource, IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private const int MaxSignatureBytes = 1024;

    private readonly Uri _list;
    private readonly BoundedHttp _http;
    private readonly Func<byte[], byte[], bool>? _verify;

    /// <param name="listUrl">Where the list is. Must be https (or plain http to this computer).</param>
    /// <param name="userAgent">The product name sent with each request, such as <c>HeySip/1.0</c>.</param>
    /// <param name="handler">For tests; the real one is made here.</param>
    /// <param name="timeout">How long one request may take.</param>
    public HttpStoreSource(Uri listUrl, string userAgent, HttpMessageHandler? handler = null, TimeSpan? timeout = null, Func<byte[], byte[], bool>? verify = null)
    {
        if (!StoreAddress.IsAllowed(listUrl))
        {
            throw new ArgumentException("The list address must be https.", nameof(listUrl));
        }

        _list = listUrl;
        _verify = verify;
        _http = new BoundedHttp(userAgent, timeout ?? DefaultTimeout, handler);
    }

    public async Task<StoreCatalogResult> LoadAsync(CancellationToken cancel)
    {
        try
        {
            var bytes = await GetAsync(_list, StoreCatalog.MaxBytes, cancel);

            // With a check supplied, the list must come with a signature file next to it (the same name ending in .sig) that passes it.
            // A list on this very computer is exempt, so a server can be tried out without signing; it can't come from the internet.
            if (_verify is not null && !_list.IsLoopback)
            {
                var signature = await GetAsync(new Uri(_list, Path.ChangeExtension(Path.GetFileName(_list.AbsolutePath), ".sig")), MaxSignatureBytes, cancel);
                if (!_verify(bytes, signature))
                {
                    return StoreCatalogResult.Failure("The online list isn't signed by a key this version trusts");
                }
            }

            return StoreCatalog.Parse(bytes);
        }
        catch (IOException)
        {
            return StoreCatalogResult.Failure("The online list couldn't be reached");
        }
    }

    public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
    {
        if (entry.PackLocation is not { } location)
        {
            throw new IOException("This item has no file to download");
        }

        if (!Uri.TryCreate(_list, location, out var url) || !StoreAddress.IsAllowed(url))
        {
            throw new IOException("The file's address isn't one the app will use");
        }

        return GetAsync(url, PackLimits.MaxPackBytes, cancel);
    }

    public void Dispose() => _http.Dispose();

    private Task<byte[]> GetAsync(Uri url, int maxBytes, CancellationToken cancel) => _http.GetAsync(url, maxBytes, cancel);
}
