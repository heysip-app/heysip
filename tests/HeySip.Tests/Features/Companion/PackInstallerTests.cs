using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PackInstallerTests : IDisposable
{
    private static readonly PackLoadOptions App = new(new Version(1, 0));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-installer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Characters => Path.Combine(_root, "characters");

    private static JsonObject Manifest(string id = "tester.blob", string version = "1.0.0", string name = "Blob", string? sound = null)
    {
        JsonArray Point(double x, double y) => new(x, y);
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = id,
            ["name"] = name,
            ["version"] = version,
            ["palette"] = new JsonObject { ["light"] = "#FFFFFF", ["deep"] = "#888888", ["outline"] = "#444444" },
            ["anchors"] = new JsonObject
            {
                ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
                ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
                ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
                ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
            },
            ["body"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(50, 80, 36, 40), ["fill"] = "$light" }),
        };
        if (sound is not null)
        {
            manifest["sounds"] = new JsonObject { ["reminder"] = sound };
        }

        return manifest;
    }

    private static byte[] Wav()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + 800);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(8000);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(800);
        writer.Write(new byte[800]);
        return stream.ToArray();
    }

    private static PackLoadResult Load(JsonObject manifest, Dictionary<string, byte[]>? extra = null, PackLoadOptions? options = null)
    {
        var files = new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) };
        foreach (var (name, data) in extra ?? [])
        {
            files[name] = data;
        }

        var result = PackLoader.FromFiles(files, options ?? App);
        Assert.True(result.Ok, result.Error);
        return result;
    }

    // ---- installing

    [Fact]
    public void A_pack_is_installed_under_its_id_and_version_and_found_afterwards()
    {
        var outcome = PackInstaller.Install(Load(Manifest()), Characters);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(Path.Combine(Characters, "tester.blob", "1.0.0"), outcome.Folder);
        Assert.True(File.Exists(Path.Combine(outcome.Folder!, "pack.json")));
        var found = Assert.Single(PackStore.Scan(Characters, App));
        Assert.Equal("tester.blob", found.Pack.Header.Id);
    }

    [Fact]
    public void The_installed_manifest_is_exactly_the_one_that_was_checked()
    {
        var loaded = Load(Manifest());

        var outcome = PackInstaller.Install(loaded, Characters);

        Assert.Equal(loaded.ValidatedFiles["pack.json"], File.ReadAllBytes(Path.Combine(outcome.Folder!, "pack.json")));
    }

    [Fact]
    public void Sounds_are_installed_with_the_pack_and_still_pass_every_check_when_it_is_scanned()
    {
        var loaded = Load(Manifest(sound: "sounds/chime.wav"), new() { ["sounds/chime.wav"] = Wav() });

        var outcome = PackInstaller.Install(loaded, Characters);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.True(File.Exists(Path.Combine(outcome.Folder!, "sounds", "chime.wav")));
        Assert.Single(PackStore.Scan(Characters, App));
    }

    [Fact]
    public void Installing_the_same_version_again_replaces_it_and_leaves_nothing_behind()
    {
        PackInstaller.Install(Load(Manifest(name: "First")), Characters);

        var outcome = PackInstaller.Install(Load(Manifest(name: "Second")), Characters);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("Second", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Name);
        Assert.Equal([Path.Combine(Characters, "tester.blob")], Directory.GetDirectories(Characters));
        Assert.Equal([Path.Combine(Characters, "tester.blob", "1.0.0")], Directory.GetDirectories(Path.Combine(Characters, "tester.blob")));
    }

    [Fact]
    public void A_new_version_replaces_the_old_one_and_becomes_the_one_in_use()
    {
        PackInstaller.Install(Load(Manifest(version: "1.0.0", name: "Old")), Characters);

        PackInstaller.Install(Load(Manifest(version: "1.1.0", name: "New")), Characters);

        Assert.Equal([Path.Combine(Characters, "tester.blob", "1.1.0")], Directory.GetDirectories(Path.Combine(Characters, "tester.blob")));
        Assert.Equal("New", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Name);
    }

    [Fact]
    public void No_half_finished_install_is_left_when_saving_fails()
    {
        Directory.CreateDirectory(Characters);
        File.WriteAllText(Path.Combine(Characters, "tester.blob"), "a file where the pack's folder should go");

        var outcome = PackInstaller.Install(Load(Manifest()), Characters);

        Assert.False(outcome.Ok);
        Assert.Contains("couldn't be saved", outcome.Error);
        Assert.DoesNotContain(Directory.GetDirectories(Characters), folder => Path.GetFileName(folder).StartsWith('.'));
    }

    [Fact]
    public void A_pack_that_failed_to_load_cannot_be_installed()
    {
        var outcome = PackInstaller.Install(PackLoadResult.Failure("This isn't a valid pack file"), Characters);

        Assert.False(outcome.Ok);
        Assert.Equal("This isn't a valid pack file", outcome.Error);
        Assert.False(Directory.Exists(Characters));
    }

    [Fact]
    public void A_pack_cannot_take_a_built_in_id_even_if_it_was_loaded_as_built_in()
    {
        var loaded = Load(Manifest(id: "heysip.impostor"), options: App with { BuiltIn = true });

        var outcome = PackInstaller.Install(loaded, Characters);

        Assert.False(outcome.Ok);
        Assert.False(Directory.Exists(Characters));
    }

    [Fact]
    public void A_file_name_that_climbs_out_of_the_folder_is_refused_and_nothing_is_written()
    {
        var loaded = Load(Manifest());
        var tampered = loaded with
        {
            Files = new Dictionary<string, byte[]>
            {
                ["pack.json"] = loaded.ValidatedFiles["pack.json"],
                ["../../escaped.txt"] = [1, 2, 3],
            },
        };

        var outcome = PackInstaller.Install(tampered, Characters);

        Assert.False(outcome.Ok);
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.txt")));
        Assert.Empty(Directory.Exists(Characters) ? Directory.GetFileSystemEntries(Characters, "*", SearchOption.AllDirectories).Where(entry => entry.EndsWith("escaped.txt")) : []);
    }

    [Fact]
    public void A_result_without_a_manifest_is_refused()
    {
        var loaded = Load(Manifest());

        var outcome = PackInstaller.Install(loaded with { Files = new Dictionary<string, byte[]>() }, Characters);

        Assert.False(outcome.Ok);
    }

    [Fact]
    public void An_install_in_progress_is_not_mistaken_for_a_pack()
    {
        var staging = Path.Combine(Characters, ".incoming-abc", "1.0.0");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "pack.json"), Encoding.UTF8.GetBytes(Manifest().ToJsonString()));

        Assert.Empty(PackStore.Scan(Characters, App));
    }

    // ---- what installing would do

    [Fact]
    public void The_plan_says_new_update_reinstall_or_older()
    {
        var one = Load(Manifest(version: "1.0.0")).Pack!;
        var two = Load(Manifest(version: "1.2.0")).Pack!;

        Assert.Equal(new InstallPlan(InstallKind.New, null), PackInstaller.Plan(one, Characters));

        PackInstaller.Install(Load(Manifest(version: "1.0.0")), Characters);
        Assert.Equal(new InstallPlan(InstallKind.Reinstall, "1.0.0"), PackInstaller.Plan(one, Characters));
        Assert.Equal(new InstallPlan(InstallKind.Update, "1.0.0"), PackInstaller.Plan(two, Characters));

        PackInstaller.Install(Load(Manifest(version: "1.2.0")), Characters);
        Assert.Equal(new InstallPlan(InstallKind.Older, "1.2.0"), PackInstaller.Plan(one, Characters));
    }

    [Fact]
    public void Versions_are_compared_as_numbers_not_as_text()
    {
        PackInstaller.Install(Load(Manifest(version: "1.9.0")), Characters);

        var plan = PackInstaller.Plan(Load(Manifest(version: "1.10.0")).Pack!, Characters);

        Assert.Equal(InstallKind.Update, plan.Kind);
    }

    // ---- removing

    [Fact]
    public void Removing_a_pack_deletes_every_version_of_it()
    {
        PackInstaller.Install(Load(Manifest(version: "1.0.0")), Characters);
        PackInstaller.Install(Load(Manifest(version: "1.1.0")), Characters);

        Assert.True(PackInstaller.Uninstall("tester.blob", Characters));

        Assert.Empty(PackStore.Scan(Characters, App));
        Assert.False(Directory.Exists(Path.Combine(Characters, "tester.blob")));
    }

    [Fact]
    public void Removing_leaves_other_packs_alone()
    {
        PackInstaller.Install(Load(Manifest(id: "tester.one")), Characters);
        PackInstaller.Install(Load(Manifest(id: "tester.two")), Characters);

        PackInstaller.Uninstall("tester.one", Characters);

        Assert.Equal(["tester.two"], PackStore.Scan(Characters, App).Select(item => item.Pack.Header.Id));
    }

    [Theory]
    [InlineData("heysip.drip")]
    [InlineData("../../somewhere")]
    [InlineData("not an id")]
    [InlineData("tester.missing")]
    [InlineData("")]
    public void Built_in_missing_and_malformed_ids_cannot_be_removed(string id)
    {
        PackInstaller.Install(Load(Manifest()), Characters);

        Assert.False(PackInstaller.Uninstall(id, Characters));
        Assert.Single(PackStore.Scan(Characters, App));
    }
}
