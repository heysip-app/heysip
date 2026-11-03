using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using HeySip.App.Features.Install;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class InstallFlowTests : IDisposable
{
    private readonly string _files = Path.Combine(Path.GetTempPath(), "heysip-installflow-" + Guid.NewGuid().ToString("N"));

    public InstallFlowTests() => Directory.CreateDirectory(_files);

    public void Dispose()
    {
        CharacterCatalog.Configure(null);
        if (Directory.Exists(_files))
        {
            Directory.Delete(_files, recursive: true);
        }
    }

    private UiHarness Harness(bool onboarded = true)
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = onboarded });
        ui.Get<ReminderService>().Restart();
        CharacterCatalog.Configure(ui.Data.Location.CharactersDirectory);
        return ui;
    }

    private static JsonArray Point(double x, double y) => new(x, y);

    private static JsonObject CharacterManifest(string id = "tester.blob", string version = "1.0.0", string name = "Blob") => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "character",
        ["id"] = id,
        ["name"] = name,
        ["version"] = version,
        ["author"] = "Tester",
        ["description"] = "A purple blob.",
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

    private static JsonObject AccessoryManifest(string id = "tester.top-hat", string version = "1.0.0", string name = "Top hat", string slot = "hat") => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "accessory",
        ["id"] = id,
        ["name"] = name,
        ["version"] = version,
        ["slot"] = slot,
        ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-12,0 L0,-26 L12,0 Z", ["fill"] = "#22AA55" }),
    };

    private string Write(JsonObject manifest, string? fileName = null)
    {
        var path = Path.Combine(_files, fileName ?? manifest["id"] + "-" + manifest["version"] + ".heysippack");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using var content = zip.CreateEntry("pack.json").Open();
        content.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return path;
    }

    private static string CharactersDirectory(UiHarness ui) => ui.Data.Location.CharactersDirectory;

    private static bool IsInstalled(UiHarness ui, string id) =>
        Directory.Exists(Path.Combine(CharactersDirectory(ui), id));

    // ---- the preview

    [AvaloniaFact]
    public void Opening_a_character_shows_what_it_is_and_how_it_looks_without_installing_anything()
    {
        using var ui = Harness();

        ui.Shell.OpenPackFile(Write(CharacterManifest()));

        var card = ui.Shell.InstallPreview;
        Assert.True(card.IsVisible);
        Assert.True(card.IsPreview);
        Assert.Equal("Blob", card.Title);
        Assert.Equal("New character", card.Eyebrow);
        Assert.Contains("Tester", card.Details);
        Assert.Contains("1.0.0", card.Details);
        Assert.Contains("Free", card.Details);
        Assert.Contains("A purple blob.", card.Details);
        Assert.Contains("bytes", card.Details);
        Assert.Equal(4, card.Tiles.Count);
        Assert.All(card.Tiles, tile => Assert.Equal("Blob", tile.Pack!.Header.Name));
        Assert.Equal("Install", card.InstallLabel);
        Assert.True(card.CanInstall);
        Assert.False(IsInstalled(ui, "tester.blob"));
        Assert.False(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public void Opening_an_accessory_shows_it_on_the_character_you_have_now()
    {
        using var ui = Harness();
        ui.Get<SettingsService>().Update(s => s with { Character = "heysip.duckling" });

        ui.Shell.OpenPackFile(Write(AccessoryManifest()));

        var card = ui.Shell.InstallPreview;
        Assert.Equal("New accessory · Hat", card.Eyebrow);
        Assert.Equal("Top hat", card.Title);
        Assert.Equal(3, card.Tiles.Count);
        Assert.All(card.Tiles, tile =>
        {
            Assert.Null(tile.Pack);
            Assert.Equal("heysip.duckling", tile.Character);
            Assert.Equal("tester.top-hat", Assert.Single(tile.Outfit!).Header.Id);
        });
    }

    [AvaloniaFact]
    public void The_card_tells_the_user_a_pack_cannot_run_programs()
    {
        using var ui = Harness();

        Assert.Contains("can't run programs", ui.Shell.InstallPreview.Reassurance);
    }

    [AvaloniaFact]
    public void Cancelling_installs_nothing_and_closes_the_card()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));

        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.False(IsInstalled(ui, "tester.blob"));
    }

    // ---- installing

    [AvaloniaFact]
    public void Installing_saves_the_character_and_offers_it_in_every_picker()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        var card = ui.Shell.InstallPreview;
        Assert.True(card.IsDone);
        Assert.Equal("Installed", card.Eyebrow);
        Assert.Equal("Blob is ready", card.Title);
        Assert.Equal("Use it now", card.UseLabel);
        Assert.True(IsInstalled(ui, "tester.blob"));
        Assert.True(CharacterCatalog.Contains("tester.blob"));
        Assert.Contains(ui.Shell.Settings.Characters, option => option.Id == "tester.blob" && option.Name == "Blob");
    }

    [AvaloniaFact]
    public void Use_it_now_switches_to_the_new_character_and_closes_the_card()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        ui.Shell.InstallPreview.UseNowCommand.Execute(null);

        Assert.Equal("tester.blob", ui.Get<SettingsService>().Current.Character);
        Assert.Equal("tester.blob", ui.Shell.Today.Character);
        Assert.False(ui.Shell.InstallPreview.IsVisible);
    }

    [AvaloniaFact]
    public void Not_now_keeps_the_current_character()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        Assert.Equal("heysip.drip", ui.Get<SettingsService>().Current.Character);
        Assert.True(CharacterCatalog.Contains("tester.blob"));
    }

    [AvaloniaFact]
    public void An_installed_accessory_appears_in_its_slot_and_can_be_worn_straight_away()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(AccessoryManifest()));

        ui.Shell.InstallPreview.InstallCommand.Execute(null);

        Assert.Equal("Wear it now", ui.Shell.InstallPreview.UseLabel);
        Assert.Contains(ui.Shell.Settings.Outfit[0].Options, option => option.Id == "tester.top-hat");

        ui.Shell.InstallPreview.UseNowCommand.Execute(null);

        Assert.Equal("tester.top-hat", ui.Get<SettingsService>().Current.Outfit["hat"]);
        Assert.Equal(["tester.top-hat"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));
    }

    [AvaloniaFact]
    public void A_new_version_says_it_is_an_update_and_an_old_one_cannot_be_installed_over_a_newer_one()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest(version: "1.0.0")));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        ui.Shell.OpenPackFile(Write(CharacterManifest(version: "1.1.0")));
        Assert.Equal("Update", ui.Shell.InstallPreview.InstallLabel);
        Assert.Contains("1.0.0", ui.Shell.InstallPreview.Note);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        ui.Shell.OpenPackFile(Write(CharacterManifest(version: "1.0.0"), "again.heysippack"));
        Assert.False(ui.Shell.InstallPreview.CanInstall);
        Assert.Contains("newer version (1.1.0)", ui.Shell.InstallPreview.Note);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        Assert.True(ui.Shell.InstallPreview.IsPreview); // nothing happened
        Assert.Equal("1.1.0", CharacterCatalog.Find("tester.blob").Pack.Header.Version);
    }

    [AvaloniaFact]
    public void Opening_what_is_already_installed_offers_to_reinstall()
    {
        using var ui = Harness();
        var path = Write(CharacterManifest());
        ui.Shell.OpenPackFile(path);
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        ui.Shell.OpenPackFile(path);

        Assert.Equal("Reinstall", ui.Shell.InstallPreview.InstallLabel);
        Assert.True(ui.Shell.InstallPreview.CanInstall);
    }

    // ---- files that cannot be installed

    [AvaloniaFact]
    public void A_file_that_is_not_a_pack_shows_why_and_offers_no_install()
    {
        using var ui = Harness();
        var path = Path.Combine(_files, "not-a-pack.heysippack");
        File.WriteAllText(path, "this is just text");

        ui.Shell.OpenPackFile(path);

        var card = ui.Shell.InstallPreview;
        Assert.True(card.IsVisible);
        Assert.True(card.IsFailed);
        Assert.Contains("isn't a valid pack file", card.ErrorText);
        Assert.Empty(card.Tiles);
        card.InstallCommand.Execute(null);
        Assert.True(card.IsFailed);
        Assert.False(Directory.Exists(CharactersDirectory(ui)) && Directory.GetFileSystemEntries(CharactersDirectory(ui)).Length > 0);
    }

    [AvaloniaFact]
    public void A_pack_that_breaks_a_rule_shows_the_rule()
    {
        using var ui = Harness();
        var manifest = CharacterManifest();
        ((JsonObject)manifest["anchors"]!).Remove("mouth");

        ui.Shell.OpenPackFile(Write(manifest));

        Assert.True(ui.Shell.InstallPreview.IsFailed);
        Assert.Contains("missing its mouth anchor", ui.Shell.InstallPreview.ErrorText);
    }

    [AvaloniaFact]
    public void A_pack_that_wants_a_built_in_id_is_refused()
    {
        using var ui = Harness();

        ui.Shell.OpenPackFile(Write(CharacterManifest(id: "heysip.drip")));

        Assert.True(ui.Shell.InstallPreview.IsFailed);
        Assert.Contains("reserved", ui.Shell.InstallPreview.ErrorText);
    }

    [AvaloniaFact]
    public void A_pack_for_a_newer_version_of_the_app_says_so()
    {
        using var ui = Harness();
        var manifest = CharacterManifest();
        manifest["minAppVersion"] = "99.0.0";

        ui.Shell.OpenPackFile(Write(manifest));

        Assert.Contains("needs HeySip 99.0.0", ui.Shell.InstallPreview.ErrorText);
    }

    [AvaloniaFact]
    public void A_missing_or_oversized_file_is_reported()
    {
        using var ui = Harness();

        ui.Shell.OpenPackFile(Path.Combine(_files, "nothing-here.heysippack"));
        Assert.Contains("wasn't found", ui.Shell.InstallPreview.ErrorText);

        var big = Path.Combine(_files, "big.heysippack");
        File.WriteAllBytes(big, new byte[PackLimits.MaxPackBytes + 1]);
        ui.Shell.OpenPackFile(big);
        Assert.Contains("too big", ui.Shell.InstallPreview.ErrorText);
    }

    [AvaloniaFact]
    public void A_folder_can_be_opened_like_a_file()
    {
        using var ui = Harness();
        var folder = Path.Combine(_files, "blob-folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pack.json"), CharacterManifest().ToJsonString());

        ui.Shell.OpenPackFile(folder);

        Assert.True(ui.Shell.InstallPreview.IsPreview);
        Assert.Equal("Blob", ui.Shell.InstallPreview.Title);
    }

    // ---- the card is modal

    [AvaloniaFact]
    public void While_the_card_is_up_shortcuts_cannot_switch_pages_and_escape_cancels_it()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));

        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.OpenPaletteCommand.Execute(null);

        Assert.True(ui.Shell.IsToday);
        Assert.False(ui.Shell.Palette.IsOpen);

        ui.Shell.DismissCommand.Execute(null);

        Assert.False(ui.Shell.InstallPreview.IsVisible);
        Assert.False(IsInstalled(ui, "tester.blob"));
    }

    [AvaloniaFact]
    public void A_pack_opened_during_the_welcome_waits_until_the_welcome_is_done()
    {
        using var ui = Harness(onboarded: false);
        ui.Shell.Onboarding.ShowIfFirstRun();
        var path = Write(CharacterManifest());

        ui.Shell.OpenPackFile(path);
        Assert.False(ui.Shell.InstallPreview.IsVisible);

        ui.Shell.Onboarding.SkipCommand.Execute(null);

        Assert.True(ui.Shell.InstallPreview.IsVisible);
        Assert.Equal("Blob", ui.Shell.InstallPreview.Title);
    }

    [AvaloniaFact]
    public void Escape_still_skips_the_welcome_before_anything_else()
    {
        using var ui = Harness(onboarded: false);
        ui.Shell.Onboarding.ShowIfFirstRun();

        ui.Shell.DismissCommand.Execute(null);

        Assert.False(ui.Shell.Onboarding.IsVisible);
    }

    // ---- choosing a file

    [AvaloniaFact]
    public async Task The_palette_and_settings_open_the_file_picker_and_then_the_card()
    {
        using var ui = Harness();
        var path = Write(CharacterManifest());
        var asked = 0;
        ui.Shell.PickPackFile = () =>
        {
            asked++;
            return Task.FromResult<string?>(path);
        };

        await ui.Shell.InstallFromFileAsync();

        Assert.Equal(1, asked);
        Assert.Equal("Blob", ui.Shell.InstallPreview.Title);

        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        ui.Shell.Palette.Open();
        Assert.Contains(ui.Shell.Palette.Results, item => item.Command.Id == "install.file");
        Assert.NotNull(ui.Shell.Settings.InstallPackCommand);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_file_picker_does_nothing()
    {
        using var ui = Harness();
        ui.Shell.PickPackFile = () => Task.FromResult<string?>(null);

        await ui.Shell.InstallFromFileAsync();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_file_picker_that_fails_shows_an_error_instead_of_crashing()
    {
        using var ui = Harness();
        ui.Shell.PickPackFile = () => throw new NotSupportedException();

        await ui.Shell.InstallFromFileAsync();

        Assert.True(ui.Shell.InstallPreview.IsFailed);
    }

    [AvaloniaFact]
    public async Task Without_a_window_to_ask_with_there_is_nothing_to_pick()
    {
        using var ui = Harness();
        ui.Shell.PickPackFile = null;

        await ui.Shell.InstallFromFileAsync();

        Assert.False(ui.Shell.InstallPreview.IsVisible);
    }

    // ---- removing what was installed

    [AvaloniaFact]
    public void Installed_packs_are_listed_in_settings_and_can_be_removed()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);
        ui.Shell.OpenPackFile(Write(AccessoryManifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.CloseCommand.Execute(null);

        Assert.True(ui.Shell.Settings.HasInstalled);
        Assert.Equal(["Blob", "Top hat"], ui.Shell.Settings.Installed.Select(item => item.Name));

        ui.Shell.Settings.Installed.Single(item => item.Id == "tester.blob").RemoveCommand.Execute(null);

        Assert.False(IsInstalled(ui, "tester.blob"));
        Assert.DoesNotContain(ui.Shell.Settings.Characters, option => option.Id == "tester.blob");
        Assert.Equal(["Top hat"], ui.Shell.Settings.Installed.Select(item => item.Name));

        ui.Shell.Settings.Installed.Single().RemoveCommand.Execute(null);

        Assert.False(ui.Shell.Settings.HasInstalled);
        Assert.DoesNotContain(ui.Shell.Settings.Outfit[0].Options, option => option.Id == "tester.top-hat");
    }

    [AvaloniaFact]
    public void Removing_the_character_in_use_falls_back_to_the_default_and_says_so()
    {
        using var ui = Harness();
        ui.Shell.OpenPackFile(Write(CharacterManifest()));
        ui.Shell.InstallPreview.InstallCommand.Execute(null);
        ui.Shell.InstallPreview.UseNowCommand.Execute(null);

        ui.Shell.Settings.Installed.Single().RemoveCommand.Execute(null);

        Assert.Equal("tester.blob", ui.Get<SettingsService>().Current.Character); // kept, in case the pack comes back
        Assert.Contains("isn't installed", ui.Shell.Settings.CharacterNote);
        Assert.Equal("heysip.drip", CharacterCatalog.Find("tester.blob").Id);
    }

    [AvaloniaFact]
    public void Nothing_installed_means_nothing_to_remove()
    {
        using var ui = Harness();

        Assert.False(ui.Shell.Settings.HasInstalled);
        Assert.Empty(ui.Shell.Settings.Installed);
    }

    // ---- opening a pack from the command line

    [Fact]
    public void A_pack_path_is_found_in_the_command_line()
    {
        Assert.Equal(@"C:\packs\blob.heysippack", PendingPack.FromArguments(["--minimized", @"C:\packs\blob.heysippack"]));
        Assert.Equal(@"C:\packs\BLOB.HEYSIPPACK", PendingPack.FromArguments([@"C:\packs\BLOB.HEYSIPPACK"]));
        Assert.Null(PendingPack.FromArguments(["--minimized", "notes.txt"]));
        Assert.Null(PendingPack.FromArguments([]));
    }

    [Fact]
    public void A_second_launch_hands_its_file_to_the_first_exactly_once()
    {
        using var data = new HeySip.Tests.Support.TempData();

        PendingPack.Write(data.Location, Path.Combine(_files, "blob.heysippack"));

        Assert.Equal(Path.Combine(_files, "blob.heysippack"), PendingPack.TryTake(data.Location));
        Assert.Null(PendingPack.TryTake(data.Location));
    }

    [Fact]
    public void Nothing_waiting_gives_nothing_to_open()
    {
        using var data = new HeySip.Tests.Support.TempData();

        Assert.Null(PendingPack.TryTake(data.Location));
    }
}
