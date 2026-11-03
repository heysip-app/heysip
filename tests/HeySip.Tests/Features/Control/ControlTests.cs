using System.Net;
using System.Security.Cryptography;
using System.Text;
using HeySip.App.Features.Control;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Control;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Control;

public sealed class ControlTests : IDisposable
{
    private static readonly Uri Address = new("https://heysip-app.github.io/heysip-config/v1/control.json");
    private static readonly Uri Second = new("https://backup.example/heysip/v1/control.json");
    private const string Releases = "https://github.com/heysip-app/heysip/releases";

    private readonly CoreHarness _core = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly ECDsa _main = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _backup = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly List<ControlService> _services = [];

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        _main.Dispose();
        _backup.Dispose();
        _core.Dispose();
    }

    /// <summary>Answers by host and path, and records what was asked. A missing file is a 404.</summary>
    private sealed class Stub : HttpMessageHandler
    {
        public Dictionary<string, byte[]?> Files { get; } = [];

        public List<Uri> Requests { get; } = [];

        public Func<Uri, HttpResponseMessage?>? Override { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (Override?.Invoke(request.RequestUri!) is { } custom)
            {
                return Task.FromResult(custom);
            }

            var key = request.RequestUri!.Host + request.RequestUri.AbsolutePath;
            return Task.FromResult(Files.TryGetValue(key, out var body) && body is not null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string Json(string issuedAt = "2026-11-04T00:00:00Z", string extra = "") =>
        $$"""{ "formatVersion": 1, "issuedAt": "{{issuedAt}}"{{extra}} }""";

    /// <summary>The <c>app</c> group: versions and where to get the newest one.</summary>
    private static string App(string? latest = null, string? min = null, string? download = null) =>
        ", \"app\": { " + string.Join(", ", new[]
        {
            latest is null ? null : $"\"latestVersion\": \"{latest}\"",
            min is null ? null : $"\"minSupportedVersion\": \"{min}\"",
            download is null ? null : $"\"downloadUrl\": \"{download}\"",
        }.Where(part => part is not null)) + " }";

    /// <summary>One rule of <c>api.catalog</c>: the app versions it covers and the addresses to try, in order.</summary>
    private static string Rule(string? min, string? max, params string[] urls) =>
        $$"""{ "appVersions": { {{string.Join(", ", new[] { min is null ? null : $"\"min\": \"{min}\"", max is null ? null : $"\"max\": \"{max}\"" }.Where(part => part is not null))}} }, "urls": [{{string.Join(", ", urls.Select(url => $"\"{url}\""))}}] }""";

    private static string Api(params string[] rules) => $$""", "api": { "catalog": [{{string.Join(", ", rules)}}] }""";

    private static string Notice(string id, string text = "Hello", string extra = "") =>
        $$"""{ "id": "{{id}}", "text": "{{text}}"{{extra}} }""";

    private static string Notices(params string[] notices) => $$""", "notices": [{{string.Join(",", notices)}}]""";

    private static string Versions(string? min, string? max) =>
        ", \"appVersions\": { " + string.Join(", ", new[] { min is null ? null : $"\"min\": \"{min}\"", max is null ? null : $"\"max\": \"{max}\"" }.Where(part => part is not null)) + " }";

    private static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static string Signature(string json, ECDsa key) => ControlVerifier.Sign(Encoding.UTF8.GetBytes(json), key);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Puts a file and its signature (<c>control.sig</c>) at an address.</summary>
    private static void Serve(Stub stub, Uri address, string json, string signature)
    {
        var folder = address.Host + address.AbsolutePath[..address.AbsolutePath.LastIndexOf('/')];
        stub.Files[address.Host + address.AbsolutePath] = Bytes(json);
        stub.Files[folder + "/control.sig"] = Encoding.ASCII.GetBytes(signature + "\n");
    }

    private ControlOptions Options(params Uri[] urls)
    {
        var addresses = urls.Length == 0 ? [Address] : urls;
        return new ControlOptions(addresses, [PublicKey(_main), PublicKey(_backup)], ControlLocation.LinkRule(addresses));
    }

    private ControlService Service(Stub stub, Func<bool>? allowed = null, Version? app = null, ControlOptions? options = null, TempData? data = null)
    {
        var service = new ControlService(options ?? Options(), (data ?? _core.Data).Location, allowed ?? (() => true), app ?? new Version(0, 1, 0), _core.Time, "HeySip/0.1", handler: stub);
        _services.Add(service);
        return service;
    }

    private static bool AllowAll(Uri _) => true;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- signature ---

    [Fact]
    public void A_file_signed_by_either_key_is_trusted_and_nothing_else_is()
    {
        var data = Bytes(Json());
        var keys = new[] { PublicKey(_main), PublicKey(_backup) };
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.True(ControlVerifier.Verify(data, ControlVerifier.ReadSignature(Encoding.ASCII.GetBytes(Signature(Json(), _main)))!, keys));
        Assert.True(ControlVerifier.Verify(data, ControlVerifier.ReadSignature(Encoding.ASCII.GetBytes(Signature(Json(), _backup)))!, keys));
        Assert.False(ControlVerifier.Verify(data, ControlVerifier.ReadSignature(Encoding.ASCII.GetBytes(Signature(Json(), stranger)))!, keys));
        Assert.False(ControlVerifier.Verify(Bytes(Json(extra: App("9.9.9"))), ControlVerifier.ReadSignature(Encoding.ASCII.GetBytes(Signature(Json(), _main)))!, keys));
        Assert.False(ControlVerifier.Verify(data, new byte[64], keys));
        Assert.False(ControlVerifier.Verify(data, new byte[10], keys));
        Assert.False(ControlVerifier.Verify(data, ControlVerifier.ReadSignature(Encoding.ASCII.GetBytes(Signature(Json(), _main)))!, ["not a key", ""]));
        Assert.Null(ControlVerifier.ReadSignature("this is not base64!!"u8));
    }

    // --- reading the file ---

    [Fact]
    public void The_file_is_read_and_fields_this_version_does_not_know_are_ignored()
    {
        var json = Json(extra: App("0.3.0", "0.1.0", Releases)
            + Api(Rule("0.1.0", "0.9.9", "https://api.example/v1/catalog.json", "https://mirror.example/v1/catalog.json"), Rule("1.0.0", null, "https://api.example/v2/catalog.json"))
            + ", \"features\": { \"onlineStore\": true, \"somethingNew\": 1 }, \"revoked\": [\"bad.pack\", \"abc123\"], \"somethingNew\": { \"a\": [1, 2] }"
            + Notices(Notice("hi", "Hello there", ", \"link\": \"https://heysip-app.github.io/news\"" + Versions("0.1.0", "0.9.0") + ", \"expires\": \"2027-01-01T00:00:00Z\", \"dismissible\": false, \"future\": true")));

        var file = ControlParser.Parse(Bytes(json), ControlLocation.IsOwnLink)!;

        Assert.Equal(new Version(0, 3, 0), file.LatestVersion);
        Assert.Equal(new Version(0, 1, 0), file.MinSupportedVersion);
        Assert.Equal(Releases, file.UpdateUrl!.AbsoluteUri);
        Assert.True(file.OnlineStore);
        Assert.Equal(["bad.pack", "abc123"], file.Revoked);
        Assert.Equal(2, file.Catalog.Count);
        Assert.Equal([new Uri("https://api.example/v1/catalog.json"), new Uri("https://mirror.example/v1/catalog.json")], file.Catalog[0].Urls);
        Assert.Equal(new Version(1, 0, 0), file.Catalog[1].Min);
        Assert.Null(file.Catalog[1].Max);
        var notice = Assert.Single(file.Notices);
        Assert.Equal("hi", notice.Id);
        Assert.False(notice.Dismissible);
        Assert.Equal(new Version(0, 1, 0), notice.MinVersion);
        Assert.Equal(new Version(0, 9, 0), notice.MaxVersion);
        Assert.Equal("https://heysip-app.github.io/news", notice.Link!.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "formatVersion": 2, "issuedAt": "2026-11-04T00:00:00Z" }""")]
    [InlineData("""{ "formatVersion": "1", "issuedAt": "2026-11-04T00:00:00Z" }""")]
    [InlineData("""{ "formatVersion": 1 }""")]
    [InlineData("""{ "formatVersion": 1, "issuedAt": "yesterday" }""")]
    public void A_file_that_is_not_a_control_file_gives_nothing_and_never_throws(string json) =>
        Assert.Null(ControlParser.Parse(Bytes(json), AllowAll));

    [Fact]
    public void A_file_over_the_size_limit_is_refused()
    {
        var padding = new string('x', ControlParser.MaxBytes);
        Assert.Null(ControlParser.Parse(Bytes(Json(extra: $", \"pad\": \"{padding}\"")), AllowAll));
    }

    [Fact]
    public void A_file_with_only_the_basics_means_no_online_list_and_the_list_not_switched_off()
    {
        var file = ControlParser.Parse(Bytes(Json()), AllowAll)!;

        Assert.Empty(file.Catalog);
        Assert.True(file.OnlineStore);
        Assert.Null(file.LatestVersion);
        Assert.Empty(file.Notices);
    }

    [Fact]
    public void The_store_can_be_switched_off_for_everyone()
    {
        Assert.False(ControlParser.Parse(Bytes(Json(extra: ", \"features\": { \"onlineStore\": false }")), AllowAll)!.OnlineStore);
        Assert.True(ControlParser.Parse(Bytes(Json(extra: ", \"features\": { \"onlineStore\": \"no\" }")), AllowAll)!.OnlineStore); // only a plain false counts
    }

    [Fact]
    public void Bad_catalog_addresses_and_rules_are_skipped_and_the_rest_kept()
    {
        var rules = Api(
            Rule(null, null, "http://insecure.example/catalog.json", "ftp://files.example/catalog.json", "javascript:alert(1)", "not a url"),
            Rule("0.1.0", null, "https://user:pass@api.example/catalog.json", "http://localhost:8080/catalog.json", "https://api.example/ok.json"));
        var file = ControlParser.Parse(Bytes(Json(extra: rules)), AllowAll)!;

        var rule = Assert.Single(file.Catalog);
        Assert.Equal([new Uri("http://localhost:8080/catalog.json"), new Uri("https://api.example/ok.json")], rule.Urls);
    }

    [Fact]
    public void Rules_and_addresses_are_capped()
    {
        var many = Enumerable.Range(0, 15).Select(i => Rule(null, null, $"https://api{i}.example/c.json")).ToArray();
        var file = ControlParser.Parse(Bytes(Json(extra: Api(many))), AllowAll)!;
        Assert.Equal(ControlParser.MaxCatalogRules, file.Catalog.Count);

        var addresses = Enumerable.Range(0, 12).Select(i => $"https://host{i}.example/c.json").ToArray();
        Assert.Equal(ControlParser.MaxUrlsPerRule, ControlParser.Parse(Bytes(Json(extra: Api(Rule(null, null, addresses)))), AllowAll)!.Catalog[0].Urls.Count);
    }

    [Theory]
    [InlineData("0.0.9", false)]
    [InlineData("0.1.0", true)]
    [InlineData("0.5.3", true)]
    [InlineData("0.9.9", true)]
    [InlineData("1.0.0", false)]
    public void A_rule_covers_the_app_versions_between_its_ends(string app, bool covered) =>
        Assert.Equal(covered, new CatalogRule(new Version(0, 1, 0), new Version(0, 9, 9), [Address]).Covers(Version.Parse(app)));

    [Theory]
    [InlineData("https://github.com/heysip-app/heysip/releases", true)]
    [InlineData("https://heysip-app.github.io/heysip-config/", true)]
    [InlineData("http://github.com/heysip-app/heysip", false)]
    [InlineData("https://github.com/someone-else/heysip", false)]
    [InlineData("https://github.com/heysip-apps/heysip", false)]
    [InlineData("https://evil.example/heysip-app/", false)]
    [InlineData("https://heysip-app.github.io.evil.example/", false)]
    [InlineData("https://user:pass@heysip-app.github.io/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void A_notice_may_only_link_to_the_projects_own_pages(string link, bool kept)
    {
        var json = Json(extra: Notices(Notice("n", "Hello", $", \"link\": \"{link}\"")));

        var notice = Assert.Single(ControlParser.Parse(Bytes(json), ControlLocation.IsOwnLink)!.Notices);

        Assert.Equal(kept, notice.Link is not null); // the message stays; only an untrusted link is dropped
    }

    [Fact]
    public void The_trusted_links_come_from_the_one_built_in_address_and_nothing_else()
    {
        var rule = ControlLocation.LinkRule([new Uri("https://news.example.org/config/control.json")]);

        Assert.True(rule(new Uri("https://news.example.org/anything")));
        Assert.False(rule(new Uri("https://heysip-app.github.io/news"))); // not this build's address
        Assert.False(rule(new Uri("https://github.com/news/heysip"))); // GitHub is trusted only for a GitHub Pages address

        var pages = ControlLocation.LinkRule([new Uri("https://acme.github.io/config/control.json")]);
        Assert.True(pages(new Uri("https://github.com/acme/app/releases")));
        Assert.False(pages(new Uri("https://github.com/heysip-app/app")));
    }

    [Fact]
    public void The_one_built_in_address_is_the_only_address_in_the_app()
    {
        Assert.Single(ControlLocation.Addresses);
        Assert.True(Uri.TryCreate(ControlLocation.Addresses[0], UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
        Assert.Equal(2, ControlLocation.PublicKeys.Length);
    }

    [Fact]
    public void Bad_notices_are_skipped_and_the_rest_are_kept_up_to_the_limit()
    {
        var many = Enumerable.Range(0, 15).Select(i => Notice($"n{i}")).ToList();
        var json = Json(extra: Notices([
            Notice("good"),
            Notice("good", "A repeated id"),
            Notice("has space"),
            Notice("empty", ""),
            Notice("long", new string('x', ControlParser.MaxTextLength + 1)),
            Notice("bad-date", "Hello", ", \"expires\": \"someday\""),
            """ "just a string" """,
            .. many]));

        var file = ControlParser.Parse(Bytes(json), AllowAll)!;

        Assert.Equal(ControlParser.MaxNotices, file.Notices.Count);
        Assert.Equal("good", file.Notices[0].Id);
        Assert.Equal("Hello", file.Notices[0].Text);
        Assert.DoesNotContain(file.Notices, notice => notice.Id is "has space" or "empty" or "long" or "bad-date");
    }

    // --- checking and keeping ---

    [Fact]
    public async Task A_good_signed_file_is_used_kept_on_disk_and_next_checked_in_a_day()
    {
        var stub = new Stub();
        var json = Json(extra: App("0.3.0", download: Releases));
        Serve(stub, Address, json, Signature(json, _main));
        var service = Service(stub);
        var changed = 0;
        service.Changed += () => changed++;

        Assert.True(await service.CheckAsync(Ct));

        Assert.Equal(new Version(0, 3, 0), service.Current!.LatestVersion);
        Assert.True(service.UpdateAvailable);
        Assert.Equal(_core.Time.GetUtcNow() + ControlService.CheckEvery, service.NextCheckAt);
        Assert.Equal(0, service.ConsecutiveFailures);
        Assert.Equal(1, changed);
        Assert.Equal([Address, new Uri(Address, "control.sig")], stub.Requests);

        // A new run of the app starts from the kept copy, with no internet at all.
        var offline = new Stub();
        var restarted = Service(offline);
        restarted.Load();
        Assert.Equal(new Version(0, 3, 0), restarted.Current!.LatestVersion);
        Assert.Empty(offline.Requests);
    }

    [Fact]
    public async Task A_file_that_is_unsigned_tampered_or_signed_by_a_stranger_is_ignored()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var real = Json(extra: App("0.3.0"));
        var forged = Json(extra: App("9.9.9"));
        var cases = new (string Json, string? Signature)[]
        {
            (real, null),
            (forged, Signature(real, _main)),
            (forged, Signature(forged, stranger)),
            (real, "garbage"),
        };

        foreach (var (body, signature) in cases)
        {
            var stub = new Stub();
            Serve(stub, Address, body, signature ?? "");
            var service = Service(stub, data: new TempData());

            Assert.False(await service.CheckAsync(Ct));

            Assert.Null(service.Current);
            Assert.Equal(1, service.ConsecutiveFailures);
        }
    }

    [Fact]
    public async Task A_missing_server_a_404_a_web_page_and_a_dropped_connection_are_quiet_failures()
    {
        var stub = new Stub { Override = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Not found</html>") } };
        var service = Service(stub);
        Assert.False(await service.CheckAsync(Ct));

        stub.Override = _ => throw new HttpRequestException("No such host is known");
        Assert.False(await service.CheckAsync(Ct));

        stub.Override = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.False(await service.CheckAsync(Ct));

        Assert.Null(service.Current);
        Assert.Equal(3, service.ConsecutiveFailures);
    }

    [Fact]
    public async Task Failures_back_off_and_a_success_starts_the_schedule_over()
    {
        var stub = new Stub();
        var service = Service(stub);
        var waits = new List<TimeSpan>();
        for (var i = 0; i < 7; i++)
        {
            await service.CheckAsync(Ct);
            waits.Add(service.NextCheckAt!.Value - _core.Time.GetUtcNow());
        }

        Assert.Equal([1, 6, 24, 72, 168, 168, 168], waits.Select(wait => (int)wait.TotalHours));

        var json = Json();
        Serve(stub, Address, json, Signature(json, _main));
        Assert.True(await service.CheckAsync(Ct));

        Assert.Equal(0, service.ConsecutiveFailures);
        Assert.Equal(ControlService.CheckEvery, service.NextCheckAt - _core.Time.GetUtcNow());
    }

    [Fact]
    public async Task The_schedule_decides_when_a_check_is_due_and_survives_a_restart()
    {
        var stub = new Stub();
        var service = Service(stub);
        Assert.True(service.IsDue); // never checked

        await service.CheckAsync(Ct); // fails: next in an hour
        Assert.False(service.IsDue);
        _core.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.False(service.IsDue);

        var restarted = Service(stub);
        restarted.Load();
        Assert.False(restarted.IsDue);

        _core.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(service.IsDue);
        Assert.True(restarted.IsDue);
    }

    [Fact]
    public async Task With_online_lookups_off_nothing_is_contacted_at_all()
    {
        var stub = new Stub();
        var json = Json();
        Serve(stub, Address, json, Signature(json, _main));
        var on = false;
        var service = Service(stub, () => on);

        Assert.False(service.IsDue);
        Assert.False(await service.CheckAsync(Ct));
        Assert.Empty(stub.Requests);

        on = true;
        Assert.True(service.IsDue);
        Assert.True(await service.CheckAsync(Ct));
    }

    [Fact]
    public async Task The_second_address_is_used_when_the_first_gives_nothing()
    {
        var stub = new Stub();
        var json = Json(extra: App("0.4.0"));
        Serve(stub, Second, json, Signature(json, _backup)); // also shows the backup key is accepted
        var service = Service(stub, options: Options(Address, Second));

        Assert.True(await service.CheckAsync(Ct));

        Assert.Equal(new Version(0, 4, 0), service.Current!.LatestVersion);
        Assert.Equal(0, service.ConsecutiveFailures);
    }

    [Fact]
    public async Task An_older_signed_file_never_replaces_a_newer_one()
    {
        var stub = new Stub();
        var newer = Json("2026-11-04T00:00:00Z", App("0.5.0"));
        Serve(stub, Address, newer, Signature(newer, _main));
        var service = Service(stub);
        await service.CheckAsync(Ct);

        var older = Json("2026-10-01T00:00:00Z", App("0.2.0"));
        Serve(stub, Address, older, Signature(older, _main)); // a stale cache, or somebody replaying an old file
        Assert.True(await service.CheckAsync(Ct));

        Assert.Equal(new Version(0, 5, 0), service.Current!.LatestVersion);
        var restarted = Service(new Stub());
        restarted.Load();
        Assert.Equal(new Version(0, 5, 0), restarted.Current!.LatestVersion);
    }

    [Fact]
    public async Task A_kept_copy_that_was_changed_on_disk_is_not_trusted()
    {
        var stub = new Stub();
        var json = Json(extra: App("0.3.0"));
        Serve(stub, Address, json, Signature(json, _main));
        await Service(stub).CheckAsync(Ct);

        var kept = Path.Combine(_core.Data.Location.RootDirectory, "control.json");
        File.WriteAllText(kept, Json(extra: App("9.9.9")));
        var restarted = Service(new Stub());
        restarted.Load();

        Assert.Null(restarted.Current);
    }

    [Fact]
    public void A_broken_state_or_kept_file_just_starts_over()
    {
        File.WriteAllText(Path.Combine(_core.Data.Location.RootDirectory, "control.json"), "{");
        File.WriteAllText(Path.Combine(_core.Data.Location.RootDirectory, "control.sig"), "x");
        File.WriteAllText(Path.Combine(_core.Data.Location.RootDirectory, "control-state.json"), "not json");
        var service = Service(new Stub());

        service.Load();

        Assert.Null(service.Current);
        Assert.True(service.IsDue);
    }

    [Fact]
    public async Task The_background_loop_checks_when_due_and_stops_when_told()
    {
        var stub = new Stub();
        var json = Json();
        Serve(stub, Address, json, Signature(json, _main));
        var service = Service(stub);
        using var stop = new CancellationTokenSource();
        var waits = 0;

        await service.RunAsync(stop.Token, (_, _) =>
        {
            if (++waits == 3)
            {
                stop.Cancel();
            }

            return Task.CompletedTask;
        });

        Assert.Equal(2, stub.Requests.Count); // one check (file and signature); the second pass found nothing due
        Assert.NotNull(service.Current);
    }

    // --- what the user is told ---

    [Fact]
    public async Task Notices_follow_the_version_the_expiry_and_what_was_dismissed()
    {
        var stub = new Stub();
        var json = Json(extra: Notices(
            Notice("everyone", "For everyone"),
            Notice("new-only", "Newer than 0.2", Versions("0.2.0", null)),
            Notice("old-only", "Older than 0.2", Versions(null, "0.1.9")),
            Notice("expired", "Gone", ", \"expires\": \"2026-11-01T00:00:00Z\""),
            Notice("later", "Still on", ", \"expires\": \"2026-12-01T00:00:00Z\"")));
        Serve(stub, Address, json, Signature(json, _main));
        var service = Service(stub, app: new Version(0, 1, 0));
        await service.CheckAsync(Ct);

        Assert.Equal(["everyone", "old-only", "later"], service.ActiveNotices().Select(notice => notice.Id));

        service.Dismiss("everyone");
        Assert.Equal(["old-only", "later"], service.ActiveNotices().Select(notice => notice.Id));

        _core.Time.Advance(TimeSpan.FromDays(30));
        Assert.Equal(["old-only"], service.ActiveNotices().Select(notice => notice.Id));

        // Dismissed notices stay dismissed after a restart.
        var restarted = Service(new Stub());
        restarted.Load();
        Assert.DoesNotContain(restarted.ActiveNotices(), notice => notice.Id == "everyone");
    }

    [Fact]
    public async Task An_update_notice_appears_only_with_somewhere_to_get_it_and_can_be_dismissed()
    {
        var stub = new Stub();
        var withLink = Json(extra: App("0.3.0", download: Releases));
        Serve(stub, Address, withLink, Signature(withLink, _main));
        var service = Service(stub);
        await service.CheckAsync(Ct);

        var update = Assert.Single(service.ActiveNotices());
        Assert.Equal("update-0.3.0", update.Id);
        Assert.Contains("0.3.0", update.Text);
        Assert.NotNull(update.Link);

        service.Dismiss("update-0.3.0");
        Assert.Empty(service.ActiveNotices());

        var noLink = Json("2026-11-05T00:00:00Z", App("0.4.0"));
        Serve(stub, Address, noLink, Signature(noLink, _main));
        await service.CheckAsync(Ct);
        Assert.Empty(service.ActiveNotices());
        Assert.False(service.UpdateAvailable);
    }

    [Fact]
    public async Task A_version_below_the_minimum_gets_a_notice_that_cannot_be_dismissed()
    {
        var stub = new Stub();
        var json = Json(extra: App("0.3.0", "0.2.0", Releases));
        Serve(stub, Address, json, Signature(json, _main));
        var service = Service(stub, app: new Version(0, 1, 0));
        await service.CheckAsync(Ct);

        Assert.True(service.BelowMinimum);
        Assert.Equal(["unsupported", "update-0.3.0"], service.ActiveNotices().Select(notice => notice.Id));

        service.Dismiss("unsupported");
        Assert.Equal("unsupported", service.ActiveNotices()[0].Id);

        var current = Service(stub, app: new Version(0, 2, 0), data: new TempData());
        await current.CheckAsync(Ct);
        Assert.False(current.BelowMinimum);
    }

    // --- where the online list is ---

    [Fact]
    public async Task The_address_of_the_list_depends_on_the_app_version_and_the_store_can_be_switched_off()
    {
        var stub = new Stub();
        var json = Json(extra: Api(
                Rule("0.1.0", "0.9.9", "https://api.example/v1/catalog.json", "https://mirror.example/v1/catalog.json"),
                Rule("1.0.0", null, "https://api.example/v2/catalog.json"))
            + ", \"features\": { \"onlineStore\": false }");
        Serve(stub, Address, json, Signature(json, _main));
        var older = Service(stub, app: new Version(0, 5, 0));
        var newer = Service(stub, app: new Version(1, 2, 0), data: new TempData());
        var tooOld = Service(stub, app: new Version(0, 0, 1), data: new TempData());

        Assert.Empty(older.CatalogUrls()); // nothing read yet: no list, and not switched off
        Assert.True(older.OnlineStoreEnabled);

        await older.CheckAsync(Ct);
        await newer.CheckAsync(Ct);
        await tooOld.CheckAsync(Ct);

        Assert.Equal([new Uri("https://api.example/v1/catalog.json"), new Uri("https://mirror.example/v1/catalog.json")], older.CatalogUrls());
        Assert.Equal([new Uri("https://api.example/v2/catalog.json")], newer.CatalogUrls());
        Assert.Empty(tooOld.CatalogUrls());
        Assert.False(older.OnlineStoreEnabled);
    }

    private const string Catalog = """
        { "formatVersion": 1, "items": [
          { "id": "alice.hat", "kind": "accessory", "name": "Hat", "version": "1.0.0", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          { "id": "bob.cap", "kind": "accessory", "name": "Cap", "version": "1.0.0", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
          { "id": "carol.bow", "kind": "accessory", "name": "Bow", "version": "1.0.0", "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" } ] }
        """;

    /// <summary>A control service that has been told where the list is, and a stub that serves a signed list there.</summary>
    private async Task<(ControlService Control, Stub Stub)> WithList(string rules, string extra = "", string? catalogSignature = "main")
    {
        var stub = new Stub();
        var json = Json(extra: Api(rules) + extra);
        Serve(stub, Address, json, Signature(json, _main));
        stub.Files["api.example/v1/catalog.json"] = Bytes(Catalog);
        if (catalogSignature is not null)
        {
            stub.Files["api.example/v1/catalog.sig"] = Encoding.ASCII.GetBytes(Signature(Catalog, catalogSignature == "main" ? _main : _backup));
        }

        var control = Service(stub);
        await control.CheckAsync(Ct);
        return (control, stub);
    }

    [Fact]
    public async Task With_no_address_in_the_control_file_the_store_adds_nothing_and_says_nothing()
    {
        var stub = new Stub();
        using var source = new ControlledStoreSource(Service(stub), "HeySip/0.1", _core.Time, stub);

        var result = await source.LoadAsync(Ct);

        Assert.True(result.Ok);
        Assert.Empty(result.Entries);
        Assert.Null(result.Notice);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task The_store_follows_the_address_in_the_control_file_and_hides_revoked_packs()
    {
        var (control, stub) = await WithList(
            Rule(null, null, "https://api.example/v1/catalog.json"),
            ", \"revoked\": [\"alice.hat\", \"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\"]");
        using var source = new ControlledStoreSource(control, "HeySip/0.1", _core.Time, stub);

        var result = await source.LoadAsync(Ct);

        Assert.Equal(["carol.bow"], result.Entries.Select(entry => entry.Id));
        var revoked = new StoreEntry("alice.hat", PackKind.Accessory, "Hat", "", "1.0.0", "", 0, "x.heysippack", null, 0, null);
        await Assert.ThrowsAsync<IOException>(() => source.FetchPackAsync(revoked, Ct));
    }

    [Fact]
    public async Task A_list_must_be_signed_by_a_trusted_key_or_it_is_refused()
    {
        foreach (var signature in new string?[] { null, "stranger" })
        {
            var (control, stub) = await WithList(Rule(null, null, "https://api.example/v1/catalog.json"), catalogSignature: signature);
            if (signature == "stranger")
            {
                using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                stub.Files["api.example/v1/catalog.sig"] = Encoding.ASCII.GetBytes(Signature(Catalog, stranger));
            }

            using var source = new ControlledStoreSource(control, "HeySip/0.1", _core.Time, stub);
            var result = await source.LoadAsync(Ct);

            Assert.False(result.Ok);
            Assert.Empty(result.Entries);
        }

        // Changing one letter of a signed list breaks it.
        var (tamperedControl, tamperedStub) = await WithList(Rule(null, null, "https://api.example/v1/catalog.json"));
        tamperedStub.Files["api.example/v1/catalog.json"] = Bytes(Catalog.Replace("Hat", "Hit"));
        using var tampered = new ControlledStoreSource(tamperedControl, "HeySip/0.1", _core.Time, tamperedStub);
        Assert.False((await tampered.LoadAsync(Ct)).Ok);

        // The backup key counts too.
        var (backupControl, backupStub) = await WithList(Rule(null, null, "https://api.example/v1/catalog.json"), catalogSignature: "backup");
        using var viaBackup = new ControlledStoreSource(backupControl, "HeySip/0.1", _core.Time, backupStub);
        Assert.True((await viaBackup.LoadAsync(Ct)).Ok);
    }

    [Fact]
    public async Task The_next_address_is_tried_when_the_first_cannot_give_a_good_list()
    {
        var (control, stub) = await WithList(Rule(null, null, "https://dead.example/catalog.json", "https://api.example/v1/catalog.json"));
        using var source = new ControlledStoreSource(control, "HeySip/0.1", _core.Time, stub);

        var result = await source.LoadAsync(Ct);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Entries.Count);
        Assert.Contains(stub.Requests, url => url.Host == "dead.example");
    }

    [Fact]
    public async Task A_store_that_cannot_be_reached_fails_fast_for_a_few_minutes_then_tries_again()
    {
        var (control, stub) = await WithList(Rule(null, null, "https://api.example/v1/catalog.json"));
        stub.Files["api.example/v1/catalog.json"] = null; // the list goes missing
        using var source = new ControlledStoreSource(control, "HeySip/0.1", _core.Time, stub);
        var before = stub.Requests.Count;

        Assert.False((await source.LoadAsync(Ct)).Ok);
        var after = stub.Requests.Count;
        Assert.True(after > before);

        Assert.False((await source.LoadAsync(Ct)).Ok);
        Assert.Equal(after, stub.Requests.Count); // no second request: nothing waited for

        stub.Files["api.example/v1/catalog.json"] = Bytes(Catalog);
        _core.Time.Advance(ControlledStoreSource.RetryAfterFailure + TimeSpan.FromSeconds(1));
        var result = await source.LoadAsync(Ct);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Entries.Count);
    }

    [Fact]
    public async Task A_list_on_this_computer_needs_no_signature_so_a_server_can_be_tried_out()
    {
        var stub = new Stub();
        stub.Files["localhost/catalog.json"] = Bytes(Catalog);
        var json = Json(extra: Api(Rule(null, null, "http://localhost:8080/catalog.json")));
        Serve(stub, Address, json, Signature(json, _main));
        var control = Service(stub);
        await control.CheckAsync(Ct);
        using var source = new ControlledStoreSource(control, "HeySip/0.1", _core.Time, stub);

        Assert.Equal(3, (await source.LoadAsync(Ct)).Entries.Count);
    }
}
