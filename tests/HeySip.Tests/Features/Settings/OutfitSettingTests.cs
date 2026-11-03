using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class OutfitSettingTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    private string SettingsFile => _data.Location.SettingsPath;

    private AppSettings LoadFile(string json)
    {
        File.WriteAllText(SettingsFile, json);
        var settings = new SettingsService(_data.Location);
        settings.Load();
        return settings.Current;
    }

    [Fact]
    public void Nobody_wears_anything_to_begin_with()
    {
        Assert.Empty(new AppSettings().Outfit);
    }

    [Fact]
    public void A_file_from_before_outfits_loads_with_nothing_worn()
    {
        Assert.Empty(LoadFile("{ \"SchemaVersion\": 2, \"Name\": \"Kai\" }").Outfit);
        Assert.Empty(LoadFile("{ \"SchemaVersion\": 2, \"Outfit\": null }").Outfit);
    }

    [Fact]
    public void An_accessory_can_be_put_on_and_taken_off_one_slot_at_a_time()
    {
        var bare = new AppSettings();

        var hat = bare.WithAccessory(AccessorySlot.Hat, "heysip.crown");
        var both = hat.WithAccessory(AccessorySlot.Eyes, "heysip.sunglasses");
        var swapped = both.WithAccessory(AccessorySlot.Hat, "heysip.beanie");
        var hatOff = swapped.WithAccessory(AccessorySlot.Hat, null);

        Assert.Equal("heysip.crown", hat.Outfit["hat"]);
        Assert.Equal(2, both.Outfit.Count);
        Assert.Equal("heysip.beanie", swapped.Outfit["hat"]);
        Assert.Equal("heysip.sunglasses", swapped.Outfit["eyes"]);
        Assert.Equal(["eyes"], hatOff.Outfit.Keys);
        Assert.Empty(bare.Outfit); // the original is never changed
    }

    [Fact]
    public void Taking_off_what_is_not_worn_changes_nothing()
    {
        Assert.Empty(new AppSettings().WithAccessory(AccessorySlot.Neck, null).Outfit);
    }

    [Fact]
    public void The_outfit_survives_a_restart_and_is_written_by_slot_name()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s.WithAccessory(AccessorySlot.Hat, "heysip.party-hat").WithAccessory(AccessorySlot.Held, "heysip.balloon"));

        var second = new SettingsService(_data.Location);
        second.Load();

        Assert.Equal("heysip.party-hat", second.Current.Outfit["hat"]);
        Assert.Equal("heysip.balloon", second.Current.Outfit["held"]);
        var text = File.ReadAllText(SettingsFile);
        Assert.Contains("\"hat\": \"heysip.party-hat\"", text);
        Assert.Contains("\"held\": \"heysip.balloon\"", text);
    }

    [Fact]
    public void An_accessory_from_an_installed_pack_is_kept_whether_or_not_it_is_installed()
    {
        var loaded = LoadFile("{ \"SchemaVersion\": 2, \"Outfit\": { \"hat\": \"alice.top-hat\" } }");

        Assert.Equal("alice.top-hat", loaded.Outfit["hat"]);
    }

    [Fact]
    public void Slots_that_do_not_exist_and_ids_that_are_not_ids_are_dropped()
    {
        var loaded = LoadFile("""
            { "SchemaVersion": 2, "Outfit": {
                "hat": "heysip.crown",
                "tail": "heysip.balloon",
                "eyes": "Not An Id",
                "neck": "../../evil",
                "held": "" } }
            """);

        Assert.Equal(["hat"], loaded.Outfit.Keys);
    }

    [Fact]
    public void Changing_only_the_outfit_counts_as_a_change_and_the_same_outfit_does_not()
    {
        var bare = new AppSettings();
        var crowned = bare.WithAccessory(AccessorySlot.Hat, "heysip.crown");

        Assert.False(bare.SameAs(crowned));
        Assert.True(crowned.SameAs(bare.WithAccessory(AccessorySlot.Hat, "heysip.crown")));
        Assert.False(crowned.SameAs(bare.WithAccessory(AccessorySlot.Hat, "heysip.beanie")));
        Assert.False(crowned.SameAs(bare.WithAccessory(AccessorySlot.Eyes, "heysip.crown")));
    }

    [Fact]
    public void Wearing_the_same_thing_again_raises_no_change()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();
        settings.Update(s => s.WithAccessory(AccessorySlot.Hat, "heysip.crown"));
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.Update(s => s.WithAccessory(AccessorySlot.Hat, "heysip.crown"));

        Assert.Equal(0, raised);
    }

    // ---- each character keeps its own outfit

    [Fact]
    public void Each_character_keeps_its_own_outfit_when_you_switch()
    {
        var minty = new AppSettings { Character = "heysip.minty" }.WithAccessory(AccessorySlot.Hat, "heysip.crown");

        var drip = minty with { Character = "heysip.drip" };
        Assert.Empty(drip.Outfit); // Drip wears nothing, although Minty has a crown

        var dripDressed = drip.WithAccessory(AccessorySlot.Eyes, "heysip.sunglasses");
        var backToMinty = dripDressed with { Character = "heysip.minty" };

        Assert.Equal(["hat"], backToMinty.Outfit.Keys);
        Assert.Equal("heysip.crown", backToMinty.Outfit["hat"]);
        Assert.Equal("heysip.sunglasses", backToMinty.OutfitOf("heysip.drip")["eyes"]);
    }

    [Fact]
    public void A_character_that_is_not_in_use_can_be_dressed_without_changing_the_one_that_is()
    {
        var settings = new AppSettings { Character = "heysip.drip" }.WithAccessory("heysip.minty", AccessorySlot.Hat, "heysip.beanie");

        Assert.Empty(settings.Outfit);
        Assert.Equal("heysip.beanie", settings.OutfitOf("heysip.minty")["hat"]);
    }

    [Fact]
    public void Taking_off_the_last_accessory_leaves_no_entry_for_that_character()
    {
        var dressed = new AppSettings { Character = "heysip.minty" }.WithAccessory(AccessorySlot.Hat, "heysip.crown");

        var bare = dressed.WithAccessory(AccessorySlot.Hat, null);

        Assert.Empty(bare.Outfits);
        Assert.True(bare.SameAs(new AppSettings { Character = "heysip.minty" }));
    }

    [Fact]
    public void Dressing_one_character_counts_as_a_change_even_when_another_wears_the_same_thing()
    {
        var minty = new AppSettings { Character = "heysip.minty" }.WithAccessory(AccessorySlot.Hat, "heysip.crown");
        var drip = new AppSettings { Character = "heysip.minty" }.WithAccessory("heysip.drip", AccessorySlot.Hat, "heysip.crown");

        Assert.False(minty.SameAs(drip));
        Assert.True(minty.SameAs(new AppSettings { Character = "heysip.minty" }.WithAccessory(AccessorySlot.Hat, "heysip.crown")));
    }

    [Fact]
    public void Every_characters_outfit_survives_a_restart()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s.WithAccessory("heysip.drip", AccessorySlot.Hat, "heysip.crown").WithAccessory("heysip.minty", AccessorySlot.Neck, "heysip.scarf"));

        var second = new SettingsService(_data.Location);
        second.Load();

        Assert.Equal("heysip.crown", second.Current.OutfitOf("heysip.drip")["hat"]);
        Assert.Equal("heysip.scarf", second.Current.OutfitOf("heysip.minty")["neck"]);
    }

    [Fact]
    public void Outfits_for_a_character_id_that_is_not_an_id_are_dropped()
    {
        var loaded = LoadFile("""
            { "SchemaVersion": 3, "Outfits": {
                "heysip.minty": { "hat": "heysip.crown" },
                "../../evil": { "hat": "heysip.crown" },
                "heysip.drip": { "tail": "heysip.balloon" } } }
            """);

        Assert.Equal(["heysip.minty"], loaded.Outfits.Keys);
    }

    [Fact]
    public void The_one_outfit_from_an_older_file_goes_to_the_character_in_use_and_the_old_entry_is_not_written_back()
    {
        var loaded = LoadFile("""
            { "SchemaVersion": 2, "Character": "heysip.minty", "Outfit": { "hat": "heysip.crown", "held": "heysip.balloon" } }
            """);

        Assert.Equal("heysip.minty", loaded.Character);
        Assert.Equal("heysip.crown", loaded.OutfitOf("heysip.minty")["hat"]);
        Assert.Equal("heysip.balloon", loaded.OutfitOf("heysip.minty")["held"]);
        Assert.Empty(loaded.OutfitOf("heysip.drip"));
        Assert.Null(loaded.LegacyOutfit);

        var text = File.ReadAllText(SettingsFile);
        Assert.Contains("\"Outfits\"", text);
        Assert.DoesNotContain("\"Outfit\"", text);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void An_older_file_with_no_outfit_upgrades_with_nobody_wearing_anything()
    {
        var loaded = LoadFile("{ \"SchemaVersion\": 2, \"Character\": \"heysip.minty\" }");

        Assert.Empty(loaded.Outfits);
    }

    [Fact]
    public void Slots_have_stable_names_and_readable_titles()
    {
        Assert.Equal(["hat", "eyes", "neck", "held"], OutfitSlots.All.Select(OutfitSlots.KeyOf));
        Assert.True(OutfitSlots.IsSlot("hat"));
        Assert.False(OutfitSlots.IsSlot("tail"));
        Assert.False(OutfitSlots.IsSlot(null));
        Assert.Equal("Hat", OutfitSlots.TitleOf(AccessorySlot.Hat));
        Assert.Equal("Glasses", OutfitSlots.TitleOf(AccessorySlot.Eyes));
    }
}
