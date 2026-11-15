using System.Text;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.SharedKernel;
using Xunit;

namespace HeySip.Tests.Features.Companion;

/// <summary>
/// The pack and store limits that are written once as a number and once inside a pattern must stay equal, and the small
/// shared helpers behind them must behave.
/// </summary>
public sealed class PackFormatConstantsTests
{
    private static readonly PackLoadOptions App = new(new Version(1, 0, 0));

    private static string AccessoryWithPaletteColor(string colorName) => $$"""
        {
          "formatVersion": 1,
          "kind": "accessory",
          "id": "test.hat",
          "name": "Hat",
          "version": "1.0.0",
          "slot": "hat",
          "palette": { "{{colorName}}": "#112233" },
          "parts": [ { "ellipse": [0, 0, 5, 5], "fill": "${{colorName}}" } ]
        }
        """;

    [Fact]
    public void A_palette_colour_name_of_the_longest_allowed_length_is_accepted()
    {
        var name = "a" + new string('b', PackLimits.MaxPaletteNameLength - 1);
        var json = Encoding.UTF8.GetBytes(AccessoryWithPaletteColor(name));

        var pack = PackParser.Parse(json, App);

        Assert.Equal("test.hat", pack.Header.Id);
    }

    [Fact]
    public void A_palette_colour_name_one_longer_is_refused()
    {
        var name = "a" + new string('b', PackLimits.MaxPaletteNameLength);
        var json = Encoding.UTF8.GetBytes(AccessoryWithPaletteColor(name));

        Assert.Throws<InvalidPackException>(() => PackParser.Parse(json, App));
    }

    [Fact]
    public void A_byte_order_mark_in_front_of_a_pack_is_ignored()
    {
        var json = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(AccessoryWithPaletteColor("sky"))).ToArray();

        Assert.Equal("test.hat", PackParser.Parse(json, App).Header.Id);
    }

    [Fact]
    public void The_required_anchors_are_the_named_constants_and_have_no_duplicates()
    {
        Assert.Same(PackAnchors.Required, CharacterPack.RequiredAnchors);
        Assert.Equal(PackAnchors.Required.Count, PackAnchors.Required.Distinct().Count());
        Assert.All(new[] { PackAnchors.HandHold, PackAnchors.ShoulderLeft, PackAnchors.ShoulderRight, PackAnchors.Mouth }, anchor => Assert.Contains(anchor, PackAnchors.Required));
    }

    [Fact]
    public void The_default_motion_is_inside_the_ranges_a_pack_is_held_to()
    {
        var motion = PackMotion.Default;

        Assert.True(PackLimits.Bob.Contains(motion.Bob));
        Assert.True(PackLimits.BobSpeed.Contains(motion.BobSpeed));
        Assert.True(PackLimits.Squash.Contains(motion.Squash));
        Assert.True(PackLimits.BlinkEveryMs.Contains(motion.BlinkEveryMs));
        Assert.True(PackLimits.BlinkMs.Contains(motion.BlinkMs));
        Assert.True(PackLimits.SwayAmplitude.Contains(PackLimits.DefaultSwayAmplitude));
        Assert.True(PackLimits.SwaySpeed.Contains(PackLimits.DefaultSwaySpeed));
        Assert.True(PackLimits.Stroke.Contains(PackLimits.DefaultArmWidth));
        Assert.True(PackLimits.Stroke.Contains(PackLimits.DefaultArmOutlineWidth));
    }

    [Fact]
    public void A_range_clamps_and_checks_its_own_ends()
    {
        var range = new PackRange(2, 5);

        Assert.Equal(2, range.Clamp(-10));
        Assert.Equal(5, range.Clamp(99));
        Assert.Equal(3.5, range.Clamp(3.5));
        Assert.True(range.Contains(2));
        Assert.True(range.Contains(5));
        Assert.False(range.Contains(5.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackRange(5, 2));
    }

    [Fact]
    public void A_sha256_in_a_store_list_must_have_exactly_the_hash_length()
    {
        string List(string sha) => $$"""
            { "formatVersion": 1, "items": [ { "id": "test.hat", "kind": "accessory", "name": "Hat", "version": "1.0.0", "sha256": "{{sha}}" } ] }
            """;

        var right = StoreCatalog.Parse(Encoding.UTF8.GetBytes(List(new string('a', StoreCatalog.Sha256HexLength))));
        var tooLong = StoreCatalog.Parse(Encoding.UTF8.GetBytes(List(new string('a', StoreCatalog.Sha256HexLength + 1))));
        var tooShort = StoreCatalog.Parse(Encoding.UTF8.GetBytes(List(new string('a', StoreCatalog.Sha256HexLength - 1))));

        Assert.Single(right.Entries);
        Assert.Empty(tooLong.Entries);
        Assert.Empty(tooShort.Entries);
    }

    [Fact]
    public void A_store_list_with_a_byte_order_mark_still_reads()
    {
        var json = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("""{ "formatVersion": 1, "items": [] }"""))
            .ToArray();

        Assert.True(StoreCatalog.Parse(json).Ok);
    }

    [Theory]
    [InlineData("""{ "formatVersion": 1 }""", true)]
    [InlineData("""{ "formatVersion": 2 }""", false)]
    [InlineData("""{ "formatVersion": "1" }""", false)]
    [InlineData("""{ "formatVersion": 1.5 }""", false)]
    [InlineData("""{ }""", false)]
    [InlineData("""[ 1 ]""", false)]
    public void The_format_version_check_wants_a_whole_number_that_matches(string json, bool expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal(expected, JsonInput.HasFormatVersion(document.RootElement, JsonInput.SupportedFormatVersion));
    }

    [Fact]
    public void The_byte_order_mark_is_removed_only_when_it_is_there()
    {
        byte[] withMark = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];
        byte[] without = [(byte)'{', (byte)'}'];

        Assert.Equal(without, JsonInput.WithoutByteOrderMark(withMark).ToArray());
        Assert.Equal(without, JsonInput.WithoutByteOrderMark(without).ToArray());
        Assert.Empty(JsonInput.WithoutByteOrderMark(ReadOnlyMemory<byte>.Empty).ToArray());
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(5L * 1024 * 1024 + (512 * 1024), "5.5 MB")]
    public void Sizes_are_written_in_the_unit_that_reads_best(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Friendly(bytes).Replace(',', '.'));
    }

    [Fact]
    public void Quoted_values_are_cut_to_the_limit_and_marked()
    {
        Assert.Equal("short", PackText.Shorten("short", 10));
        Assert.Equal("0123456789", PackText.Shorten("0123456789", 10));
        Assert.Equal("0123456789…", PackText.Shorten("0123456789ABC", 10));
    }
}
