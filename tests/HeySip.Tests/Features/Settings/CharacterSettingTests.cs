using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class CharacterSettingTests : IDisposable
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
    public void A_new_install_uses_the_default_character()
    {
        Assert.Equal("heysip.drip", new AppSettings().Character);
        Assert.Equal("heysip.drip", PackId.DefaultCharacter);
    }

    [Theory]
    [InlineData("Drip", "heysip.drip")]
    [InlineData("Minty", "heysip.minty")]
    [InlineData("Sunny", "heysip.sunny")]
    [InlineData("Blossom", "heysip.blossom")]
    public void A_file_from_before_packs_keeps_the_character_it_had(string oldName, string expectedId)
    {
        var loaded = LoadFile($"{{ \"SchemaVersion\": 1, \"Name\": \"Kai\", \"Character\": \"{oldName}\" }}");

        Assert.Equal(expectedId, loaded.Character);
        Assert.Equal("Kai", loaded.Name);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void The_upgraded_file_is_rewritten_and_the_old_one_is_kept()
    {
        const string original = "{ \"SchemaVersion\": 1, \"Character\": \"Blossom\" }";

        LoadFile(original);

        var rewritten = File.ReadAllText(SettingsFile);
        Assert.Contains("\"Character\": \"heysip.blossom\"", rewritten);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", rewritten);
        Assert.Equal(original, File.ReadAllText(SettingsFile + ".v1.bak"));
    }

    [Fact]
    public void A_file_from_before_versions_existed_goes_through_every_step()
    {
        var loaded = LoadFile("{ \"Character\": \"Sunny\", \"Name\": \"Old\" }");

        Assert.Equal("heysip.sunny", loaded.Character);
        Assert.Equal("Old", loaded.Name);
    }

    [Theory]
    [InlineData("Bob")]
    [InlineData("")]
    [InlineData("heysip")]
    public void A_character_name_nobody_knows_becomes_the_default(string oldName)
    {
        var loaded = LoadFile($"{{ \"SchemaVersion\": 1, \"Character\": \"{oldName}\" }}");

        Assert.Equal("heysip.drip", loaded.Character);
    }

    [Fact]
    public void A_file_without_a_character_gets_the_default()
    {
        Assert.Equal("heysip.drip", LoadFile("{ \"SchemaVersion\": 1, \"Name\": \"Kai\" }").Character);
        Assert.Equal("heysip.drip", LoadFile("{ \"SchemaVersion\": 2, \"Name\": \"Kai\" }").Character);
    }

    [Fact]
    public void A_pack_id_is_kept_whether_or_not_the_pack_is_installed()
    {
        // Settings know nothing about which packs exist; falling back is the catalogue's job, so choosing is never lost.
        var loaded = LoadFile("{ \"SchemaVersion\": 2, \"Character\": \"alice.axolotl\" }");

        Assert.Equal("alice.axolotl", loaded.Character);
    }

    [Theory]
    [InlineData("Not An Id")]
    [InlineData("../../evil")]
    [InlineData("UPPER.case")]
    public void A_current_file_with_something_that_is_not_an_id_gets_the_default(string character)
    {
        var loaded = LoadFile($"{{ \"SchemaVersion\": 2, \"Character\": \"{character}\" }}");

        Assert.Equal("heysip.drip", loaded.Character);
    }

    [Fact]
    public void The_chosen_character_survives_a_restart()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s with { Character = "alice.axolotl" });

        var second = new SettingsService(_data.Location);
        second.Load();

        Assert.Equal("alice.axolotl", second.Current.Character);
        Assert.Contains("\"Character\": \"alice.axolotl\"", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Choosing_the_character_it_already_is_changes_nothing()
    {
        var settings = new SettingsService(_data.Location);
        settings.Load();
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.Update(s => s with { Character = "heysip.drip" });

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("heysip.drip", true)]
    [InlineData("alice.axolotl", true)]
    [InlineData("a-b.c-d.e", true)]
    [InlineData("nodot", false)]
    [InlineData("Upper.case", false)]
    [InlineData("has space.x", false)]
    [InlineData("a..b", false)]
    [InlineData(".a.b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_author_dot_name_is_a_valid_pack_id(string? id, bool valid)
    {
        Assert.Equal(valid, PackId.IsValid(id));
    }

    [Fact]
    public void A_pack_id_cannot_be_longer_than_sixty_four_characters()
    {
        Assert.True(PackId.IsValid("a." + new string('b', 62)));
        Assert.False(PackId.IsValid("a." + new string('b', 63)));
    }

    [Fact]
    public void The_characters_folder_sits_in_the_data_folder()
    {
        Assert.Equal(Path.Combine(_data.Location.RootDirectory, "characters"), _data.Location.CharactersDirectory);
    }
}
