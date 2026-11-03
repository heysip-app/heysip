using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PackStoreTests : IDisposable
{
    private static readonly PackLoadOptions App = new(new Version(1, 0));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JsonObject Manifest(string id, string version, string name = "Test")
    {
        JsonArray Point(double x, double y) => new(x, y);
        return new JsonObject
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
    }

    private string Install(string folderId, string folderVersion, JsonObject manifest)
    {
        var folder = Path.Combine(_root, folderId, folderVersion);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "pack.json"), Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return folder;
    }

    [Fact]
    public void An_installed_pack_is_found()
    {
        Install("tester.blob", "1.0.0", Manifest("tester.blob", "1.0.0", "Blob"));

        var found = PackStore.Scan(_root, App);

        var pack = Assert.Single(found).Pack;
        Assert.Equal("tester.blob", pack.Header.Id);
        Assert.Equal("Blob", pack.Header.Name);
    }

    [Fact]
    public void A_missing_or_empty_folder_has_no_packs()
    {
        Assert.Empty(PackStore.Scan(Path.Combine(_root, "nope"), App));

        Directory.CreateDirectory(_root);
        Assert.Empty(PackStore.Scan(_root, App));
    }

    [Fact]
    public void Several_packs_are_all_found()
    {
        Install("tester.one", "1.0.0", Manifest("tester.one", "1.0.0"));
        Install("tester.two", "1.0.0", Manifest("tester.two", "1.0.0"));

        Assert.Equal(["tester.one", "tester.two"], PackStore.Scan(_root, App).Select(item => item.Pack.Header.Id).Order());
    }

    [Fact]
    public void Only_the_newest_version_of_a_pack_is_used()
    {
        Install("tester.blob", "1.9.0", Manifest("tester.blob", "1.9.0", "Old"));
        Install("tester.blob", "1.10.0", Manifest("tester.blob", "1.10.0", "New"));

        var found = Assert.Single(PackStore.Scan(_root, App));

        Assert.Equal("1.10.0", found.Pack.Header.Version); // ten is more than nine, even though "1.10.0" sorts before "1.9.0" as text
    }

    [Fact]
    public void A_broken_newer_version_falls_back_to_the_older_one()
    {
        Install("tester.blob", "1.0.0", Manifest("tester.blob", "1.0.0", "Good"));
        var broken = Install("tester.blob", "2.0.0", Manifest("tester.blob", "2.0.0"));
        File.WriteAllText(Path.Combine(broken, "pack.json"), "{ not json");

        var found = Assert.Single(PackStore.Scan(_root, App));

        Assert.Equal("1.0.0", found.Pack.Header.Version);
    }

    [Fact]
    public void A_pack_whose_folder_does_not_match_its_id_or_version_is_skipped()
    {
        Install("tester.blob", "1.0.0", Manifest("tester.other", "1.0.0"));
        Install("tester.two", "1.0.0", Manifest("tester.two", "9.9.9"));

        Assert.Empty(PackStore.Scan(_root, App));
    }

    [Fact]
    public void A_pack_that_fails_its_checks_is_skipped_without_stopping_the_rest()
    {
        Install("tester.good", "1.0.0", Manifest("tester.good", "1.0.0"));
        var bad = Manifest("tester.bad", "1.0.0");
        ((JsonObject)bad["anchors"]!).Remove("mouth");
        Install("tester.bad", "1.0.0", bad);
        Install("heysip.fake", "1.0.0", Manifest("heysip.fake", "1.0.0")); // the built-in prefix is reserved

        var found = PackStore.Scan(_root, App);

        Assert.Equal(["tester.good"], found.Select(item => item.Pack.Header.Id));
    }

    [Fact]
    public void Stray_files_and_folders_that_are_not_packs_are_ignored()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_root, "empty.folder"));
        Install("tester.good", "1.0.0", Manifest("tester.good", "1.0.0"));

        Assert.Equal(["tester.good"], PackStore.Scan(_root, App).Select(item => item.Pack.Header.Id));
    }

    [Fact]
    public void Problems_are_logged_so_a_missing_character_can_be_explained()
    {
        var bad = Install("tester.bad", "1.0.0", Manifest("tester.bad", "1.0.0"));
        File.WriteAllText(Path.Combine(bad, "pack.json"), "{ not json");
        var log = new HeySip.Core.SharedKernel.Diagnostics.DiagnosticLog(TimeProvider.System, Path.Combine(_root, "log"));

        PackStore.Scan(_root, App, log);

        Assert.Contains("tester.bad", string.Join('\n', log.Tail()));
    }
}
