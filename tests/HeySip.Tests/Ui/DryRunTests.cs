using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using HeySip.App.Features.Browse;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Control;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

/// <summary>
/// A rehearsal against a real web server. Set HEYSIP_DRYRUN_URL to the address of a catalog.json being served (for example
/// by <c>python -m http.server</c> on this machine); nothing happens when it isn't set. The app is built the way the real
/// one is, and told the address the only way it can be: by a signed control file naming it.
/// </summary>
public sealed class DryRunTests : IDisposable
{
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

    public void Dispose() => CharacterCatalog.Configure(null);

    /// <summary>
    /// The app is told where the list is the only way it can be: by a signed control file. This one is kept on disk, signed with a key made
    /// for the rehearsal, and names the address (a list on this computer needs no signature of its own).
    /// </summary>
    private static UiHarness Start(string url, FakeLauncher launcher)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = new TempData();
        var json = $$"""
            { "formatVersion": 1, "issuedAt": "2026-11-04T00:00:00Z", "api": { "catalog": [ { "urls": ["{{url}}"] } ] } }
            """;
        File.WriteAllText(Path.Combine(data.Location.RootDirectory, "control.json"), json);
        File.WriteAllText(Path.Combine(data.Location.RootDirectory, "control.sig"), ControlVerifier.Sign(Encoding.UTF8.GetBytes(json), key));
        var options = new ControlOptions([new Uri("https://example.invalid/control.json")], [Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())], _ => false);
        var control = new ControlService(options, data.Location, () => true, new Version(0, 1, 0), TimeProvider.System, "HeySip/0.1");
        control.Load();

        var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<IShellLauncher>(launcher);
            services.AddSingleton(control);
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        return ui;
    }

    private static StoreCardViewModel Card(UiHarness ui, string id) => ui.Shell.Browse.Cards.Single(card => card.Entry.Id == id);

    [AvaloniaFact]
    public async Task Rehearse_against_a_served_list()
    {
        var url = Environment.GetEnvironmentVariable("HEYSIP_DRYRUN_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var launcher = new FakeLauncher();
        using var ui = Start(url, launcher);
        var browse = ui.Shell.Browse;

        // 1. The list arrives: built-ins, plus what only the server has, with no notice.
        await browse.RefreshAsync();
        Assert.True(string.IsNullOrEmpty(browse.ErrorText), browse.ErrorText);
        Assert.True(string.IsNullOrEmpty(browse.Notice), browse.Notice);
        Assert.Equal(StoreStatus.Available, Card(ui, "heysip-studio.orca").Status);
        Assert.Equal("1.1.0", Card(ui, "heysip-studio.cactus").Entry.Version); // online wins over the bundled 1.0.0
        Assert.DoesNotContain(browse.Cards, card => card.Entry.Id == "heysip-studio.broken");
        Assert.Contains(browse.Cards, card => card.Entry.Id == "heysip.drip");

        // 2. Get a character that only exists on the server, through the install card.
        var orca = Card(ui, "heysip-studio.orca");
        orca.ActCommand.Execute(null);
        await WaitFor(() => ui.Shell.InstallPreview.IsVisible || !string.IsNullOrEmpty(orca.Message));
        Assert.True(ui.Shell.InstallPreview.IsVisible && ui.Shell.InstallPreview.IsPreview, orca.Message);
        Assert.Equal("Orca", ui.Shell.InstallPreview.Title);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        Assert.Equal(StoreStatus.Installed, orca.Status);
        Assert.True(CharacterCatalog.Contains("heysip-studio.orca"));

        // 3. The newer Cactus comes from the server too, and installs as 1.1.0.
        var cactus = Card(ui, "heysip-studio.cactus");
        cactus.ActCommand.Execute(null);
        await WaitFor(() => ui.Shell.InstallPreview.IsVisible || !string.IsNullOrEmpty(cactus.Message));
        Assert.True(ui.Shell.InstallPreview.IsVisible && ui.Shell.InstallPreview.IsPreview, cactus.Message);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        Assert.Equal(StoreStatus.Installed, cactus.Status);

        // 4. A pack that isn't what its list entry promised is refused with a message, and nothing is installed.
        var liar = Card(ui, "heysip-studio.liar");
        liar.ActCommand.Execute(null);
        await WaitFor(() => !string.IsNullOrEmpty(liar.Message));
        Assert.Contains("wasn't what the list promised", liar.Message);
        Assert.False(CharacterCatalog.Contains("heysip-studio.liar"));
        Assert.False(ui.Shell.InstallPreview.IsVisible);

        // 5. A paid item offers Buy, which opens its page and downloads nothing.
        var deluxe = Card(ui, "heysip-studio.deluxe");
        Assert.StartsWith("Buy", deluxe.ActionLabel);
        Assert.Contains("4.99", deluxe.ActionLabel);
        deluxe.ActCommand.Execute(null);
        Assert.Equal(["https://example.com/buy"], launcher.Opened);

        // 6. Switching the lookup off: the server's items go, the built-ins stay, the page says why.
        ui.Get<SettingsService>().Update(s => s with { OnlineCharacters = false });
        await browse.RefreshAsync();
        Assert.Contains("turned off", browse.Notice);
        Assert.DoesNotContain(browse.Cards, card => card.Entry.Id == "heysip-studio.orca" && card.Status == StoreStatus.Available);
        Assert.Contains(browse.Cards, card => card.Entry.Id == "heysip.drip");
    }

    [AvaloniaFact]
    public async Task Rehearse_against_nothing_listening()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEYSIP_DRYRUN_URL")))
        {
            return;
        }

        using var ui = Start("http://127.0.0.1:1/catalog.json", new FakeLauncher());
        await ui.Shell.Browse.RefreshAsync();

        Assert.True(string.IsNullOrEmpty(ui.Shell.Browse.ErrorText), ui.Shell.Browse.ErrorText);
        Assert.Contains("couldn't be reached", ui.Shell.Browse.Notice);
        Assert.Contains(ui.Shell.Browse.Cards, card => card.Entry.Id == "heysip.drip");
        Assert.Contains(ui.Shell.Browse.Cards, card => card.Entry.Id == "heysip-studio.whale");
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
