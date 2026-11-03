using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PackVersionTests : IDisposable
{
    private static readonly PackLoadOptions App = new(new Version(1, 0));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-versions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Characters => Path.Combine(_root, "characters");

    private static PackLoadResult Pack(string version, string name = "Blob")
    {
        JsonArray Point(double x, double y) => new(x, y);
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = "character",
            ["id"] = "tester.blob",
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
        var result = PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) }, App);
        Assert.True(result.Ok, result.Error);
        return result;
    }

    // ---- ordering

    [Theory]
    [InlineData("1.9.0", "1.10.0", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.1", -1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta", 0)]
    [InlineData("1.0.1-beta", "1.0.0", 1)]
    public void Versions_are_ordered_the_way_semantic_versioning_orders_them(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(PackVersion.Compare(a, b)));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("10.20.30", true)]
    [InlineData("1.0.0-beta.1", true)]
    [InlineData("1.0", false)]
    [InlineData("1.0.0.0", false)]
    [InlineData("v1.0.0", false)]
    [InlineData("1.0.0.replaced-ab", false)]
    [InlineData("../1.0.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_three_part_versions_are_allowed_because_they_become_folder_names(string? version, bool valid)
    {
        Assert.Equal(valid, PackVersion.IsValid(version));
    }

    // ---- what that means for installing

    [Fact]
    public void A_release_is_an_update_over_its_beta_and_a_beta_is_older_than_its_release()
    {
        PackInstaller.Install(Pack("1.0.0-beta"), Characters);
        Assert.Equal(InstallKind.Update, PackInstaller.Plan(Pack("1.0.0").Pack!, Characters).Kind);

        PackInstaller.Install(Pack("1.0.0"), Characters);
        Assert.Equal(InstallKind.Older, PackInstaller.Plan(Pack("1.0.0-beta").Pack!, Characters).Kind);
    }

    [Fact]
    public void The_scan_prefers_the_release_to_its_beta_whichever_order_the_folders_come_in()
    {
        foreach (var version in new[] { "1.0.0-beta", "1.0.0", "1.0.0-alpha" })
        {
            var folder = Path.Combine(Characters, "tester.blob", version);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "pack.json"), Pack(version).ValidatedFiles["pack.json"]);
        }

        Assert.Equal("1.0.0", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Version);
    }

    [Fact]
    public void Installing_a_newer_version_tidies_the_older_ones_away()
    {
        PackInstaller.Install(Pack("1.0.0"), Characters);
        PackInstaller.Install(Pack("1.1.0"), Characters);
        PackInstaller.Install(Pack("1.2.0"), Characters);

        Assert.Equal([Path.Combine(Characters, "tester.blob", "1.2.0")], Directory.GetDirectories(Path.Combine(Characters, "tester.blob")));
        Assert.Equal("1.2.0", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Version);
    }

    [Fact]
    public void Reinstalling_leaves_exactly_one_copy_and_no_backup_behind()
    {
        PackInstaller.Install(Pack("1.0.0", name: "First"), Characters);

        PackInstaller.Install(Pack("1.0.0", name: "Second"), Characters);

        var folders = Directory.GetDirectories(Path.Combine(Characters, "tester.blob"));
        Assert.Equal([Path.Combine(Characters, "tester.blob", "1.0.0")], folders);
        Assert.Equal("Second", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Name);
    }

    [Fact]
    public void A_hidden_backup_or_leftover_beside_the_versions_is_never_mistaken_for_one()
    {
        PackInstaller.Install(Pack("1.0.0"), Characters);
        var leftover = Path.Combine(Characters, "tester.blob", ".1.0.0.replaced-abcd1234");
        Directory.CreateDirectory(leftover);
        File.WriteAllBytes(Path.Combine(leftover, "pack.json"), Pack("1.0.0", "Ghost").ValidatedFiles["pack.json"]);

        Assert.Equal("Blob", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Name);
    }

    [Fact]
    public void A_failed_replace_keeps_the_copy_that_was_working()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // this relies on Windows refusing to move a folder that has a file open inside it
        }

        PackInstaller.Install(Pack("1.0.0", name: "Working"), Characters);
        var manifest = Path.Combine(Characters, "tester.blob", "1.0.0", "pack.json");

        using (new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read)) // held open, so the folder can't be moved
        {
            var outcome = PackInstaller.Install(Pack("1.0.0", name: "Replacement"), Characters);

            Assert.False(outcome.Ok);
            Assert.Contains("couldn't be saved", outcome.Error);
        }

        Assert.Equal("Working", Assert.Single(PackStore.Scan(Characters, App)).Pack.Header.Name);
        Assert.DoesNotContain(Directory.GetDirectories(Characters), folder => Path.GetFileName(folder).StartsWith('.'));
    }
}
