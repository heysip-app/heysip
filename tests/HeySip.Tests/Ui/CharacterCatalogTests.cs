using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class CharacterCatalogTests : IDisposable
{
    private static readonly string[] BuiltInIds = ["heysip.drip", "heysip.minty", "heysip.sunny", "heysip.blossom", "heysip.axolotl", "heysip.duckling"];
    private static readonly int BuiltInCount = BuiltInIds.Length;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-catalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        CharacterCatalog.Configure(null); // the catalogue is shared by every test, so leave it as it was found
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Install(string id, string name, string deep = "#CC3344", string? folderName = null)
    {
        JsonArray Point(double x, double y) => new(x, y);
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["palette"] = new JsonObject { ["light"] = "#FFD0D8", ["deep"] = deep, ["outline"] = "#661122" },
            ["anchors"] = new JsonObject
            {
                ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
                ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
                ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
                ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
            },
            ["body"] = new JsonArray(new JsonObject
            {
                ["ellipse"] = new JsonArray(50, 90, 44, 38),
                ["fill"] = new JsonObject { ["gradient"] = new JsonArray("$light", "$deep") },
            }),
        };

        var folder = Path.Combine(_root, folderName ?? id, "1.0.0");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "pack.json"), Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }

    private static UiHarness Harness()
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    private static byte[] Render(DripControl drip)
    {
        drip.IsAnimated = false;
        drip.Measure(new Size(100, 140));
        drip.Arrange(new Rect(0, 0, 100, 140));
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 140));
        bitmap.Render(drip);
        using var stream = new MemoryStream();
#pragma warning disable CS0618
        bitmap.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void Without_installed_packs_the_catalogue_lists_the_built_in_characters()
    {
        CharacterCatalog.Configure(Path.Combine(_root, "none"));

        Assert.Equal(BuiltInIds, CharacterCatalog.All.Select(entry => entry.Id));
        Assert.All(CharacterCatalog.All, entry => Assert.True(entry.BuiltIn));
        Assert.Equal("heysip.drip", CharacterCatalog.Default.Id);
    }

    [AvaloniaFact]
    public void Installed_characters_follow_the_built_in_ones()
    {
        Install("tester.zebra", "Zebra");
        Install("tester.apple", "Apple");

        CharacterCatalog.Configure(_root);

        Assert.Equal(
            [.. BuiltInIds, "tester.apple", "tester.zebra"],
            CharacterCatalog.All.Select(entry => entry.Id));
        Assert.False(CharacterCatalog.Find("tester.apple").BuiltIn);
        Assert.Equal("Apple", CharacterCatalog.Find("tester.apple").Name);
    }

    [AvaloniaFact]
    public void An_id_that_cannot_be_found_gives_the_default_character()
    {
        CharacterCatalog.Configure(_root);

        Assert.Equal("heysip.drip", CharacterCatalog.Find("nobody.here").Id);
        Assert.Equal("heysip.drip", CharacterCatalog.Find(null).Id);
        Assert.False(CharacterCatalog.Contains("nobody.here"));
        Assert.True(CharacterCatalog.Contains("heysip.sunny"));
    }

    [AvaloniaFact]
    public void A_broken_installed_pack_never_stops_the_built_in_ones_loading()
    {
        Install("tester.good", "Good");
        var broken = Path.Combine(_root, "tester.bad", "1.0.0");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "pack.json"), "{ not json");

        CharacterCatalog.Configure(_root);

        Assert.Equal(BuiltInCount + 1, CharacterCatalog.All.Count);
        Assert.False(CharacterCatalog.Contains("tester.bad"));
    }

    [AvaloniaFact]
    public void An_installed_pack_cannot_take_the_place_of_a_built_in_character()
    {
        Install("heysip.drip", "Impostor", folderName: "heysip.drip");

        CharacterCatalog.Configure(_root);

        Assert.Equal("Drip", CharacterCatalog.Find("heysip.drip").Name);
        Assert.Equal(BuiltInCount, CharacterCatalog.All.Count);
    }

    [AvaloniaFact]
    public void Reconfiguring_picks_up_a_newly_installed_pack()
    {
        CharacterCatalog.Configure(_root);
        Assert.Equal(BuiltInCount, CharacterCatalog.All.Count);

        Install("tester.late", "Late");
        var changes = 0;
        void OnChanged() => changes++;
        CharacterCatalog.Changed += OnChanged;
        try
        {
            CharacterCatalog.Configure(_root);
        }
        finally
        {
            CharacterCatalog.Changed -= OnChanged;
        }

        Assert.Equal(BuiltInCount + 1, CharacterCatalog.All.Count);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void The_settings_and_welcome_pickers_offer_installed_characters_and_choosing_one_saves_its_id()
    {
        Install("tester.blob", "Blob");
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        var settingsPick = ui.Shell.Settings.Characters.Single(option => option.Id == "tester.blob");
        Assert.Equal("Blob", settingsPick.Name);
        settingsPick.SelectCommand.Execute(null);

        Assert.Equal("tester.blob", ui.Get<SettingsService>().Current.Character);
        Assert.True(settingsPick.IsSelected);
        Assert.Equal("tester.blob", ui.Shell.Today.Character);
        Assert.Contains(ui.Shell.Onboarding.Characters, option => option.Id == "tester.blob");
    }

    [AvaloniaFact]
    public void The_palette_offers_installed_characters_by_name()
    {
        Install("tester.blob", "Blob");
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "Character: Blob";
        var command = ui.Shell.Palette.Results.First(result => result.Title == "Character: Blob");
        command.RunCommand.Execute(null);

        Assert.Equal("tester.blob", ui.Get<SettingsService>().Current.Character);
    }

    [AvaloniaFact]
    public void A_saved_character_that_is_not_installed_is_drawn_as_the_default_and_the_settings_page_says_so()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Get<SettingsService>().Update(s => s with { Character = "gone.missing" });

        Assert.Equal("gone.missing", ui.Get<SettingsService>().Current.Character); // the choice is kept, in case the pack comes back
        Assert.Contains("gone.missing", ui.Shell.Settings.CharacterNote);
        Assert.Contains("Drip", ui.Shell.Settings.CharacterNote);
        Assert.DoesNotContain(ui.Shell.Settings.Characters, option => option.IsSelected);
        Assert.Equal(Render(new DripControl { Character = "heysip.drip" }), Render(new DripControl { Character = "gone.missing" }));

        ui.Get<SettingsService>().Update(s => s with { Character = "heysip.minty" });
        Assert.Equal("", ui.Shell.Settings.CharacterNote);
    }

    [AvaloniaFact]
    public void The_character_comes_back_when_its_pack_is_installed_again()
    {
        CharacterCatalog.Configure(_root);
        var missing = Render(new DripControl { Character = "tester.blob" });
        Assert.Equal(Render(new DripControl { Character = "heysip.drip" }), missing);

        Install("tester.blob", "Blob");
        CharacterCatalog.Configure(_root);

        Assert.NotEqual(missing, Render(new DripControl { Character = "tester.blob" }));
    }

    [AvaloniaFact]
    public void Each_installed_character_draws_in_its_own_colours()
    {
        Install("tester.red", "Red", deep: "#CC3344");
        Install("tester.blue", "Blue", deep: "#3344CC");
        CharacterCatalog.Configure(_root);

        Assert.NotEqual(Render(new DripControl { Character = "tester.red" }), Render(new DripControl { Character = "tester.blue" }));
    }
}
