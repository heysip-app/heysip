namespace HeySip.Core.Features.Companion.Store;

/// <summary>
/// One GET with a time limit and a size limit, through a client that follows few redirects and keeps no cookies. A redirect may
/// only lead to an address <see cref="StoreAddress"/> allows. Every failure is an <see cref="IOException"/>, never anything else.
/// </summary>
public sealed class BoundedHttp : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    /// <param name="userAgent">The product name sent with each request, such as <c>HeySip/1.0</c>.</param>
    /// <param name="timeout">How long one request may take.</param>
    /// <param name="handler">For tests; the real one is made here.</param>
    public BoundedHttp(string userAgent, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        _timeout = timeout;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 3 })
        {
            Timeout = Timeout.InfiniteTimeSpan, // each request has its own limit, below
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<byte[]> GetAsync(Uri url, int maxBytes, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);

            // A redirect may only lead somewhere the app would have gone anyway.
            if (response.RequestMessage?.RequestUri is { } final && !StoreAddress.IsAllowed(final))
            {
                throw new IOException("The server sent the app somewhere it won't go");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"The server answered {(int)response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new IOException("The answer was too big");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new IOException("The answer was too big");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new IOException("The server took too long to answer");
        }
        catch (HttpRequestException exception)
        {
            throw new IOException("The server couldn't be reached", exception);
        }
    }

    public void Dispose() => _http.Dispose();
}
