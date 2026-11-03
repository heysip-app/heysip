using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.Tests.Ui;

public sealed class OutfitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-outfit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        CharacterCatalog.Configure(null);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static UiHarness Harness()
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    private static void Wear(UiHarness ui, AccessorySlot slot, string? id) =>
        ui.Get<SettingsService>().Update(s => s.WithAccessory(slot, id));

    private void InstallAccessory(string id, string name, string slot)
    {
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "accessory",
            ["id"] = id,
            ["name"] = name,
            ["version"] = "1.0.0",
            ["slot"] = slot,
            ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-12,0 L0,-26 L12,0 Z", ["fill"] = "#22AA55" }),
        };
        var folder = Path.Combine(_root, id, "1.0.0");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "pack.json"), Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }

    private static byte[] Png(DripControl drip, int width = 100, int height = 140)
    {
        drip.IsAnimated = false;
        drip.Measure(new Size(width, height));
        drip.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(drip);
        using var stream = new MemoryStream();
#pragma warning disable CS0618
        bitmap.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    /// <summary>The picture's alpha channel, one byte per pixel, row by row.</summary>
    private static byte[] Alpha(DripControl drip, int width = 100, int height = 140)
    {
        using var decoded = new Bitmap(new MemoryStream(Png(drip, width, height)));
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            decoded.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return [.. Enumerable.Range(0, width * height).Select(i => pixels[(i * 4) + 3])];
    }

    // ---- the accessories that come with the app

    [AvaloniaFact]
    public void Nine_accessories_come_with_the_app_in_all_four_slots()
    {
        CharacterCatalog.Configure(_root);

        Assert.Equal(9, CharacterCatalog.Accessories.Count);
        Assert.All(CharacterCatalog.Accessories, entry =>
        {
            Assert.True(entry.BuiltIn);
            Assert.StartsWith("heysip.", entry.Id);
            Assert.Equal(PackTier.Free, entry.Pack.Header.Tier);
            Assert.Equal(entry.Slot, entry.Pack.Slot);
        });
        Assert.Equal(3, CharacterCatalog.AccessoriesFor(AccessorySlot.Hat).Count);
        Assert.Equal(2, CharacterCatalog.AccessoriesFor(AccessorySlot.Eyes).Count);
        Assert.Equal(2, CharacterCatalog.AccessoriesFor(AccessorySlot.Neck).Count);
        Assert.Equal(2, CharacterCatalog.AccessoriesFor(AccessorySlot.Held).Count);
        Assert.Equal(CharacterCatalog.Accessories.Count, CharacterCatalog.Accessories.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(CharacterCatalog.Accessories.Count, CharacterCatalog.Accessories.Select(entry => entry.Name).Distinct().Count());
    }

    [AvaloniaFact]
    public void An_installed_accessory_joins_the_list_but_cannot_replace_a_built_in_one()
    {
        InstallAccessory("tester.top-hat", "Top hat", "hat");
        InstallAccessory("heysip.crown", "Impostor crown", "hat");

        CharacterCatalog.Configure(_root);

        Assert.Equal("Top hat", CharacterCatalog.FindAccessory("tester.top-hat")!.Name);
        Assert.False(CharacterCatalog.FindAccessory("tester.top-hat")!.BuiltIn);
        Assert.Equal("Crown", CharacterCatalog.FindAccessory("heysip.crown")!.Name);
        Assert.Equal(10, CharacterCatalog.Accessories.Count);
    }

    [AvaloniaFact]
    public void An_installed_accessory_does_not_show_up_as_a_character()
    {
        InstallAccessory("tester.top-hat", "Top hat", "hat");

        CharacterCatalog.Configure(_root);

        Assert.False(CharacterCatalog.Contains("tester.top-hat"));
    }

    [AvaloniaFact]
    public void A_saved_outfit_means_one_accessory_per_slot_and_skips_what_it_cannot_find()
    {
        CharacterCatalog.Configure(_root);
        var outfit = new Dictionary<string, string>
        {
            ["hat"] = "heysip.crown",
            ["eyes"] = "heysip.sunglasses",
            ["neck"] = "gone.missing",
            ["held"] = "heysip.crown", // a hat saved in the wrong slot
        };

        var worn = CharacterCatalog.Wearing(outfit);

        Assert.Equal(["heysip.crown", "heysip.sunglasses"], worn.Select(pack => pack.Header.Id));
        Assert.Empty(CharacterCatalog.Wearing(new Dictionary<string, string>()));
    }

    // ---- choosing an outfit in Settings

    [AvaloniaFact]
    public void Settings_offers_every_slot_with_none_first_and_what_fits_after_it()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        var slots = ui.Shell.Settings.Outfit;

        Assert.Equal(["Hat", "Glasses", "Scarf and ties", "Holding"], slots.Select(slot => slot.Title));
        Assert.All(slots, slot =>
        {
            Assert.Null(slot.Options[0].Id);
            Assert.Equal("None", slot.Options[0].Name);
            Assert.True(slot.Options[0].IsSelected); // nothing worn yet
            Assert.All(slot.Options.Skip(1), option => Assert.False(option.IsSelected));
        });
        Assert.Equal(4, slots[0].Options.Count); // none and three hats
    }

    [AvaloniaFact]
    public void Choosing_an_accessory_wears_it_and_choosing_none_takes_it_off()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();
        var hats = ui.Shell.Settings.Outfit.Single(slot => slot.Slot == AccessorySlot.Hat);

        hats.Options.Single(option => option.Id == "heysip.crown").SelectCommand.Execute(null);

        Assert.Equal("heysip.crown", ui.Get<SettingsService>().Current.Outfit["hat"]);
        Assert.True(hats.Options.Single(option => option.Id == "heysip.crown").IsSelected);
        Assert.False(hats.Options[0].IsSelected);

        hats.Options[0].SelectCommand.Execute(null);

        Assert.Empty(ui.Get<SettingsService>().Current.Outfit);
        Assert.True(hats.Options[0].IsSelected);
    }

    [AvaloniaFact]
    public void Each_slot_is_chosen_on_its_own_so_a_hat_and_glasses_can_go_together()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Shell.Settings.Outfit[0].Options.Single(option => option.Id == "heysip.beanie").SelectCommand.Execute(null);
        ui.Shell.Settings.Outfit[1].Options.Single(option => option.Id == "heysip.sunglasses").SelectCommand.Execute(null);
        ui.Shell.Settings.Outfit[0].Options.Single(option => option.Id == "heysip.party-hat").SelectCommand.Execute(null);

        var outfit = ui.Get<SettingsService>().Current.Outfit;
        Assert.Equal("heysip.party-hat", outfit["hat"]);
        Assert.Equal("heysip.sunglasses", outfit["eyes"]);
    }

    [AvaloniaFact]
    public void A_saved_accessory_that_is_not_installed_shows_none_as_selected()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        Wear(ui, AccessorySlot.Hat, "gone.missing");

        var hats = ui.Shell.Settings.Outfit[0];
        Assert.True(hats.Options[0].IsSelected);
        Assert.Equal("gone.missing", ui.Get<SettingsService>().Current.Outfit["hat"]); // kept, in case the pack comes back
    }

    [AvaloniaFact]
    public void The_previews_are_drawn_on_the_character_you_have()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Get<SettingsService>().Update(s => s with { Character = "heysip.axolotl" });

        Assert.All(ui.Shell.Settings.Outfit.SelectMany(slot => slot.Options), option => Assert.Equal("heysip.axolotl", option.Character));
    }

    [AvaloniaFact]
    public void Installed_accessories_appear_in_their_slot()
    {
        InstallAccessory("tester.top-hat", "Top hat", "hat");
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        Assert.Contains(ui.Shell.Settings.Outfit[0].Options, option => option.Id == "tester.top-hat" && option.Name == "Top hat");
    }

    [AvaloniaFact]
    public void The_outfit_can_be_found_by_searching_settings()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        foreach (var word in new[] { "outfit", "hat", "glasses", "scarf", "balloon", "dress up" })
        {
            ui.Shell.OpenSettings(word);
            Assert.True(ui.Shell.Settings.ShowOutfit, word);
            Assert.True(ui.Shell.Settings.ShowCompanionCard, word);
        }

        ui.Shell.OpenSettings("interval");
        Assert.False(ui.Shell.Settings.ShowOutfit);
    }

    // ---- wearing it

    [AvaloniaFact]
    public void Today_and_the_popup_wear_the_chosen_outfit_and_it_follows_changes()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();
        Assert.Empty(ui.Shell.Today.Outfit);

        Wear(ui, AccessorySlot.Hat, "heysip.crown");
        Wear(ui, AccessorySlot.Neck, "heysip.scarf");

        Assert.Equal(["heysip.crown", "heysip.scarf"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));

        Wear(ui, AccessorySlot.Hat, null);

        Assert.Equal(["heysip.scarf"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));
    }

    [AvaloniaFact]
    public void Each_character_wears_its_own_outfit_and_switching_back_restores_it()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();
        Wear(ui, AccessorySlot.Eyes, "heysip.round-glasses");

        settings.Update(s => s with { Character = "heysip.duckling" });

        Assert.Equal("heysip.duckling", ui.Shell.Today.Character);
        Assert.Empty(ui.Shell.Today.Outfit); // the duckling has nothing on yet

        Wear(ui, AccessorySlot.Hat, "heysip.crown");
        Assert.Equal(["heysip.crown"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));

        settings.Update(s => s with { Character = PackId.DefaultCharacter });

        Assert.Equal(["heysip.round-glasses"], ui.Shell.Today.Outfit.Select(pack => pack.Header.Id));
    }

    [AvaloniaFact]
    public void Wearing_something_changes_the_drawing_for_every_character_and_accessory()
    {
        CharacterCatalog.Configure(_root);
        foreach (var character in CharacterCatalog.All)
        {
            var bare = Png(new DripControl { Character = character.Id });
            foreach (var accessory in CharacterCatalog.Accessories)
            {
                var worn = Png(new DripControl { Character = character.Id, Outfit = [accessory.Pack] });

                Assert.NotEqual(bare, worn);
            }
        }
    }

    [AvaloniaFact]
    public void Nothing_a_character_wears_is_cut_off_by_the_edge_of_its_picture()
    {
        CharacterCatalog.Configure(_root);
        const int width = 100;
        const int height = 140;
        var problems = new List<string>();

        void Check(string label, DripControl drip)
        {
            var alpha = Alpha(drip, width, height);
            bool Touches(IEnumerable<int> indexes) => indexes.Any(index => alpha[index] > 8);

            if (Touches(Enumerable.Range(0, width)))
            {
                problems.Add($"{label}: the top is cut off");
            }

            if (Touches(Enumerable.Range(0, height).Select(y => y * width)))
            {
                problems.Add($"{label}: the left is cut off");
            }

            if (Touches(Enumerable.Range(0, height).Select(y => (y * width) + width - 1)))
            {
                problems.Add($"{label}: the right is cut off");
            }
        }

        foreach (var character in CharacterCatalog.All)
        {
            Check($"{character.Name} bare", new DripControl { Character = character.Id });

            foreach (var accessory in CharacterCatalog.Accessories)
            {
                foreach (var pose in new[] { DripPose.Idle, DripPose.Waving, DripPose.Cheering })
                {
                    Check($"{character.Name} with {accessory.Name} ({pose})", new DripControl { Character = character.Id, Pose = pose, Outfit = [accessory.Pack] });
                }
            }

            Check($"{character.Name} fully dressed", new DripControl
            {
                Character = character.Id,
                Pose = DripPose.Waving,
                Outfit = [.. CharacterCatalog.Wearing(new Dictionary<string, string>
                {
                    ["hat"] = "heysip.party-hat", ["eyes"] = "heysip.sunglasses", ["neck"] = "heysip.scarf", ["held"] = "heysip.balloon",
                })],
            });
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [AvaloniaFact]
    public void A_character_wearing_a_tall_hat_is_drawn_a_little_smaller_so_it_all_fits()
    {
        CharacterCatalog.Configure(_root);
        var bare = Alpha(new DripControl { Character = "heysip.drip" });
        var hatted = Alpha(new DripControl { Character = "heysip.drip", Outfit = [CharacterCatalog.FindAccessory("heysip.party-hat")!.Pack] });

        // The feet stay on the ground line, but the character itself takes up less of the picture.
        static int Width(byte[] alpha, int row) => Enumerable.Range(0, 100).Count(x => alpha[(row * 100) + x] > 8);
        Assert.True(Width(hatted, 100) < Width(bare, 100), "the body should be narrower once it makes room for the hat");
    }

    [AvaloniaFact]
    public void A_held_item_goes_where_the_hand_is()
    {
        CharacterCatalog.Configure(_root);
        var balloon = CharacterCatalog.FindAccessory("heysip.balloon")!.Pack;

        var resting = Png(new DripControl { Character = "heysip.drip", Pose = DripPose.Idle, Outfit = [balloon] });
        var raised = Png(new DripControl { Character = "heysip.drip", Pose = DripPose.Happy, Outfit = [balloon] });

        Assert.NotEqual(resting, raised);
    }

    // ---- the palette

    [AvaloniaFact]
    public void The_palette_puts_accessories_on_and_takes_them_off()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "Wear: Crown";
        ui.Shell.Palette.Results.First(result => result.Title == "Wear: Crown").RunCommand.Execute(null);

        Assert.Equal("heysip.crown", ui.Get<SettingsService>().Current.Outfit["hat"]);

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "Crown";
        Assert.DoesNotContain(ui.Shell.Palette.Results, result => result.Title == "Wear: Crown"); // already worn
        ui.Shell.Palette.Results.First(result => result.Title == "Take off: Crown").RunCommand.Execute(null);

        Assert.Empty(ui.Get<SettingsService>().Current.Outfit);
    }

    [AvaloniaFact]
    public void The_palette_has_nothing_to_take_off_when_nothing_is_worn()
    {
        CharacterCatalog.Configure(_root);
        using var ui = Harness();

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "Take off";

        Assert.DoesNotContain(ui.Shell.Palette.Results, result => result.Title.StartsWith("Take off", StringComparison.Ordinal));
    }
}
