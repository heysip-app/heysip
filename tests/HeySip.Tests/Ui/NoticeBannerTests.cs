using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using HeySip.App.Features.Control;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Control;
using HeySip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class NoticeBannerTests : IDisposable
{
    private readonly TempData _data = new();
    private readonly CoreHarness _core = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _key.Dispose();
        _core.Dispose();
        _data.Dispose();
    }

    private sealed class FakeLauncher : IShellLauncher
    {
        public List<string> Opened { get; } = [];

        public bool OpenFolder(string path) => true;

        public bool OpenUrl(string url)
        {
            Opened.Add(url);
            return true;
        }
    }

    /// <summary>A control service that starts from a signed copy kept on disk, as it would after a real check.</summary>
    private ControlService KeptCopy(string json, Version app)
    {
        File.WriteAllText(Path.Combine(_data.Location.RootDirectory, "control.json"), json);
        File.WriteAllText(Path.Combine(_data.Location.RootDirectory, "control.sig"), ControlVerifier.Sign(Encoding.UTF8.GetBytes(json), _key));
        var options = new ControlOptions([new Uri("https://heysip-app.github.io/heysip-config/v1/control.json")], [Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())], ControlLocation.IsOwnLink);
        var service = new ControlService(options, _data.Location, () => true, app, _core.Time, "HeySip/0.1");
        service.Load();
        return service;
    }

    private const string Update = """
        { "formatVersion": 1, "issuedAt": "2026-11-04T00:00:00Z",
          "app": { "latestVersion": "0.3.0", "downloadUrl": "https://github.com/heysip-app/heysip/releases" },
          "notices": [ { "id": "welcome", "text": "Thanks for trying HeySip.", "link": "https://heysip-app.github.io/news" } ] }
        """;

    [AvaloniaFact]
    public void The_bar_shows_the_most_important_message_opens_its_link_and_can_be_dismissed()
    {
        var launcher = new FakeLauncher();
        var banner = new NoticeBannerViewModel(KeptCopy(Update, new Version(0, 1, 0)), launcher);

        Assert.True(banner.IsVisible);
        Assert.Equal("HeySip 0.3.0 is available.", banner.Text);
        Assert.True(banner.HasLink);
        Assert.Equal("Get it", banner.LinkText);
        Assert.True(banner.CanDismiss);

        banner.OpenCommand.Execute(null);
        Assert.Equal(["https://github.com/heysip-app/heysip/releases"], launcher.Opened);

        banner.DismissCommand.Execute(null);
        Assert.Equal("Thanks for trying HeySip.", banner.Text);
        Assert.Equal("Learn more", banner.LinkText);

        banner.DismissCommand.Execute(null);
        Assert.False(banner.IsVisible);
        Assert.Empty(banner.Text);
        banner.OpenCommand.Execute(null);
        Assert.Single(launcher.Opened);
    }

    [AvaloniaFact]
    public void Nothing_shows_when_there_is_no_message()
    {
        var banner = new NoticeBannerViewModel(KeptCopy(Update, new Version(0, 3, 0)), new FakeLauncher());
        banner.DismissCommand.Execute(null);

        Assert.False(banner.IsVisible);

        var none = new NoticeBannerViewModel(new ControlService(new ControlOptions([], [], _ => true), new TempData().Location, () => true, new Version(0, 1, 0), _core.Time, "HeySip/0.1"), new FakeLauncher());
        Assert.False(none.IsVisible);
    }

    [AvaloniaFact]
    public void A_message_that_cannot_be_dismissed_has_no_dismiss_button()
    {
        const string Unsupported = """
            { "formatVersion": 1, "issuedAt": "2026-11-04T00:00:00Z",
              "app": { "latestVersion": "0.3.0", "minSupportedVersion": "0.2.0", "downloadUrl": "https://github.com/heysip-app/heysip/releases" } }
            """;
        var banner = new NoticeBannerViewModel(KeptCopy(Unsupported, new Version(0, 1, 0)), new FakeLauncher());

        Assert.True(banner.IsVisible);
        Assert.False(banner.CanDismiss);
        banner.DismissCommand.Execute(null);
        Assert.True(banner.IsVisible);
    }

    [AvaloniaFact]
    public void The_real_window_draws_the_bar_and_starts_with_none()
    {
        using var ui = UiHarness.Create();
        ui.Show();
        Assert.False(ui.Shell.NoticeBanner.IsVisible); // nothing has been checked in a test run, and no network is touched

        var control = KeptCopy(Update, new Version(0, 1, 0));
        using var withNotice = UiHarness.Create(services => services.AddSingleton(control));
        withNotice.Show();

        Assert.True(withNotice.Shell.NoticeBanner.IsVisible);
        Assert.Equal("HeySip 0.3.0 is available.", withNotice.Shell.NoticeBanner.Text);
    }
}
