using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;

namespace HeySip.Tests.Features.Companion;

public sealed class StoreCatalogTests
{
    private static JsonObject Item(string id = "tester.blob", string kind = "character", string version = "1.0.0") => new()
    {
        ["id"] = id,
        ["kind"] = kind,
        ["name"] = "Blob",
        ["author"] = "Tester",
        ["version"] = version,
        ["description"] = "A blob.",
        ["priceCents"] = 0,
        ["pack"] = "packs/blob.heysippack",
    };

    private static StoreCatalogResult Parse(params JsonNode?[] items) =>
        StoreCatalog.Parse(Encoding.UTF8.GetBytes(new JsonObject { ["formatVersion"] = 1, ["items"] = new JsonArray(items) }.ToJsonString()));

    [Fact]
    public void A_good_entry_is_read_with_every_field()
    {
        var item = Item();
        item["priceCents"] = 250;
        item["size"] = 4096;
        item["sha256"] = new string('A', 64);
        item["buyUrl"] = "https://example.com/buy/blob";

        var result = Parse(item);

        Assert.True(result.Ok);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(new StoreEntry("tester.blob", PackKind.Character, "Blob", "Tester", "1.0.0", "A blob.", 250, "packs/blob.heysippack", new string('a', 64), 4096, "https://example.com/buy/blob"), entry);
        Assert.False(entry.IsFree);
    }

    [Fact]
    public void A_character_can_list_the_accessories_that_come_with_it()
    {
        var item = Item();
        item["accessories"] = new JsonArray("tester.top-hat", "tester.scarf");

        var entry = Assert.Single(Parse(item).Entries);

        Assert.Equal(["tester.top-hat", "tester.scarf"], entry.Included);
    }

    [Fact]
    public void An_entry_with_no_accessories_comes_with_none()
    {
        Assert.Empty(Assert.Single(Parse(Item()).Entries).Included);
        Assert.Empty(Assert.Single(Parse(WithAccessories(Item(), new JsonArray())).Entries).Included);
    }

    [Fact]
    public void An_accessory_cannot_list_accessories_of_its_own()
    {
        var item = Item("tester.top-hat", kind: "accessory");
        item["accessories"] = new JsonArray("tester.scarf");

        Assert.Empty(Assert.Single(Parse(item).Entries).Included);
    }

    [Theory]
    [InlineData("\"Not An Id\"")]
    [InlineData("\"../../evil\"")]
    [InlineData("\"tester.blob\"")] // itself
    [InlineData("42")]
    public void A_character_with_a_bad_accessory_id_is_skipped_whole(string badJson)
    {
        var accessories = new JsonArray("tester.top-hat", JsonNode.Parse(badJson));

        var result = Parse(WithAccessories(Item(), accessories));

        Assert.Empty(result.Entries);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void A_repeated_accessory_or_a_list_that_is_not_a_list_skips_the_character()
    {
        Assert.Empty(Parse(WithAccessories(Item(), new JsonArray("tester.top-hat", "tester.top-hat"))).Entries);

        var notAList = Item();
        notAList["accessories"] = "tester.top-hat";
        Assert.Empty(Parse(notAList).Entries);
    }

    [Fact]
    public void A_character_cannot_come_with_more_accessories_than_the_limit()
    {
        static JsonArray Hats(int count) => new([.. Enumerable.Range(0, count).Select(n => (JsonNode?)JsonValue.Create($"tester.hat-{n}"))]);

        Assert.Empty(Parse(WithAccessories(Item(), Hats(StoreCatalog.MaxAccessoriesPerCharacter + 1))).Entries);
        Assert.Single(Parse(WithAccessories(Item(), Hats(StoreCatalog.MaxAccessoriesPerCharacter))).Entries);
    }

    private static JsonObject WithAccessories(JsonObject item, JsonArray accessories)
    {
        item["accessories"] = accessories;
        return item;
    }

    [Fact]
    public void An_entry_needs_only_what_identifies_it()
    {
        var result = Parse(new JsonObject { ["id"] = "heysip.drip", ["kind"] = "character", ["name"] = "Drip", ["version"] = "1.0.0" });

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.IsFree);
        Assert.Null(entry.PackLocation);
        Assert.Equal("", entry.Author);
    }

