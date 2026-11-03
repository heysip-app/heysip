using HeySip.Core.Features.Companion.Packs;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Companion;

public sealed class PackBundleTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    private string Directory => _data.Location.CharactersDirectory;

    private string FileOf(string characterId) => Path.Combine(Directory, characterId, PackBundle.FileName);

    private void WriteRaw(string characterId, string text)
    {
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, characterId));
        File.WriteAllText(FileOf(characterId), text);
    }

    [Fact]
    public void Saved_accessories_are_read_back_in_order()
    {
        PackBundle.Save(Directory, "tester.blob", ["tester.top-hat", "tester.scarf"]);

        Assert.Equal(["tester.top-hat", "tester.scarf"], PackBundle.Read(Directory, "tester.blob"));
    }

    [Fact]
    public void A_character_with_nothing_saved_comes_with_nothing()
    {
        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));
    }

    [Fact]
    public void Saving_an_empty_list_removes_what_was_saved()
    {
        PackBundle.Save(Directory, "tester.blob", ["tester.top-hat"]);

        PackBundle.Save(Directory, "tester.blob", []);

        Assert.False(File.Exists(FileOf("tester.blob")));
        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));
    }

    [Fact]
    public void Saving_again_replaces_the_list()
    {
        PackBundle.Save(Directory, "tester.blob", ["tester.top-hat"]);
        PackBundle.Save(Directory, "tester.blob", ["tester.scarf"]);

        Assert.Equal(["tester.scarf"], PackBundle.Read(Directory, "tester.blob"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "accessories": "tester.top-hat" }""")]
    [InlineData("""{ "accessories": ["Not An Id"] }""")]
    [InlineData("""{ "accessories": ["../../evil"] }""")]
    [InlineData("""{ "accessories": [42] }""")]
    [InlineData("""{ "accessories": ["tester.top-hat", "tester.top-hat"] }""")]
    public void A_file_that_is_not_what_was_written_counts_as_nothing(string text)
    {
        WriteRaw("tester.blob", text);

        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));
    }

    [Fact]
    public void A_list_longer_than_the_limit_counts_as_nothing()
    {
        var tooMany = Enumerable.Range(0, PackBundle.MaxAccessories + 1).Select(n => $"tester.hat-{n}").ToList();
        WriteRaw("tester.blob", System.Text.Json.JsonSerializer.Serialize(new { accessories = tooMany }));

        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));

        var fits = tooMany.Take(PackBundle.MaxAccessories).ToList();
        WriteRaw("tester.blob", System.Text.Json.JsonSerializer.Serialize(new { accessories = fits }));

        Assert.Equal(fits, PackBundle.Read(Directory, "tester.blob"));
    }

    [Fact]
    public void A_file_that_is_far_too_big_counts_as_nothing()
    {
        WriteRaw("tester.blob", """{ "accessories": ["tester.top-hat"], "padding": """ + "\"" + new string('x', 20_000) + "\" }");

        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("Not An Id")]
    [InlineData("")]
    public void A_character_id_that_is_not_an_id_is_never_used_as_a_path(string characterId)
    {
        PackBundle.Save(Directory, characterId, ["tester.top-hat"]);

        Assert.Empty(PackBundle.Read(Directory, characterId));
        Assert.False(File.Exists(Path.Combine(Directory, "..", "evil", PackBundle.FileName)));
    }

    private static AccessoryPack Accessory(string id, AccessorySlot slot) =>
        new(new PackHeader(1, PackKind.Accessory, id, id, "1.0.0", "", "", "", null, PackTier.Free, null), slot, InFront: false, []);

    [Fact]
    public void A_new_character_wears_the_first_accessory_of_each_slot_in_slot_order()
    {
        var look = PackBundle.DefaultLook(
        [
            Accessory("tester.scarf", AccessorySlot.Neck),
            Accessory("tester.hat-b", AccessorySlot.Hat),
            Accessory("tester.hat-a", AccessorySlot.Hat),
            Accessory("tester.balloon", AccessorySlot.Held),
        ]);

        Assert.Equal(["tester.hat-b", "tester.scarf", "tester.balloon"], look.Select(accessory => accessory.Header.Id));
    }

    [Fact]
    public void A_character_that_comes_with_nothing_wears_nothing()
    {
        Assert.Empty(PackBundle.DefaultLook([]));
    }

    [Fact]
    public void Uninstalling_the_character_takes_the_list_with_it()
    {
        PackBundle.Save(Directory, "tester.blob", ["tester.top-hat"]);

        Assert.True(PackInstaller.Uninstall("tester.blob", Directory));

        Assert.Empty(PackBundle.Read(Directory, "tester.blob"));
    }
}
