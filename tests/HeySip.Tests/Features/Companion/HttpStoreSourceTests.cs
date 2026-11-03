using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;

namespace HeySip.Tests.Features.Companion;

public sealed class HttpStoreSourceTests
{
    private static readonly Uri ListUrl = new("https://example.com/store/catalog.json");

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }

    /// <summary>A stream that can't say how long it is, like a server that doesn't send a length.</summary>
    private sealed class OneWayStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Ok(HttpRequestMessage request, byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request };

    private static HttpStoreSource Source(FakeHandler handler, TimeSpan? timeout = null) =>
        new(ListUrl, "HeySip/1.0", handler, timeout);

    private static byte[] Catalog(string pack = "packs/blob.heysippack")
    {
        var item = new JsonObject { ["id"] = "tester.blob", ["kind"] = "character", ["name"] = "Blob", ["version"] = "1.0.0", ["pack"] = pack };
        return Encoding.UTF8.GetBytes(new JsonObject { ["formatVersion"] = 1, ["items"] = new JsonArray(item) }.ToJsonString());
    }

    private static StoreEntry Entry(string? pack = "packs/blob.heysippack") =>
        new("tester.blob", PackKind.Character, "Blob", "Tester", "1.0.0", "", 0, pack, null, 0, null);

    // ---- the list

    [Fact]
    public async Task The_list_is_read_from_its_address()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, Catalog())));

        var result = await Source(handler).LoadAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("tester.blob", Assert.Single(result.Entries).Id);
        Assert.Equal(ListUrl, Assert.Single(handler.Requests).RequestUri);
    }

    [Fact]
    public async Task Only_a_plain_request_with_the_product_name_is_sent()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, Catalog())));

        await Source(handler).LoadAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("HeySip/1.0", request.Headers.UserAgent.ToString());
        Assert.Equal(["User-Agent"], request.Headers.Select(header => header.Key));
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task A_list_that_cannot_be_reached_is_a_failed_result_not_an_exception()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("no network"));

        var result = await Source(handler).LoadAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Empty(result.Entries);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_server_error_is_a_failed_result(HttpStatusCode status)
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request }));

        var result = await Source(handler).LoadAsync(CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task A_list_that_is_not_a_list_is_a_failed_result()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, Encoding.UTF8.GetBytes("<html>Welcome</html>"))));

        var result = await Source(handler).LoadAsync(CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task A_list_that_is_too_big_is_refused_whether_or_not_the_server_says_how_big_it_is()
    {
        var big = new byte[StoreCatalog.MaxBytes + 1];

        var announced = new FakeHandler((request, _) => Task.FromResult(Ok(request, big)));
        Assert.False((await Source(announced).LoadAsync(CancellationToken.None)).Ok);

        var silent = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new OneWayStream(big)), RequestMessage = request }));
        Assert.False((await Source(silent).LoadAsync(CancellationToken.None)).Ok);
    }

    // ---- a pack file

    [Fact]
    public async Task A_pack_is_fetched_from_next_to_the_list()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, [1, 2, 3])));

        var bytes = await Source(handler).FetchPackAsync(Entry("packs/blob.heysippack"), CancellationToken.None);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Equal(new Uri("https://example.com/store/packs/blob.heysippack"), Assert.Single(handler.Requests).RequestUri);
    }

    [Fact]
    public async Task A_pack_may_live_on_another_https_host()
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, [9])));

        await Source(handler).FetchPackAsync(Entry("https://cdn.example.org/a/blob.heysippack"), CancellationToken.None);

        Assert.Equal(new Uri("https://cdn.example.org/a/blob.heysippack"), Assert.Single(handler.Requests).RequestUri);
    }

    [Theory]
    [InlineData("http://cdn.example.org/blob.heysippack")]
    [InlineData("ftp://example.com/blob.heysippack")]
    [InlineData("file:///c:/secret.zip")]
    [InlineData("https://user:password@example.com/blob.heysippack")]
    [InlineData(null)]
    public async Task A_pack_address_the_app_would_not_use_is_refused_without_asking_the_server(string? location)
    {
        var handler = new FakeHandler((request, _) => Task.FromResult(Ok(request, [1])));

        await Assert.ThrowsAsync<IOException>(() => Source(handler).FetchPackAsync(Entry(location), CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_pack_that_is_missing_or_unreachable_is_an_IOException()
    {
        var missing = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }));
        await Assert.ThrowsAsync<IOException>(() => Source(missing).FetchPackAsync(Entry(), CancellationToken.None));

        var offline = new FakeHandler((_, _) => throw new HttpRequestException("no network"));
        await Assert.ThrowsAsync<IOException>(() => Source(offline).FetchPackAsync(Entry(), CancellationToken.None));
    }

    [Fact]
    public async Task A_pack_bigger_than_the_limit_is_refused_whether_or_not_the_server_says_how_big_it_is()
    {
        var big = new byte[PackLimits.MaxPackBytes + 1];

        var announced = new FakeHandler((request, _) => Task.FromResult(Ok(request, big)));
        await Assert.ThrowsAsync<IOException>(() => Source(announced).FetchPackAsync(Entry(), CancellationToken.None));

        var silent = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new OneWayStream(big)), RequestMessage = request }));
        await Assert.ThrowsAsync<IOException>(() => Source(silent).FetchPackAsync(Entry(), CancellationToken.None));
    }

    [Fact]
    public async Task A_pack_exactly_at_the_limit_is_accepted()
    {
        var exact = new byte[PackLimits.MaxPackBytes];
        var handler = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new OneWayStream(exact)), RequestMessage = request }));

        var bytes = await Source(handler).FetchPackAsync(Entry(), CancellationToken.None);

        Assert.Equal(PackLimits.MaxPackBytes, bytes.Length);
    }

    // ---- redirects, slow servers, giving up

    [Fact]
    public async Task A_redirect_to_a_plain_http_site_is_refused()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1]),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://evil.example.com/blob.heysippack"),
        }));

        await Assert.ThrowsAsync<IOException>(() => Source(handler).FetchPackAsync(Entry(), CancellationToken.None));
    }

    [Fact]
    public async Task A_server_that_never_answers_gives_up_with_an_IOException()
    {
        var handler = new FakeHandler(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });

        await Assert.ThrowsAsync<IOException>(() => Source(handler, TimeSpan.FromMilliseconds(80)).FetchPackAsync(Entry(), CancellationToken.None));
        Assert.False((await Source(handler, TimeSpan.FromMilliseconds(80)).LoadAsync(CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Cancelling_stops_the_request_and_is_not_mistaken_for_a_slow_server()
    {
        var handler = new FakeHandler(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(handler).FetchPackAsync(Entry(), cancel.Token));
    }

    [Theory]
    [InlineData("http://example.com/catalog.json")]
    [InlineData("ftp://example.com/catalog.json")]
    [InlineData("https://user:secret@example.com/catalog.json")]
    public void A_list_address_the_app_would_not_use_cannot_be_given(string address)
    {
        Assert.Throws<ArgumentException>(() => new HttpStoreSource(new Uri(address), "HeySip/1.0"));
    }

    // ---- a real server on this computer

    [Fact]
    public async Task It_works_against_a_real_web_server()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var served = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var context = await listener.GetContextAsync();
                var path = context.Request.Url!.AbsolutePath;
                byte[]? body = path switch
                {
                    "/store/catalog.json" => Catalog(),
                    "/store/packs/blob.heysippack" => [7, 7, 7],
                    _ => null,
                };
                context.Response.StatusCode = body is null ? 404 : 200;
                if (body is not null)
                {
                    await context.Response.OutputStream.WriteAsync(body);
                }

                context.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        using var source = new HttpStoreSource(new Uri($"http://127.0.0.1:{port}/store/catalog.json"), "HeySip/1.0");
        var list = await source.LoadAsync(CancellationToken.None);
        var entry = Assert.Single(list.Entries);
        var pack = await source.FetchPackAsync(entry, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => source.FetchPackAsync(entry with { PackLocation = "packs/missing.heysippack" }, CancellationToken.None));
        await served;

        Assert.Equal([7, 7, 7], pack);
    }

    private static int FreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}

public sealed class StoreAddressTests
{
    [Theory]
    [InlineData("https://example.com/catalog.json", true)]
    [InlineData("HTTPS://EXAMPLE.COM/x", true)]
    [InlineData("http://localhost:5000/catalog.json", true)]
    [InlineData("http://127.0.0.1/catalog.json", true)]
    [InlineData("http://example.com/catalog.json", false)]
    [InlineData("ftp://example.com/x", false)]
    [InlineData("file:///c:/x.json", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:pass@example.com/x", false)]
    [InlineData("example.com/catalog.json", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Only_https_or_this_computer_is_allowed(string? text, bool allowed)
    {
        Assert.Equal(allowed, StoreAddress.Parse(text) is not null);
    }

    [Fact]
    public void Surrounding_spaces_are_ignored()
    {
        Assert.Equal(new Uri("https://example.com/x"), StoreAddress.Parse("  https://example.com/x \n"));
    }
}

public sealed class LayeredStoreSourceTests
{
    private sealed class FakeSource(string name, params StoreEntry[] entries) : IStoreSource
    {
        public bool Fails { get; set; }

        public bool Throws { get; set; }

        public int Skipped { get; set; }

        public int Loads { get; private set; }

        public List<string> Fetched { get; } = [];

        public Task<StoreCatalogResult> LoadAsync(CancellationToken cancel)
        {
            Loads++;
            if (Throws)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(Fails ? StoreCatalogResult.Failure("down") : new StoreCatalogResult(entries, Skipped, null));
        }

        public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
        {
            Fetched.Add(entry.Id);
            return Task.FromResult(Encoding.UTF8.GetBytes(name));
        }
    }

    private static StoreEntry Entry(string id, string version = "1.0.0") =>
        new(id, PackKind.Character, id, "Tester", version, "", 0, "packs/" + id + ".heysippack", null, 0, null);

    [Fact]
    public async Task The_two_lists_are_joined()
    {
        var layered = new LayeredStoreSource(new FakeSource("online", Entry("tester.new")), new FakeSource("local", Entry("tester.old")));

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(["tester.new", "tester.old"], result.Entries.Select(entry => entry.Id).Order());
        Assert.Null(result.Notice);
    }

    [Fact]
    public async Task Online_wins_for_an_item_in_both_and_its_file_is_fetched_from_online()
    {
        var online = new FakeSource("online", Entry("tester.both", "2.0.0"));
        var local = new FakeSource("local", Entry("tester.both", "1.0.0"), Entry("tester.old"));
        var layered = new LayeredStoreSource(online, local);

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.Equal("2.0.0", result.Entries.Single(entry => entry.Id == "tester.both").Version);
        Assert.Equal("online", Encoding.UTF8.GetString(await layered.FetchPackAsync(result.Entries.Single(entry => entry.Id == "tester.both"), CancellationToken.None)));
        Assert.Equal("local", Encoding.UTF8.GetString(await layered.FetchPackAsync(result.Entries.Single(entry => entry.Id == "tester.old"), CancellationToken.None)));
        Assert.Equal(["tester.both"], online.Fetched);
        Assert.Equal(["tester.old"], local.Fetched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_online_cannot_be_had_the_built_in_list_is_shown_with_a_note(bool throws)
    {
        var online = new FakeSource("online", Entry("tester.new")) { Fails = !throws, Throws = throws };
        var layered = new LayeredStoreSource(online, new FakeSource("local", Entry("tester.old")));

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(["tester.old"], result.Entries.Select(entry => entry.Id));
        Assert.Contains("couldn't be reached", result.Notice);
    }

    [Fact]
    public async Task With_online_switched_off_the_online_list_is_never_touched_and_the_note_says_why()
    {
        var online = new FakeSource("online", Entry("tester.new"));
        var enabled = false;
        var layered = new LayeredStoreSource(online, new FakeSource("local", Entry("tester.old")), () => enabled);

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.Equal(0, online.Loads);
        Assert.Equal(["tester.old"], result.Entries.Select(entry => entry.Id));
        Assert.Contains("turned off in Settings", result.Notice);

        enabled = true;
        result = await layered.LoadAsync(CancellationToken.None);

        Assert.Equal(1, online.Loads);
        Assert.Equal(["tester.new", "tester.old"], result.Entries.Select(entry => entry.Id).Order());
        Assert.Null(result.Notice);
    }

    [Fact]
    public async Task Switching_online_off_after_the_list_was_read_stops_downloads_from_online_but_not_built_in_ones()
    {
        var online = new FakeSource("online", Entry("tester.new"));
        var enabled = true;
        var layered = new LayeredStoreSource(online, new FakeSource("local", Entry("tester.old")), () => enabled);
        var result = await layered.LoadAsync(CancellationToken.None);

        enabled = false;

        await Assert.ThrowsAsync<IOException>(() => layered.FetchPackAsync(result.Entries.Single(entry => entry.Id == "tester.new"), CancellationToken.None));
        Assert.Empty(online.Fetched);
        Assert.Equal("local", Encoding.UTF8.GetString(await layered.FetchPackAsync(result.Entries.Single(entry => entry.Id == "tester.old"), CancellationToken.None)));
    }

    [Fact]
    public async Task A_broken_built_in_list_is_reported_as_it_is()
    {
        var layered = new LayeredStoreSource(new FakeSource("online", Entry("tester.new")), new FakeSource("local") { Fails = true });

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Skipped_entries_from_both_lists_are_counted()
    {
        var layered = new LayeredStoreSource(new FakeSource("online", Entry("tester.new")) { Skipped = 2 }, new FakeSource("local", Entry("tester.old")) { Skipped = 1 });

        var result = await layered.LoadAsync(CancellationToken.None);

        Assert.Equal(3, result.Skipped);
    }

    [Fact]
    public async Task Cancelling_while_loading_is_not_swallowed()
    {
        var online = new CancelledSource();
        var layered = new LayeredStoreSource(online, new FakeSource("local", Entry("tester.old")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layered.LoadAsync(CancellationToken.None));
    }

    private sealed class CancelledSource : IStoreSource
    {
        public Task<StoreCatalogResult> LoadAsync(CancellationToken cancel) => throw new OperationCanceledException();

        public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel) => throw new OperationCanceledException();
    }

    [Fact]
    public async Task An_item_that_was_never_listed_cannot_be_fetched()
    {
        var layered = new LayeredStoreSource(new FakeSource("online"), new FakeSource("local"));
        await layered.LoadAsync(CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => layered.FetchPackAsync(Entry("tester.unknown"), CancellationToken.None));
    }
}
