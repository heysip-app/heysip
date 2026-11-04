using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using HeySip.App.Features.Browse;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class BrowseTests : IDisposable
{
    private sealed class FakeSource : IStoreSource
    {
        public List<StoreEntry> Entries { get; } = [];

        public Dictionary<string, byte[]> Packs { get; } = [];

        public string? ListError { get; set; }

        public string? Notice { get; set; }

        public bool FailDownloads { get; set; }

        public int Fetches { get; private set; }

        public Task<StoreCatalogResult> LoadAsync(CancellationToken cancel) =>
            Task.FromResult(ListError is { } error ? StoreCatalogResult.Failure(error) : new StoreCatalogResult(Entries.ToList(), 0, null, Notice));

        public Task<byte[]> FetchPackAsync(StoreEntry entry, CancellationToken cancel)
        {
            Fetches++;
            if (FailDownloads || !Packs.TryGetValue(entry.Id, out var bytes))
            {
                throw new IOException("offline");
            }

            return Task.FromResult(bytes);
        }
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

    private readonly FakeSource _source = new();
    private readonly FakeLauncher _launcher = new();

    public void Dispose() => CharacterCatalog.Configure(null);

    /// <param name="payments">Whether paid items can be bought. On here so the buy flow is tested; the app itself ships with it off.</param>
    private UiHarness Harness(bool fake = true, bool payments = true)
    {
        var ui = UiHarness.Create(services =>
        {
            services.AddSingleton(new StorePayments(payments));
            if (fake)
            {
                services.AddSingleton<IStoreSource>(_source);
            }

            services.AddSingleton<IShellLauncher>(_launcher);
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        return ui;
    }

    private static JsonArray Point(double x, double y) => new(x, y);

    private static JsonObject Character(string id = "tester.blob", string version = "1.0.0", string name = "Blob") => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "character",
        ["id"] = id,
        ["name"] = name,
        ["version"] = version,
        ["author"] = "Tester",
        ["palette"] = new JsonObject { ["light"] = "#C4B5FD", ["deep"] = "#7C3AED", ["outline"] = "#4C1D95" },
        ["anchors"] = new JsonObject
        {
            ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
            ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
            ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
            ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
        },
        ["body"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(50, 90, 42, 36), ["fill"] = "$light" }),
    };

    private static JsonObject Hat(string id = "tester.top-hat", string version = "1.0.0") => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "accessory",
        ["id"] = id,
        ["name"] = "Top hat",
        ["version"] = version,
        ["slot"] = "hat",
        ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-12,0 L0,-26 L12,0 Z", ["fill"] = "#22AA55" }),
    };

    private static byte[] Zip(JsonObject manifest)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var content = zip.CreateEntry("pack.json").Open())
        {
            content.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        return stream.ToArray();
    }

    /// <summary>Puts a pack on offer: its list entry, and the file the entry points to.</summary>
    private StoreEntry Offer(JsonObject manifest, int priceCents = 0, string? buyUrl = null, string? sha256 = null, string? withId = null, string[]? accessories = null)
    {
        var bytes = Zip(manifest);
        var id = withId ?? (string)manifest["id"]!;
        var entry = new StoreEntry(
            id, manifest["kind"]!.ToString() == "accessory" ? PackKind.Accessory : PackKind.Character,
            (string)manifest["name"]!, "Tester", (string)manifest["version"]!, "A pack.", priceCents,
            "packs/" + id + ".heysippack", sha256, bytes.Length, buyUrl, accessories);
        _source.Entries.Add(entry);
        _source.Packs[id] = bytes;
        return entry;
    }

    private static StoreCardViewModel Card(UiHarness ui, string id) => ui.Shell.Browse.Cards.Single(card => card.Entry.Id == id);

    private static async Task OpenBrowse(UiHarness ui)
    {
        ui.Shell.Navigate(HeySip.App.Shell.ShellPage.Browse);
        await ui.Shell.Browse.RefreshAsync();
    }

    // ---- the list that comes with the app

    [AvaloniaFact]
    public async Task The_built_in_list_shows_what_comes_with_the_app_as_included_and_the_rest_as_available()
    {
        using var ui = Harness(fake: false);

        await OpenBrowse(ui);

        var cards = ui.Shell.Browse.Cards;
        Assert.Equal(20, cards.Count);
        Assert.Equal(15, cards.Count(card => card.Status == StoreStatus.Included));
        Assert.Equal(
            ["heysip-studio.cactus", "heysip-studio.cactus-bandana", "heysip-studio.cactus-heart-glasses", "heysip-studio.cactus-sombrero", "heysip-studio.whale"],
            cards.Where(card => card.Status == StoreStatus.Available).Select(card => card.Entry.Id).Order());
        Assert.All(cards, card => Assert.True(card.HasPreview));
        Assert.Equal(8, cards.Count(card => card.IsCharacter));
        Assert.Equal(12, cards.Count(card => !card.IsCharacter)); // nine built in, and the three that come with the Cactus
        Assert.False(ui.Shell.Browse.IsEmpty);
    }

    [AvaloniaFact]
    public async Task Items_that_can_be_got_or_updated_come_first()
    {
        Offer(Character(id: "tester.zed", name: "Zed"));
        Offer(Character(id: "tester.able", name: "Able"));
        using var ui = Harness();
        _source.Entries.Insert(0, new StoreEntry("heysip.minty", PackKind.Character, "Minty", "HeySip", "1.0.0", "", 0, null, null, 0, null));

        await OpenBrowse(ui);

        Assert.Equal(["tester.zed", "tester.able", "heysip.minty"], ui.Shell.Browse.Cards.Select(card => card.Entry.Id));
    }

    // ---- when the online list goes away for good

    /// <summary>The app as it really is: the online list on top of the built-in one, and the built-in one when the online one is gone.</summary>
    private UiHarness HarnessWithOnlineList()
    {
        var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<IStoreSource>(new LayeredStoreSource(_source, new BundledStoreSource()));
            services.AddSingleton<IShellLauncher>(_launcher);
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        return ui;
    }

    [AvaloniaFact]
    public async Task A_new_user_after_the_online_list_is_gone_gets_the_built_in_list_and_nothing_from_the_server()
    {
        Offer(Character(id: "tester.blob", name: "Blob"));
        using var ui = HarnessWithOnlineList();
        _source.ListError = "The server is gone";

        await OpenBrowse(ui);

        Assert.True(string.IsNullOrEmpty(ui.Shell.Browse.ErrorText), ui.Shell.Browse.ErrorText);
        Assert.Contains("couldn't be reached", ui.Shell.Browse.Notice);
        Assert.Equal(20, ui.Shell.Browse.Cards.Count); // exactly what comes with the app
        Assert.DoesNotContain(ui.Shell.Browse.Cards, card => card.Entry.Id.StartsWith("tester.", StringComparison.Ordinal));
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public async Task What_someone_installed_stays_when_the_online_list_goes_away_and_only_they_can_remove_it()
    {
        Offer(Character(id: "tester.blob", name: "Blob"));
        Offer(Hat());
        using var ui = HarnessWithOnlineList();
        await OpenBrowse(ui);
        foreach (var id in new[] { "tester.blob", "tester.top-hat" })
        {
            Card(ui, id).ActCommand.Execute(null);
            await Task.Yield();
            Assert.True(ui.Shell.InstallPreview.IsPreview, Card(ui, id).Message);
            ui.Shell.InstallPreview.InstallCommand.Execute(null);
            ui.Shell.InstallPreview.CloseCommand.Execute(null);
        }

        ui.Get<SettingsService>().Update(s => (s with { Character = "tester.blob" }).WithAccessory(AccessorySlot.Hat, "tester.top-hat"));

        // The server goes away for good, and the app is opened again.
        _source.Entries.Clear();
        _source.Packs.Clear();
        _source.ListError = "The server is gone";
        await OpenBrowse(ui);
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory); // what starting the app again does

        Assert.True(Directory.Exists(Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob")));
        Assert.True(CharacterCatalog.Contains("tester.blob"));
        Assert.NotNull(CharacterCatalog.FindAccessory("tester.top-hat"));
        var settings = ui.Get<SettingsService>().Current;
        Assert.Equal("tester.blob", settings.Character);
        Assert.Equal("tester.top-hat", settings.Outfit[OutfitSlots.KeyOf(AccessorySlot.Hat)]);

        // The Characters page still shows them, as installed, even though the list doesn't mention them any more.
        var browse = ui.Shell.Browse;
        var blob = Card(ui, "tester.blob");
        Assert.Equal(StoreStatus.Installed, blob.Status);
        Assert.True(blob.IsInUse);
        Assert.Equal("Owned", blob.StateText);
        Assert.Equal("By Tester", blob.Byline);
        Assert.NotNull(blob.Pack); // drawn from the installed pack, so no download is needed for its picture
        Assert.Contains(blob, browse.Sections.Single(section => section.Title == "Installed").Groups.SelectMany(group => group.Cards));
        Assert.DoesNotContain(browse.Sections, section => section.Title == "Not installed" && section.Groups.SelectMany(group => group.Cards).Contains(blob));
        browse.ShowAccessoriesCommand.Execute(null);
        var hat = Card(ui, "tester.top-hat");
        Assert.Equal(StoreStatus.Installed, hat.Status);
        Assert.True(hat.IsInUse); // it is being worn
        Assert.Contains(hat, browse.Sections.Single(section => section.Title == "Installed").Groups.SelectMany(group => group.Cards));
        Assert.Equal(20, browse.Cards.Count - 2); // everything from the built-in list is still there too

        // Still listed in Settings, where the user can remove it; nothing removed it for them.
        Assert.Contains(ui.Shell.Settings.Installed, pack => pack.Id == "tester.blob");
        Assert.Contains(ui.Shell.Settings.Installed, pack => pack.Id == "tester.top-hat");
        ui.Shell.Settings.Installed.Single(pack => pack.Id == "tester.blob").RemoveCommand.Execute(null);
        Assert.False(CharacterCatalog.Contains("tester.blob"));
        Assert.True(CharacterCatalog.Contains("heysip.drip")); // the built-in ones can't go

        // Once the user removes it, its card goes too: there is nothing left to show.
        Assert.DoesNotContain(browse.Cards, card => card.Entry.Id == "tester.blob");
        Assert.Contains(browse.Cards, card => card.Entry.Id == "tester.top-hat");
    }

    [AvaloniaFact]
    public async Task An_installed_item_the_list_never_had_shows_as_installed_too_for_example_one_opened_from_a_file()
    {
        using var ui = HarnessWithOnlineList();
        var packs = ui.Data.Location.CharactersDirectory;
        Directory.CreateDirectory(packs);
        using (var stream = new MemoryStream(Zip(Character(id: "tester.fromfile", name: "From a file"))))
        {
            var installed = PackInstaller.Install(PackLoader.FromZip(stream, new PackLoadOptions(new Version(1, 0))), packs);
            Assert.True(installed.Ok, installed.Error);
        }

        CharacterCatalog.Configure(packs);

        await OpenBrowse(ui);

        var card = Card(ui, "tester.fromfile");
        Assert.Equal(StoreStatus.Installed, card.Status);
        Assert.Equal("Use it", card.ActionLabel);
        card.ActCommand.Execute(null);
        Assert.Equal("tester.fromfile", ui.Get<SettingsService>().Current.Character);
        Assert.Equal("In use", card.ActionLabel);
    }

    [AvaloniaFact]
    public async Task Every_item_on_offer_in_the_built_in_list_downloads_and_passes_every_check()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        foreach (var card in ui.Shell.Browse.Cards.Where(card => card.Status == StoreStatus.Available).ToList())
        {
            card.ActCommand.Execute(null);
            await Task.Yield();

            Assert.True(ui.Shell.InstallPreview.IsPreview, $"{card.Entry.Id}: {card.Message}");
            Assert.Equal(card.Entry.Name, ui.Shell.InstallPreview.Title);
            ui.Shell.InstallPreview.CloseCommand.Execute(null);
        }
    }

    [AvaloniaFact]
    public async Task The_cactus_from_the_built_in_list_can_be_got_installed_and_used()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);
        var cactus = Card(ui, "heysip-studio.cactus");
        Assert.Equal("By HeySip Studio", cactus.Byline);
        Assert.Equal("Get", cactus.ActionLabel);

        cactus.ActCommand.Execute(null);
        await Task.Yield();

        var install = ui.Shell.InstallPreview;
        Assert.True(install.IsPreview, cactus.Message);
        Assert.Equal("Cactus", install.Title);
        Assert.Equal("New character", install.Eyebrow);
        Assert.Equal(4, install.Tiles.Count);

        install.InstallCommand.Execute(null);
        install.CloseCommand.Execute(null);

        Assert.Equal(StoreStatus.Installed, cactus.Status);
        Assert.True(CharacterCatalog.Contains("heysip-studio.cactus"));
        cactus.ActCommand.Execute(null);
        Assert.Equal("heysip-studio.cactus", ui.Get<SettingsService>().Current.Character);
        Assert.Equal("In use", cactus.ActionLabel);
    }

    [AvaloniaFact]
    public async Task The_character_in_use_says_so_and_the_others_offer_to_be_used()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        var drip = Card(ui, "heysip.drip");
        var minty = Card(ui, "heysip.minty");

        Assert.Equal("In use", drip.ActionLabel);
        Assert.False(drip.CanAct);
        Assert.Equal("Use it", minty.ActionLabel);
        Assert.True(minty.CanAct);

        minty.ActCommand.Execute(null);

        Assert.Equal("heysip.minty", ui.Get<SettingsService>().Current.Character);
        Assert.Equal("In use", minty.ActionLabel);
        Assert.Equal("Use it", drip.ActionLabel);
    }

    [AvaloniaFact]
    public async Task An_accessory_can_be_worn_from_the_list()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        var crown = Card(ui, "heysip.crown");
        Assert.Equal("Wear it", crown.ActionLabel);

        crown.ActCommand.Execute(null);

        Assert.Equal("heysip.crown", ui.Get<SettingsService>().Current.Outfit["hat"]);
        Assert.Equal("In use", crown.ActionLabel);
    }

    [AvaloniaFact]
    public async Task The_characters_tab_shows_only_characters_in_installed_and_not_installed_sections()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        var browse = ui.Shell.Browse;
        Assert.True(browse.IsCharactersTab);
        Assert.Equal(["Installed", "Not installed"], browse.Sections.Select(section => section.Title));

        var installed = browse.Sections[0].Groups.SelectMany(group => group.Cards).ToList();
        var notInstalled = browse.Sections[1].Groups.SelectMany(group => group.Cards).ToList();
        Assert.Equal(6, installed.Count);
        Assert.All(installed, card => Assert.True(card.IsCharacter && card.IsOwned));
        Assert.Equal(["heysip-studio.cactus", "heysip-studio.whale"], notInstalled.Select(card => card.Entry.Id).Order());
        Assert.All(notInstalled, card => Assert.True(card.IsCharacter && !card.IsOwned));
        Assert.All(browse.Sections.SelectMany(section => section.Groups), group => Assert.False(group.HasTitle));
    }

    [AvaloniaFact]
    public async Task The_accessories_tab_shows_only_accessories_and_titles_a_slot_only_when_it_has_several_options()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        ui.Shell.Browse.ShowAccessoriesCommand.Execute(null);

        var browse = ui.Shell.Browse;
        Assert.True(browse.IsAccessoriesTab);
        Assert.Equal(["Installed", "Not installed"], browse.Sections.Select(section => section.Title));
        Assert.All(browse.Sections.SelectMany(section => section.Groups).SelectMany(group => group.Cards), card => Assert.False(card.IsCharacter));

        // The nine that come with the app. Every slot has two or three variants here, so each gets a heading.
        var installed = browse.Sections[0];
        Assert.Equal(9, installed.Groups.Sum(group => group.Cards.Count));
        Assert.Equal(["Hat", "Glasses", "Scarf and ties", "Holding"], installed.Groups.Select(group => group.Title));
        Assert.Equal([3, 2, 2, 2], installed.Groups.Select(group => group.Cards.Count));

        // The three that come with the Cactus aren't installed, and the list doesn't say their slot, so they sit in one plain group.
        var notInstalled = Assert.Single(browse.Sections[1].Groups);
        Assert.False(notInstalled.HasTitle);
        Assert.Equal(["heysip-studio.cactus-bandana", "heysip-studio.cactus-heart-glasses", "heysip-studio.cactus-sombrero"], notInstalled.Cards.Select(card => card.Entry.Id).Order());
        Assert.All(notInstalled.Cards, card => Assert.False(card.IsOwned));

        browse.ShowCharactersCommand.Execute(null);
        Assert.True(browse.IsCharactersTab);
    }

    [AvaloniaFact]
    public async Task An_installed_item_moves_from_not_installed_to_installed()
    {
        Offer(Character(id: "tester.blob", name: "Blob"));
        using var ui = Harness();
        await OpenBrowse(ui);

        var browse = ui.Shell.Browse;
        Assert.Contains(browse.Sections.Single(section => section.Title == "Not installed").Groups.SelectMany(group => group.Cards),
            card => card.Entry.Id == "tester.blob");

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.DoesNotContain(browse.Sections.Where(section => section.Title == "Not installed").SelectMany(section => section.Groups).SelectMany(group => group.Cards),
            card => card.Entry.Id == "tester.blob");
        Assert.Contains(browse.Sections.Single(section => section.Title == "Installed").Groups.SelectMany(group => group.Cards),
            card => card.Entry.Id == "tester.blob");
    }

    [AvaloniaFact]
    public async Task Opening_an_item_shows_its_page_with_pictures_and_going_back_shows_the_grid()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        Card(ui, "heysip.axolotl").OpenCommand.Execute(null);

        var browse = ui.Shell.Browse;
        Assert.True(browse.IsDetail);
        Assert.False(browse.IsList);
        Assert.Equal("Axolotl", browse.Selected!.Name);
        Assert.Equal(4, browse.Selected.Tiles.Count);
        Assert.Equal("By HeySip", browse.Selected.Byline);

        browse.BackCommand.Execute(null);

        Assert.True(browse.IsList);
    }

    // ---- getting something

    [AvaloniaFact]
    public async Task A_free_character_that_is_not_installed_offers_Get_with_its_picture_already_loaded()
    {
        Offer(Character());
        using var ui = Harness();

        await OpenBrowse(ui);

        var card = Card(ui, "tester.blob");
        Assert.Equal(StoreStatus.Available, card.Status);
        Assert.Equal("Get", card.ActionLabel);
        Assert.True(card.CanAct);
        Assert.Equal("Free", card.PriceText);
        Assert.Equal("By Tester", card.Byline);
        Assert.True(card.HasPreview);
        Assert.Equal(4, card.Tiles.Count);
    }

    [AvaloniaFact]
    public async Task Get_shows_the_install_card_and_installs_nothing_until_asked()
    {
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        var install = ui.Shell.InstallPreview;
        Assert.True(install.IsVisible);
        Assert.True(install.IsPreview);
        Assert.Equal("Blob", install.Title);
        Assert.Contains("the HeySip character list", install.Note);
        Assert.False(CharacterCatalog.Contains("tester.blob"));
        Assert.False(Directory.Exists(Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob")));
    }

    [AvaloniaFact]
    public async Task After_installing_the_card_becomes_installed_and_can_be_used()
    {
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var card = Card(ui, "tester.blob");
        Assert.Equal(StoreStatus.Installed, card.Status);
        Assert.Equal("Installed", card.StatusText);
        Assert.Equal("Use it", card.ActionLabel);
        Assert.True(CharacterCatalog.Contains("tester.blob"));

        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        card.ActCommand.Execute(null);
        Assert.Equal("tester.blob", ui.Get<SettingsService>().Current.Character);
    }

    [AvaloniaFact]
    public async Task An_accessory_from_the_list_is_previewed_on_the_character_in_use()
    {
        Offer(Hat());
        using var ui = Harness();
        ui.Get<SettingsService>().Update(s => s with { Character = "heysip.duckling" });
        await OpenBrowse(ui);

        var card = Card(ui, "tester.top-hat");

        Assert.Equal("Accessory", card.KindText);
        Assert.Equal("heysip.duckling", card.DrawnCharacter);
        Assert.Equal("tester.top-hat", Assert.Single(card.Outfit!).Header.Id);
        Assert.Equal(3, card.Tiles.Count);
    }

    [AvaloniaFact]
    public async Task A_newer_version_in_the_list_offers_an_update()
    {
        Offer(Character(version: "1.0.0"));
        using var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        _source.Entries.Clear();
        Offer(Character(version: "1.1.0"));
        await ui.Shell.Browse.RefreshAsync();

        var card = Card(ui, "tester.blob");
        Assert.Equal(StoreStatus.Update, card.Status);
        Assert.Equal("Update", card.ActionLabel);
        Assert.True(card.IsOwned);
    }

    // ---- a character that comes with accessories

    [AvaloniaFact]
    public async Task Getting_a_character_shows_the_accessories_that_come_with_it_and_installs_them_together()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        var install = ui.Shell.InstallPreview;
        Assert.True(install.IsPreview);
        Assert.True(install.HasBundled);
        var worn = Assert.Single(install.BundledTiles);
        Assert.Equal("Top hat", worn.Caption);
        Assert.NotNull(worn.Pack); // the character itself is drawn...
        Assert.Equal("tester.top-hat", Assert.Single(worn.Outfit!).Header.Id); // ...wearing the accessory
        Assert.Null(CharacterCatalog.FindAccessory("tester.top-hat")); // nothing is installed until asked
        Assert.False(CharacterCatalog.Contains("tester.blob"));

        install.InstallCommand.Execute(null);

        Assert.True(install.IsDone);
        Assert.True(CharacterCatalog.Contains("tester.blob"));
        Assert.NotNull(CharacterCatalog.FindAccessory("tester.top-hat"));
        Assert.True(Card(ui, "tester.top-hat").IsOwned);
    }

    [AvaloniaFact]
    public async Task An_accessory_that_is_already_installed_is_not_downloaded_again_with_its_character()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.top-hat").ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        var before = _source.Fetches;
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(before + 1, _source.Fetches); // only the character
        Assert.True(ui.Shell.InstallPreview.IsPreview);
        Assert.Equal("Top hat", Assert.Single(ui.Shell.InstallPreview.BundledTiles).Caption); // still shown, from what is installed
    }

    [AvaloniaFact]
    public async Task The_install_card_says_how_many_more_accessories_the_character_works_with()
    {
        Offer(Hat());
        Offer(Hat("tester.other-hat"));
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.other-hat").ActCommand.Execute(null); // an extra the user already has
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        // Everything on this computer that doesn't come with the character: the built-in accessories and the extra hat.
        Assert.Contains(CharacterCatalog.Accessories, accessory => accessory.Id == "tester.other-hat");
        var more = CharacterCatalog.Accessories.Count;
        Assert.True(more > 1);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.Equal($"Plus {more} more accessories you can add from the Accessories tab.", ui.Shell.InstallPreview.MoreText);

        // The next card doesn't keep the last one's set.
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        ui.Shell.InstallPreview.Show(PackLoadResult.Failure("nothing"), "a file", 0);
        Assert.False(ui.Shell.InstallPreview.HasBundled);
        Assert.Empty(ui.Shell.InstallPreview.BundledTiles);
        Assert.Equal("", ui.Shell.InstallPreview.MoreText);
    }

    [AvaloniaFact]
    public async Task A_characters_page_shows_its_own_accessories_and_the_rest_it_works_with()
    {
        Offer(Hat());
        Offer(Hat("tester.other-hat"));
        Offer(Hat("tester.third-hat"));
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();

        await OpenBrowse(ui);

        var blob = Card(ui, "tester.blob");
        Assert.True(blob.HasBundled);
        var tile = Assert.Single(blob.BundledTiles);
        Assert.Equal("Top hat", tile.Caption);
        Assert.Equal("tester.top-hat", Assert.Single(tile.Outfit!).Header.Id);
        Assert.Equal("Plus 2 more accessories you can add from the Accessories tab.", blob.MoreText);
        Assert.Equal(4, blob.Tiles.Count); // the usual poses are still there

        Assert.False(Card(ui, "tester.top-hat").HasBundled); // an accessory has no set of its own
        Assert.Equal("", Card(ui, "tester.top-hat").MoreText);
    }

    [AvaloniaFact]
    public async Task A_character_that_comes_with_nothing_says_what_it_works_with_instead()
    {
        Offer(Hat());
        Offer(Character());
        using var ui = Harness();

        await OpenBrowse(ui);

        var blob = Card(ui, "tester.blob");
        Assert.False(blob.HasBundled);
        Assert.Empty(blob.BundledTiles);
        Assert.Equal("Works with 1 accessory from the Accessories tab.", blob.MoreText);
    }

    [AvaloniaFact]
    public async Task A_character_whose_accessory_is_not_in_the_list_is_not_offered_for_install()
    {
        Offer(Character(), accessories: ["tester.top-hat"]); // the hat itself was never listed
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Contains("isn't available", Card(ui, "tester.blob").Message);
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public async Task A_character_whose_accessory_fails_its_check_is_not_offered_for_install()
    {
        Offer(Hat(), sha256: new string('0', 64));
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Contains("couldn't be used", Card(ui, "tester.blob").Message);
        Assert.Null(CharacterCatalog.FindAccessory("tester.top-hat"));
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    // ---- extras: owned, free, paid

    private const string BuyPage = "https://example.com/buy";

    [AvaloniaFact]
    public async Task Every_item_says_where_it_stands_in_a_word_or_two()
    {
        Offer(Hat("tester.free-hat"));
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        Offer(Hat("tester.soon-hat"), priceCents: 300);
        _source.Entries.Add(new StoreEntry("heysip.crown", PackKind.Accessory, "Crown", "HeySip", "1.0.0", "", 0, null, null, 0, null)); // comes with the app
        using var ui = Harness();

        await OpenBrowse(ui);

        var free = Card(ui, "tester.free-hat");
        Assert.Equal((StoreItemState.Free, "Free", false), (free.State, free.StateText, free.IsLocked));
        Assert.Equal("", free.LockNote);

        var paid = Card(ui, "tester.paid-hat");
        Assert.Equal((StoreItemState.Paid, "$2.50", true), (paid.State, paid.StateText, paid.IsLocked));
        Assert.Contains("once you've bought it", paid.LockNote);

        var soon = Card(ui, "tester.soon-hat");
        Assert.Equal((StoreItemState.ComingSoon, "$3.00 · Coming soon", true), (soon.State, soon.StateText, soon.IsLocked));
        Assert.Contains("can't be bought yet", soon.LockNote);

        var builtIn = Card(ui, "heysip.crown");
        Assert.Equal((StoreItemState.Owned, "Included", false), (builtIn.State, builtIn.StateText, builtIn.IsLocked));
    }

    [AvaloniaFact]
    public async Task An_item_becomes_owned_once_it_is_installed()
    {
        Offer(Hat("tester.free-hat"));
        using var ui = Harness();
        await OpenBrowse(ui);
        var hat = Card(ui, "tester.free-hat");
        Assert.Equal(StoreItemState.Free, hat.State);

        hat.ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Equal((StoreItemState.Owned, "Owned", false), (hat.State, hat.StateText, hat.IsLocked));
        Assert.Equal("", hat.LockNote);
    }

    [AvaloniaFact]
    public async Task A_paid_accessory_can_be_tried_on_but_not_installed_or_worn()
    {
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        using var ui = Harness();
        await OpenBrowse(ui);
        var hat = Card(ui, "tester.paid-hat");

        // It is drawn on the character in use, so it can be tried on.
        Assert.True(hat.HasPreview);
        Assert.NotEmpty(hat.Tiles);
        Assert.All(hat.Tiles, tile => Assert.Equal("tester.paid-hat", Assert.Single(tile.Outfit!).Header.Id));
        Assert.Equal("Buy $2.50", hat.ActionLabel);

        hat.ActCommand.Execute(null);
        await Task.Yield();

        Assert.Equal([BuyPage], _launcher.Opened); // buying happens on the web page, not in the app
        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Null(CharacterCatalog.FindAccessory("tester.paid-hat"));
        Assert.Empty(ui.Get<SettingsService>().Current.Outfit);
    }

    [AvaloniaFact]
    public async Task A_paid_accessory_with_nowhere_to_buy_it_can_only_be_looked_at()
    {
        Offer(Hat("tester.soon-hat"), priceCents: 300);
        using var ui = Harness();
        await OpenBrowse(ui);
        var hat = Card(ui, "tester.soon-hat");

        Assert.True(hat.HasPreview);
        Assert.Equal("Coming soon", hat.ActionLabel);
        Assert.False(hat.CanAct);
    }

    // ---- the sample set that ships with the Cactus

    [AvaloniaFact]
    public async Task Getting_the_cactus_from_the_real_list_installs_its_three_accessories_and_dresses_it()
    {
        string[] set = ["heysip-studio.cactus-sombrero", "heysip-studio.cactus-bandana", "heysip-studio.cactus-heart-glasses"];
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);

        // Its page already shows the set, before it is installed.
        var cactus = Card(ui, "heysip-studio.cactus");
        Assert.Equal(["Sombrero", "Bandana", "Heart glasses"], cactus.BundledTiles.Select(tile => tile.Caption));
        Assert.All(cactus.Tiles, tile => Assert.Equal(3, tile.Outfit!.Count)); // and the poses wear it

        cactus.ActCommand.Execute(null);
        await Task.Yield();
        var install = ui.Shell.InstallPreview;
        Assert.True(install.IsPreview);
        Assert.Equal(["Sombrero", "Bandana", "Heart glasses"], install.BundledTiles.Select(tile => tile.Caption));
        Assert.All(set, id => Assert.Null(CharacterCatalog.FindAccessory(id))); // nothing yet

        install.InstallCommand.Execute(null);

        // All four packs are installed, and the character remembers its set.
        Assert.True(CharacterCatalog.Contains("heysip-studio.cactus"));
        Assert.All(set, id => Assert.NotNull(CharacterCatalog.FindAccessory(id)));
        Assert.Equal(set, CharacterCatalog.Find("heysip-studio.cactus").Included);

        // It starts out wearing one of each, a hat, glasses and a neck piece.
        var outfit = ui.Get<SettingsService>().Current.OutfitOf("heysip-studio.cactus");
        Assert.Equal("heysip-studio.cactus-sombrero", outfit["hat"]);
        Assert.Equal("heysip-studio.cactus-heart-glasses", outfit["eyes"]);
        Assert.Equal("heysip-studio.cactus-bandana", outfit["neck"]);

        // And the outfit picker lists them first, under the Cactus.
        install.UseNowCommand.Execute(null);
        var sections = ui.Shell.Settings.OutfitSections;
        Assert.Equal(["Included with Cactus", "More accessories"], sections.Select(section => section.Title));
        Assert.Equal([AccessorySlot.Hat, AccessorySlot.Eyes, AccessorySlot.Neck], sections[0].Slots.Select(row => row.Slot));

        // The accessories are items of their own in the list, and now owned.
        Assert.All(set, id => Assert.Equal(StoreItemState.Owned, Card(ui, id).State));
    }

    // ---- payments switched off (until Paddle is set up)

    [AvaloniaFact]
    public async Task While_payments_are_off_a_paid_item_is_coming_soon_even_with_a_buy_address()
    {
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        Offer(Character(), priceCents: 300, buyUrl: BuyPage);
        using var ui = Harness(payments: false);

        await OpenBrowse(ui);

        foreach (var id in new[] { "tester.paid-hat", "tester.blob" })
        {
            var card = Card(ui, id);
            Assert.Equal(StoreItemState.ComingSoon, card.State);
            Assert.Contains("Coming soon", card.StateText);
            Assert.True(card.IsLocked);
            Assert.Contains("can't be bought yet", card.LockNote);
            Assert.Equal("Coming soon", card.ActionLabel);
            Assert.False(card.CanAct);
            Assert.False(card.CanBuy);

            card.ActCommand.Execute(null); // nothing happens
        }

        await Task.Yield();
        Assert.Empty(_launcher.Opened); // no buy page is ever opened
        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Null(CharacterCatalog.FindAccessory("tester.paid-hat"));
    }

    [AvaloniaFact]
    public async Task While_payments_are_off_a_paid_item_can_still_be_looked_at_and_tried_on()
    {
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        Offer(Character());
        using var ui = Harness(payments: false);

        await OpenBrowse(ui);

        var hat = Card(ui, "tester.paid-hat");
        Assert.True(hat.HasPreview);
        Assert.All(hat.Tiles, tile => Assert.Equal("tester.paid-hat", Assert.Single(tile.Outfit!).Header.Id));
        var extra = Assert.Single(Card(ui, "tester.blob").Extras);
        Assert.Equal("tester.paid-hat", extra.Card.Entry.Id); // and shown on the character's page
        Assert.Contains("Coming soon", extra.Card.StateText);
    }

    [AvaloniaFact]
    public async Task While_payments_are_off_free_items_and_owned_items_work_as_usual()
    {
        Offer(Hat("tester.free-hat"));
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        _source.Entries.Add(new StoreEntry("heysip.crown", PackKind.Accessory, "Crown", "HeySip", "1.0.0", "", 0, null, null, 0, null));
        using var ui = Harness(payments: false);
        await OpenBrowse(ui);

        var free = Card(ui, "tester.free-hat");
        Assert.Equal((StoreItemState.Free, "Get", true), (free.State, free.ActionLabel, free.CanAct));
        free.ActCommand.Execute(null);
        await Task.Yield();
        Assert.True(ui.Shell.InstallPreview.IsPreview);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        Assert.Equal(StoreItemState.Owned, free.State);

        var crown = Card(ui, "heysip.crown");
        Assert.Equal(("Wear it", true), (crown.ActionLabel, crown.CanAct));
        crown.ActCommand.Execute(null);
        Assert.Equal("heysip.crown", ui.Get<SettingsService>().Current.Outfit["hat"]);
    }

    [AvaloniaFact]
    public async Task While_payments_are_off_a_paid_accessory_that_comes_with_a_character_is_still_installed_with_it()
    {
        Offer(Hat(), priceCents: 250, buyUrl: BuyPage);
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness(payments: false);
        await OpenBrowse(ui);
        Assert.Equal(StoreItemState.ComingSoon, Card(ui, "tester.top-hat").State);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Equal(StoreItemState.Owned, Card(ui, "tester.top-hat").State);
        Assert.Empty(_launcher.Opened);
    }

    [AvaloniaFact]
    public void The_app_ships_with_payments_off_and_the_switch_can_be_flipped_for_a_trial()
    {
        Assert.False(StorePayments.EnabledByDefault);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StorePayments.OverrideVariable)))
        {
            return; // someone is trying payments out on this computer
        }

        using var ui = UiHarness.Create(_ => { });

        Assert.False(ui.Get<StorePayments>().Enabled);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("maybe", false)]
    [InlineData("on", true)]
    [InlineData("ON", true)]
    [InlineData(" 1 ", true)]
    [InlineData("true", true)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void The_payments_switch_reads_on_and_off_and_ignores_anything_else(string? value, bool expected)
    {
        Assert.Equal(expected, StorePayments.Resolve(value).Enabled);
    }

    [AvaloniaFact]
    public async Task An_accessory_that_comes_with_a_paid_character_is_owned_with_it_whatever_it_costs()
    {
        Offer(Hat(), priceCents: 250, buyUrl: BuyPage);
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = await GetBlob();
        Assert.Equal(StoreItemState.Paid, Card(ui, "tester.top-hat").State);

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var hat = Card(ui, "tester.top-hat");
        Assert.Equal((StoreItemState.Owned, false), (hat.State, hat.IsLocked));
        Assert.NotNull(CharacterCatalog.FindAccessory("tester.top-hat"));
    }

    // ---- extras on a character's page

    [AvaloniaFact]
    public async Task A_characters_page_tries_the_other_accessories_on_it_with_what_you_can_do_about_each()
    {
        Offer(Hat());
        Offer(Hat("tester.free-hat"));
        Offer(Hat("tester.paid-hat"), priceCents: 250, buyUrl: BuyPage);
        Offer(Character(), accessories: ["tester.top-hat"]);
        _source.Entries.Add(new StoreEntry("heysip.crown", PackKind.Accessory, "Crown", "HeySip", "1.0.0", "", 0, null, null, 0, null)); // owned
        using var ui = Harness();

        await OpenBrowse(ui);

        var blob = Card(ui, "tester.blob");
        Assert.True(blob.HasExtras);
        // Not the one that comes with it; the one you can wear now first, then the rest in the list's order.
        Assert.Equal(["heysip.crown", "tester.free-hat", "tester.paid-hat"], blob.Extras.Select(extra => extra.Card.Entry.Id));
        Assert.Equal(["Included", "Free", "$2.50"], blob.Extras.Select(extra => extra.Card.StateText));

        // Each is drawn on this character, wearing that accessory.
        Assert.All(blob.Extras, extra =>
        {
            Assert.Same(blob.Pack, extra.Tile.Pack);
            Assert.Equal(extra.Card.Entry.Id, Assert.Single(extra.Tile.Outfit!).Header.Id);
        });
    }

    [AvaloniaFact]
    public async Task Tapping_an_extra_opens_its_page()
    {
        Offer(Hat("tester.free-hat"));
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);

        var extra = Card(ui, "tester.blob").Extras.Single();
        extra.Card.OpenCommand.Execute(null);

        Assert.Same(Card(ui, "tester.free-hat"), ui.Shell.Browse.Selected);
    }

    [AvaloniaFact]
    public async Task An_extra_updates_when_you_get_it()
    {
        Offer(Hat("tester.free-hat"));
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);
        var extra = Card(ui, "tester.blob").Extras.Single();
        Assert.Equal("Free", extra.Card.StateText);

        extra.Card.ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Equal("Owned", Card(ui, "tester.blob").Extras.Single().Card.StateText);
    }

    [AvaloniaFact]
    public async Task A_characters_page_shows_a_few_extras_and_counts_the_rest()
    {
        for (var number = 0; number < BrowseViewModel.MaxExtrasShown + 2; number++)
        {
            Offer(Hat($"tester.hat-{number:00}"));
        }

        Offer(Character());
        using var ui = Harness();

        await OpenBrowse(ui);

        var blob = Card(ui, "tester.blob");
        Assert.Equal(BrowseViewModel.MaxExtrasShown, blob.Extras.Count);
        Assert.Equal($"Works with {BrowseViewModel.MaxExtrasShown + 2} accessories from the Accessories tab.", blob.MoreText);
    }

    [AvaloniaFact]
    public async Task Only_a_character_has_extras()
    {
        Offer(Hat());
        Offer(Hat("tester.other-hat"));
        using var ui = Harness();

        await OpenBrowse(ui);

        Assert.All(ui.Shell.Browse.Cards, card => Assert.False(card.HasExtras));
    }

    // ---- a new character wears its own accessories

    /// <summary>Opens Browse and presses Get on Blob, so its install card is showing.</summary>
    private async Task<UiHarness> GetBlob()
    {
        var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        return ui;
    }

    [AvaloniaFact]
    public async Task A_new_character_starts_out_wearing_the_accessories_it_comes_with()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = await GetBlob();

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var settings = ui.Get<SettingsService>().Current;
        Assert.Equal("tester.top-hat", settings.OutfitOf("tester.blob")["hat"]);
        Assert.NotEqual("tester.blob", settings.Character); // wearing it doesn't make it the character in use
        Assert.Empty(settings.Outfit);

        ui.Shell.InstallPreview.UseNowCommand.Execute(null);

        Assert.Equal("tester.top-hat", ui.Get<SettingsService>().Current.Outfit["hat"]);
        Assert.Equal(["tester.top-hat"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));
    }

    [AvaloniaFact]
    public async Task A_new_character_wears_the_first_of_its_accessories_for_a_slot_and_one_per_slot()
    {
        Offer(Hat("tester.hat-a"));
        Offer(Hat("tester.hat-b"));
        Offer(Hat("tester.hat-c"));
        Offer(Character(), accessories: ["tester.hat-b", "tester.hat-a", "tester.hat-c"]);
        using var ui = await GetBlob();

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var outfit = ui.Get<SettingsService>().Current.OutfitOf("tester.blob");
        Assert.Equal(["hat"], outfit.Keys);
        Assert.Equal("tester.hat-b", outfit["hat"]); // the first one listed
    }

    [AvaloniaFact]
    public async Task A_character_that_already_has_an_outfit_keeps_it_when_installed()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = await GetBlob();
        ui.Get<SettingsService>().Update(settings => settings.WithAccessory("tester.blob", AccessorySlot.Eyes, "heysip.sunglasses"));

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var outfit = ui.Get<SettingsService>().Current.OutfitOf("tester.blob");
        Assert.Equal(["eyes"], outfit.Keys); // not dressed on top of what was chosen
    }

    [AvaloniaFact]
    public async Task An_update_does_not_dress_the_character_again()
    {
        Offer(Hat());
        Offer(Character(version: "1.0.0"), accessories: ["tester.top-hat"]);
        using var ui = await GetBlob();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        ui.Get<SettingsService>().Update(settings => settings.WithAccessory("tester.blob", AccessorySlot.Hat, null)); // the user took it off

        _source.Entries.Clear();
        Offer(Hat());
        Offer(Character(version: "1.1.0"), accessories: ["tester.top-hat"]);
        await ui.Shell.Browse.RefreshAsync();
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        Assert.True(ui.Shell.InstallPreview.IsPreview);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Empty(ui.Get<SettingsService>().Current.OutfitOf("tester.blob"));
    }

    [AvaloniaFact]
    public void A_character_from_a_file_is_not_dressed()
    {
        using var ui = Harness();
        var fromFile = PackLoader.FromZip(new MemoryStream(Zip(Character("tester.solo", name: "Solo"))), new PackLoadOptions(new Version(1, 0)));

        ui.Shell.InstallPreview.Show(fromFile, "a file", 1);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.True(CharacterCatalog.Contains("tester.solo"));
        Assert.Empty(ui.Get<SettingsService>().Current.OutfitOf("tester.solo"));
    }

    [AvaloniaFact]
    public async Task The_preview_shows_a_new_character_wearing_what_it_will_start_out_wearing()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        using var ui = Harness();
        await OpenBrowse(ui);

        // On its page in Browse, while it isn't installed.
        var blob = Card(ui, "tester.blob");
        Assert.Equal(4, blob.Tiles.Count);
        Assert.All(blob.Tiles, tile => Assert.Equal("tester.top-hat", Assert.Single(tile.Outfit!).Header.Id));

        // On the install card.
        blob.ActCommand.Execute(null);
        await Task.Yield();
        var install = ui.Shell.InstallPreview;
        Assert.Equal(4, install.Tiles.Count);
        Assert.All(install.Tiles, tile => Assert.Equal("tester.top-hat", Assert.Single(tile.Outfit!).Header.Id));

        // Once it is installed, its page shows it as it is, not as it would start out.
        install.InstallCommand.Execute(null);
        Assert.All(Card(ui, "tester.blob").Tiles, tile => Assert.Null(tile.Outfit));
    }

    // ---- the outfit picker in Settings

    /// <summary>Gets Blob, which comes with the top hat, installs it and makes it the character in use.</summary>
    private async Task<UiHarness> UseBlobWithItsHat()
    {
        Offer(Hat());
        Offer(Character(), accessories: ["tester.top-hat"]);
        var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.UseNowCommand.Execute(null);
        return ui;
    }

    [AvaloniaFact]
    public async Task The_outfit_picker_lists_what_comes_with_the_character_first_and_the_rest_after()
    {
        using var ui = await UseBlobWithItsHat();

        Assert.Equal(["tester.top-hat"], CharacterCatalog.Find("tester.blob").Included); // remembered once installed

        var sections = ui.Shell.Settings.OutfitSections;
        Assert.Equal(["Included with Blob", "More accessories"], sections.Select(section => section.Title));

        var own = Assert.Single(sections[0].Slots);
        Assert.Equal(AccessorySlot.Hat, own.Slot);
        Assert.Equal([null, "tester.top-hat"], own.Options.Select(option => option.Id)); // "None" is here, in the first row of the slot

        var more = sections[1].Slots;
        var hats = more.Single(row => row.Slot == AccessorySlot.Hat);
        Assert.DoesNotContain(hats.Options, option => option.Id is null); // so not repeated here
        Assert.Equal(["heysip.beanie", "heysip.crown", "heysip.party-hat"], hats.Options.Select(option => option.Id).Order());

        // A slot the character has nothing of its own for keeps its "None" in the other group.
        Assert.All(more.Where(row => row.Slot != AccessorySlot.Hat), row => Assert.Null(row.Options[0].Id));
    }

    [AvaloniaFact]
    public async Task One_accessory_per_slot_is_chosen_across_both_groups()
    {
        using var ui = await UseBlobWithItsHat();
        var sections = ui.Shell.Settings.OutfitSections;
        var own = sections[0].Slots.Single();
        var none = own.Options[0];
        var topHat = own.Options.Single(option => option.Id == "tester.top-hat");
        var beanie = sections[1].Slots.Single(row => row.Slot == AccessorySlot.Hat).Options.Single(option => option.Id == "heysip.beanie");
        Assert.True(topHat.IsSelected); // a new character starts out wearing its own hat
        Assert.False(none.IsSelected);

        beanie.SelectCommand.Execute(null);

        Assert.Equal("heysip.beanie", ui.Get<SettingsService>().Current.Outfit["hat"]);
        Assert.True(beanie.IsSelected);
        Assert.False(topHat.IsSelected);
        Assert.False(none.IsSelected); // not "None" just because the beanie is in the other group

        topHat.SelectCommand.Execute(null);

        Assert.True(topHat.IsSelected);
        Assert.False(beanie.IsSelected);
        Assert.False(none.IsSelected);

        none.SelectCommand.Execute(null);

        Assert.True(none.IsSelected);
        Assert.False(topHat.IsSelected);
        Assert.Empty(ui.Get<SettingsService>().Current.Outfit);
    }

    [AvaloniaFact]
    public async Task Switching_to_a_character_that_comes_with_nothing_shows_one_plain_group()
    {
        using var ui = await UseBlobWithItsHat();
        Assert.Equal(2, ui.Shell.Settings.OutfitSections.Count);

        ui.Get<SettingsService>().Update(settings => settings with { Character = PackId.DefaultCharacter });

        var only = Assert.Single(ui.Shell.Settings.OutfitSections);
        Assert.Equal("", only.Title);
        Assert.False(only.HasTitle);
        Assert.Equal(4, only.Slots.Count);
        Assert.All(only.Slots, row => Assert.Null(row.Options[0].Id));

        ui.Get<SettingsService>().Update(settings => settings with { Character = "tester.blob" });

        Assert.Equal(2, ui.Shell.Settings.OutfitSections.Count); // and back
    }

    [AvaloniaFact]
    public async Task Installing_the_character_again_from_a_file_keeps_what_came_with_it()
    {
        using var ui = await UseBlobWithItsHat();
        var again = PackLoader.FromZip(new MemoryStream(Zip(Character())), new PackLoadOptions(new Version(1, 0)));

        ui.Shell.InstallPreview.Show(again, "a file", 1); // a file knows nothing about a set
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Equal(["tester.top-hat"], CharacterCatalog.Find("tester.blob").Included);
    }

    [AvaloniaFact]
    public async Task Removing_the_character_removes_what_was_remembered_about_its_set()
    {
        using var ui = await UseBlobWithItsHat();
        var folder = Path.Combine(ui.Data.Location.CharactersDirectory, "tester.blob");
        Assert.True(File.Exists(Path.Combine(folder, PackBundle.FileName)));

        PackInstaller.Uninstall("tester.blob", ui.Data.Location.CharactersDirectory);
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);

        Assert.False(Directory.Exists(folder));
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public async Task Get_more_accessories_opens_the_accessories_tab()
    {
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);
        Assert.True(ui.Shell.Browse.IsCharactersTab);
        ui.Shell.Navigate(HeySip.App.Shell.ShellPage.Settings);
        Assert.NotSame(ui.Shell.Browse, ui.Shell.SelectedTab.Content);

        ui.Shell.Settings.BrowseAccessoriesCommand.Execute(null);

        Assert.Same(ui.Shell.Browse, ui.Shell.SelectedTab.Content);
        Assert.True(ui.Shell.Browse.IsAccessoriesTab);

        ui.Shell.Settings.BrowseCharactersCommand.Execute(null);

        Assert.True(ui.Shell.Browse.IsCharactersTab);
    }

    // ---- what is not trusted

    [AvaloniaFact]
    public async Task A_download_that_does_not_match_its_checksum_is_thrown_away()
    {
        Offer(Character(), sha256: new string('0', 64));
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Contains("wasn't what the list promised", Card(ui, "tester.blob").Message);
    }

    [AvaloniaFact]
    public async Task A_download_with_the_right_checksum_is_accepted()
    {
        var bytes = Zip(Character());
        Offer(Character(), sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.True(ui.Shell.InstallPreview.IsPreview);
    }

    [AvaloniaFact]
    public async Task A_file_that_is_a_different_item_than_the_list_says_is_refused()
    {
        Offer(Character(id: "tester.other"), withId: "tester.blob");
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.Contains("wasn't the item", Card(ui, "tester.blob").Message);
    }

    [AvaloniaFact]
    public async Task A_file_that_fails_the_pack_checks_is_refused_with_the_reason_on_the_install_card()
    {
        Offer(Character());
        _source.Packs["tester.blob"] = Encoding.UTF8.GetBytes("this is not a zip");
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        Assert.True(ui.Shell.InstallPreview.IsFailed);
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public async Task A_built_in_id_cannot_be_installed_over_a_built_in_character()
    {
        Offer(Character(id: "heysip.impostor"));
        using var ui = Harness();
        await OpenBrowse(ui);

        Card(ui, "heysip.impostor").ActCommand.Execute(null);
        await Task.Yield();

        Assert.True(ui.Shell.InstallPreview.IsFailed);
        Assert.False(CharacterCatalog.Contains("heysip.impostor"));
    }

    // ---- when things go wrong

    [AvaloniaFact]
    public async Task A_failed_download_says_so_and_can_be_tried_again()
    {
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);
        _source.FailDownloads = true;

        var card = Card(ui, "tester.blob");
        card.ActCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("couldn't be downloaded", card.Message);
        Assert.False(card.IsBusy);
        Assert.True(card.CanAct);

        _source.FailDownloads = false;
        card.ActCommand.Execute(null);
        await Task.Yield();

        Assert.True(ui.Shell.InstallPreview.IsPreview);
        Assert.Equal("", card.Message);
    }

    [AvaloniaFact]
    public async Task A_list_that_cannot_be_loaded_shows_an_error_and_loads_on_retry()
    {
        _source.ListError = "boom";
        using var ui = Harness();

        await OpenBrowse(ui);

        Assert.True(ui.Shell.Browse.HasError);
        Assert.Empty(ui.Shell.Browse.Cards);
        Assert.False(ui.Shell.Browse.IsEmpty);

        _source.ListError = null;
        Offer(Character());
        await ui.Shell.Browse.RefreshCommand.ExecuteAsync(null);

        Assert.False(ui.Shell.Browse.HasError);
        Assert.Single(ui.Shell.Browse.Cards);
    }

    [AvaloniaFact]
    public async Task A_note_from_the_list_is_shown_and_goes_away_when_it_no_longer_applies()
    {
        Offer(Character());
        _source.Notice = "The online list couldn't be reached, so this is only what comes with the app.";
        using var ui = Harness();

        await OpenBrowse(ui);
        Assert.Contains("couldn't be reached", ui.Shell.Browse.Notice);

        _source.Notice = null;
        await ui.Shell.Browse.RefreshAsync();
        Assert.Equal("", ui.Shell.Browse.Notice);
    }

    [AvaloniaFact]
    public async Task Without_a_list_address_the_app_shows_only_the_list_inside_it_says_nothing_and_contacts_nothing()
    {
        using var ui = Harness(fake: false);

        // The app has no list address of its own, and the control file (which has not been read in a test) gives none: the online part adds nothing.
        var shown = await ui.Get<IStoreSource>().LoadAsync(TestContext.Current.CancellationToken);
        var inside = await new BundledStoreSource().LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(shown.Ok);
        Assert.Null(shown.Notice);
        Assert.Equal(inside.Entries.Select(entry => entry.Id).Order(), shown.Entries.Select(entry => entry.Id).Order());
    }

    [AvaloniaFact]
    public async Task An_online_list_with_a_newer_version_of_an_installed_item_offers_an_update_end_to_end()
    {
        var online = new FakeSource();
        using var ui = UiHarness.Create(services => services.AddSingleton<IStoreSource>(new LayeredStoreSource(online, new BundledStoreSource())));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        await OpenBrowse(ui);
        var cactus = Card(ui, "heysip-studio.cactus");
        cactus.ActCommand.Execute(null);
        await Task.Yield();
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        Assert.Equal(StoreStatus.Installed, cactus.Status);

        // The online list now has a newer version, but its file can't be had: the page says so and keeps working.
        online.Entries.Add(new StoreEntry("heysip-studio.cactus", PackKind.Character, "Cactus", "HeySip Studio", "9.0.0", "", 0, "packs/cactus-9.heysippack", null, 0, null));
        await ui.Shell.Browse.RefreshAsync();
        var newer = Card(ui, "heysip-studio.cactus");
        Assert.Equal(StoreStatus.Update, newer.Status);
        newer.ActCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("couldn't be downloaded", newer.Message);
        Assert.True(CharacterCatalog.Contains("heysip-studio.cactus"));
    }

    [AvaloniaFact]
    public async Task A_page_that_has_been_disposed_no_longer_follows_the_catalogue_or_the_settings()
    {
        using var ui = Harness(fake: false);
        await OpenBrowse(ui);
        var minty = Card(ui, "heysip.minty");

        ui.Shell.Browse.Dispose();
        ui.Get<SettingsService>().Update(s => s with { Character = "heysip.minty" });
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);

        Assert.False(minty.IsInUse);
    }

    [AvaloniaFact]
    public void Disposing_the_app_disposes_the_page()
    {
        var ui = Harness(fake: false);
        var browse = ui.Shell.Browse;
        var minty = browse.RefreshAsync();
        Assert.True(minty.IsCompleted);

        ui.Dispose();
        CharacterCatalog.Configure(null); // would reach the page if it were still listening

        Assert.False(Assert.Single(browse.Cards, card => card.Entry.Id == "heysip.minty").IsInUse);
    }

    [AvaloniaFact]
    public async Task An_empty_list_says_nothing_is_here_yet()
    {
        using var ui = Harness();

        await OpenBrowse(ui);

        Assert.True(ui.Shell.Browse.IsEmpty);
    }

    [AvaloniaFact]
    public async Task A_picture_that_cannot_be_loaded_leaves_the_card_without_one_but_usable()
    {
        Offer(Character());
        _source.FailDownloads = true;
        using var ui = Harness();

        await OpenBrowse(ui);

        var card = Card(ui, "tester.blob");
        Assert.False(card.HasPreview);
        Assert.Equal("Get", card.ActionLabel);
    }

    // ---- paid items

    [AvaloniaFact]
    public async Task A_paid_item_shows_its_price_and_opens_the_buy_page()
    {
        Offer(Character(), priceCents: 250, buyUrl: "https://example.com/buy/blob");
        using var ui = Harness();
        await OpenBrowse(ui);

        var card = Card(ui, "tester.blob");
        Assert.Equal("$2.50", card.PriceText);
        Assert.Equal("Buy $2.50", card.ActionLabel);
        Assert.True(card.IsPaid);

        card.ActCommand.Execute(null);

        Assert.Equal(["https://example.com/buy/blob"], _launcher.Opened);
        Assert.False(ui.Shell.InstallPreview.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_paid_item_with_no_buy_page_cannot_be_bought_yet()
    {
        Offer(Character(), priceCents: 99);
        using var ui = Harness();
        await OpenBrowse(ui);

        var card = Card(ui, "tester.blob");

        Assert.Equal("$0.99", card.PriceText);
        Assert.Equal("Coming soon", card.ActionLabel);
        Assert.False(card.CanAct);
    }

    // ---- getting there

    [AvaloniaFact]
    public async Task The_page_opens_in_its_own_tab_and_the_rail_shows_it()
    {
        using var ui = Harness();

        ui.Shell.ShowBrowseCommand.Execute(null);

        Assert.True(ui.Shell.IsBrowse);
        Assert.Equal("Characters", ui.Shell.SelectedTab.Title);
        Assert.Same(ui.Shell.Browse, ui.Shell.Current);

        ui.Shell.ShowBrowseCommand.Execute(null);
        Assert.Single(ui.Shell.Tabs, tab => tab.Key == "browse");
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Settings_has_a_button_that_goes_to_the_page()
    {
        using var ui = Harness();

        ui.Shell.Settings.BrowseCharactersCommand.Execute(null);

        Assert.True(ui.Shell.IsBrowse);
    }

    [AvaloniaFact]
    public void The_palette_can_open_the_page()
    {
        using var ui = Harness();

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "browse characters";
        ui.Shell.Palette.RunSelected();

        Assert.True(ui.Shell.IsBrowse);
    }

    [AvaloniaFact]
    public void The_shortcut_does_nothing_while_an_install_card_is_up()
    {
        using var ui = Harness();
        ui.Shell.InstallPreview.ShowError("x");

        ui.Shell.ShowBrowseCommand.Execute(null);

        Assert.False(ui.Shell.IsBrowse);
    }

    [AvaloniaFact]
    public async Task Closing_the_settings_tab_does_not_stop_it_following_installs()
    {
        Offer(Character());
        using var ui = Harness();
        ui.Shell.Navigate(HeySip.App.Shell.ShellPage.Settings);
        ui.Shell.CloseCurrentTab();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").ActCommand.Execute(null);
        await Task.Yield();

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Contains(ui.Shell.Settings.Characters, option => option.Id == "tester.blob");
    }

    [AvaloniaFact]
    public async Task Refreshing_keeps_the_item_that_is_open()
    {
        Offer(Character());
        using var ui = Harness();
        await OpenBrowse(ui);
        Card(ui, "tester.blob").OpenCommand.Execute(null);

        await ui.Shell.Browse.RefreshAsync();

        Assert.Equal("tester.blob", ui.Shell.Browse.Selected!.Entry.Id);
    }

    [AvaloniaFact]
    public async Task The_page_draws_without_error()
    {
        Offer(Character());
        Offer(Hat());
        Offer(Character(id: "tester.paid", name: "Paid"), priceCents: 300);
        using var ui = Harness();
        ui.Show();

        await OpenBrowse(ui);
        ui.Show();
        Card(ui, "tester.blob").OpenCommand.Execute(null);
        ui.Show();

        Assert.True(ui.Shell.Browse.IsDetail);
    }

    /// <summary>
    /// Draws the page, to look at. Set HEYSIP_BROWSE_SHOT to a file path; HEYSIP_BROWSE_VIEW to "detail" for an item's page,
    /// or "empty" for the built-in list only (default: a list with extra items on offer).
    /// </summary>
    [AvaloniaFact]
    public async Task Draw_the_page_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable("HEYSIP_BROWSE_SHOT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var view = Environment.GetEnvironmentVariable("HEYSIP_BROWSE_VIEW") ?? "";
        JsonObject Derived(string asset, string id, string name, string author)
        {
            using var source = Avalonia.Platform.AssetLoader.Open(new Uri(asset));
            var manifest = JsonNode.Parse(source)!.AsObject();
            manifest["id"] = id;
            manifest["name"] = name;
            manifest["author"] = author;
            manifest["version"] = "1.0.0";
            return manifest;
        }

        using var ui = Harness(fake: view == "empty" ? false : true);
        if (view != "empty")
        {
            foreach (var builtIn in new[] { "minty", "sunny", "blossom", "drip" })
            {
                _source.Entries.Add(new StoreEntry("heysip." + builtIn, PackKind.Character, builtIn, "HeySip", "1.0.0", "", 0, null, null, 0, null));
            }

            Offer(Derived("avares://HeySip/Assets/Characters/heysip.axolotl.json", "alice.axolotl-deluxe", "Axolotl Deluxe", "Alice"));
            Offer(Derived("avares://HeySip/Assets/Characters/heysip.duckling.json", "bob.ducky", "Ducky", "Bob"), priceCents: 250, buyUrl: "https://example.com/buy");
            Offer(Derived("avares://HeySip/Assets/Accessories/heysip.crown.json", "alice.royal-crown", "Royal crown", "Alice"));
        }

        ui.Show();
        await OpenBrowse(ui);
        if (view == "detail")
        {
            Card(ui, "alice.axolotl-deluxe").OpenCommand.Execute(null);
        }

        ui.Show();
        ui.Screenshot(path);
    }
}

public sealed class StoreAddressTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("http://example.com/catalog.json", null)]
    [InlineData("file:///c:/catalog.json", null)]
    [InlineData("https://example.com/catalog.json", "https://example.com/catalog.json")]
    [InlineData("http://localhost:5000/catalog.json", "http://localhost:5000/catalog.json")]
    public void An_address_must_be_one_the_app_may_use(string? value, string? expected)
    {
        Assert.Equal(expected, HeySip.Core.Features.Companion.Store.StoreAddress.Parse(value)?.ToString());
    }
}