    [Theory]
    [InlineData("id", "Not An Id")]
    [InlineData("kind", "hat")]
    [InlineData("name", "   ")]
    [InlineData("version", "1.0")]
    [InlineData("priceCents", -1)]
    [InlineData("priceCents", 100001)]
    [InlineData("priceCents", 2.5)]
    [InlineData("size", 2000000)]
    [InlineData("sha256", "not-a-hash")]
    [InlineData("pack", "../../secret.zip")]
    [InlineData("buyUrl", "http://example.com/plain")]
    [InlineData("buyUrl", "javascript:alert(1)")]
    [InlineData("buyUrl", "file:///c:/windows")]
    public void An_entry_with_a_bad_value_is_left_out_and_counted(string field, object value)
    {
        var item = Item();
        item[field] = System.Text.Json.JsonSerializer.SerializeToNode(value);

        var result = Parse(item, Item(id: "tester.fine"));

        Assert.True(result.Ok);
        Assert.Equal(["tester.fine"], result.Entries.Select(entry => entry.Id));
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void The_same_id_listed_twice_keeps_the_first()
    {
        var second = Item();
        second["name"] = "Impostor";

        var result = Parse(Item(), second);

        Assert.Equal("Blob", Assert.Single(result.Entries).Name);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Entries_that_are_not_objects_are_skipped()
    {
        var result = Parse(JsonValue.Create(5), null, new JsonArray(), Item());

        Assert.Single(result.Entries);
        Assert.Equal(3, result.Skipped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":\"1\",\"items\":[]}")]
    [InlineData("{\"formatVersion\":2,\"items\":[]}")]
    [InlineData("{\"formatVersion\":1}")]
    [InlineData("{\"formatVersion\":1,\"items\":{}}")]
    public void A_list_in_the_wrong_shape_is_refused_without_throwing(string json)
    {
        var result = StoreCatalog.Parse(Encoding.UTF8.GetBytes(json));

        Assert.False(result.Ok);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void A_list_with_a_byte_order_mark_is_accepted()
    {
        var json = new JsonObject { ["formatVersion"] = 1, ["items"] = new JsonArray(Item()) }.ToJsonString();

        var result = StoreCatalog.Parse(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray());

        Assert.Single(result.Entries);
    }

    [Fact]
    public void A_huge_list_is_refused_and_a_long_one_is_cut_off()
    {
        Assert.False(StoreCatalog.Parse(new byte[StoreCatalog.MaxBytes + 1]).Ok);

        var many = Enumerable.Range(0, StoreCatalog.MaxEntries + 20).Select(i => (JsonNode?)Item(id: $"tester.item-{i}")).ToArray();
        var result = Parse(many);

        Assert.Equal(StoreCatalog.MaxEntries, result.Entries.Count);
        Assert.Equal(20, result.Skipped);
    }

    [Fact]
    public void Wrong_types_in_any_field_never_throw()
    {
        foreach (var field in new[] { "id", "kind", "name", "author", "version", "description", "priceCents", "pack", "sha256", "size", "buyUrl" })
        {
            foreach (var wrong in new JsonNode?[] { null, 7, true, "text", new JsonArray(1), new JsonObject() })
            {
                var item = Item();
                item[field] = wrong?.DeepClone();
                var result = Parse(item);
                Assert.True(result.Ok);
            }
        }
    }

    // ---- what each entry offers

    private static StoreEntry Entry(string? pack = "packs/x.heysippack", string version = "1.0.0") =>
        new("tester.blob", PackKind.Character, "Blob", "Tester", version, "", 0, pack, null, 0, null);

    [Fact]
    public void Built_in_items_are_included_whatever_else_is_true()
    {
        Assert.Equal(StoreStatus.Included, StoreStatusOf.For(Entry(pack: null), builtIn: true, installedVersion: null));
    }

    [Fact]
    public void An_item_that_is_not_installed_can_be_fetched_only_when_a_file_is_listed()
    {
        Assert.Equal(StoreStatus.Available, StoreStatusOf.For(Entry(), builtIn: false, installedVersion: null));
        Assert.Equal(StoreStatus.Unavailable, StoreStatusOf.For(Entry(pack: null), builtIn: false, installedVersion: null));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", StoreStatus.Installed)]
    [InlineData("1.2.0", "1.0.0", StoreStatus.Update)]
    [InlineData("1.10.0", "1.9.0", StoreStatus.Update)]
    [InlineData("1.0.0", "1.1.0", StoreStatus.Installed)]
    [InlineData("1.0.0", "1.0.0-beta", StoreStatus.Update)]
    public void An_installed_item_offers_an_update_only_when_the_list_has_a_newer_version(string listed, string installed, StoreStatus expected)
    {
        Assert.Equal(expected, StoreStatusOf.For(Entry(version: listed), builtIn: false, installedVersion: installed));
    }

    [Fact]
    public void An_update_is_never_offered_for_an_item_with_no_file()
    {
        Assert.Equal(StoreStatus.Installed, StoreStatusOf.For(Entry(pack: null, version: "9.0.0"), builtIn: false, installedVersion: "1.0.0"));
    }
}
