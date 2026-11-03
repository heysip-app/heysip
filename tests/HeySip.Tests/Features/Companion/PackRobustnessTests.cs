using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PackRobustnessTests : IDisposable
{
    private static readonly PackLoadOptions App = new(new Version(1, 0));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heysip-robust-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JsonArray Point(double x, double y) => new(x, y);

    /// <summary>A pack that uses a bit of every part of the format, so there is something of each kind to damage.</summary>
    private static JsonObject FullCharacter() => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "character",
        ["id"] = "tester.full",
        ["name"] = "Full",
        ["version"] = "1.0.0",
        ["author"] = "Tester",
        ["license"] = "MIT",
        ["description"] = "Uses everything",
        ["minAppVersion"] = "0.1.0",
        ["requires"] = new JsonArray(),
        ["distribution"] = new JsonObject { ["tier"] = "free", ["storeId"] = "abc" },
        ["palette"] = new JsonObject { ["light"] = "#FFFFFF", ["deep"] = "#888888", ["outline"] = "#444444", ["extra"] = "#123456AA" },
        ["motion"] = new JsonObject { ["bob"] = 1.5, ["bobSpeed"] = 2.2, ["squash"] = 0.01, ["blinkEveryMs"] = 4000, ["blinkMs"] = 130 },
        ["anchors"] = new JsonObject
        {
            ["eyeLeft"] = Point(39, 86), ["eyeRight"] = Point(61, 86), ["mouth"] = Point(50, 98),
            ["cheekLeft"] = Point(29, 98), ["cheekRight"] = Point(71, 98),
            ["shoulderLeft"] = Point(16, 92), ["shoulderRight"] = Point(84, 92),
            ["headTop"] = Point(50, 6), ["eyeLine"] = Point(50, 86), ["neck"] = Point(50, 64), ["handHold"] = Point(72, 108),
        },
        ["body"] = new JsonArray(
            new JsonObject
            {
                ["path"] = "M50,6 C50,6 14,54 14,88 A36,36 0 0 0 86,88 C86,54 50,6 50,6 Z",
                ["fill"] = new JsonObject { ["gradient"] = new JsonArray("$light", "$deep") },
                ["stroke"] = new JsonObject { ["color"] = "$outline", ["width"] = 2.5, ["cap"] = "round", ["join"] = "round" },
                ["opacity"] = 0.9,
            },
            new JsonObject { ["ellipse"] = new JsonArray(26, 86, 2, 2), ["fill"] = "$extra" },
            new JsonObject { ["line"] = new JsonArray(10, 10, 20, 20), ["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 1 } }),
        ["feet"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(38, 125, 9, 5), ["fill"] = "$deep" }),
        ["arms"] = new JsonObject { ["fill"] = "$deep", ["outline"] = "$outline", ["width"] = 5, ["outlineWidth"] = 8 },
        ["poses"] = new JsonObject
        {
            ["happy"] = new JsonObject
            {
                ["eyes"] = "happy",
                ["mouth"] = "grin",
                ["brows"] = "none",
                ["arms"] = new JsonObject { ["left"] = Point(5, 64), ["right"] = Point(95, 64) },
                ["sway"] = new JsonObject { ["arm"] = "right", ["amplitude"] = 7, ["speed"] = 7 },
                ["effects"] = new JsonArray("sparkle", "zzz"),
            },
        },
        ["face"] = new JsonObject
        {
            ["eyesMirrored"] = true,
            ["eyes"] = new JsonObject { ["open"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(0, 0, 4, 5), ["fill"] = "#000000" }) },
            ["mouths"] = new JsonObject { ["grin"] = new JsonArray(new JsonObject { ["path"] = "M-9,0 Q0,16 9,0 Z", ["fill"] = "#000000" }) },
            ["brows"] = new JsonObject { ["worried"] = new JsonArray(new JsonObject { ["line"] = new JsonArray(-6, -7, 6, -12), ["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 2 } }) },
        },
        ["sounds"] = new JsonObject { ["reminder"] = "sounds/ding.wav", ["drank"] = "sounds/glug.wav" },
        ["messages"] = "messages.json",
        ["editor"] = new JsonObject { ["recipe"] = "anything" },
    };

    private static JsonObject FullAccessory() => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "accessory",
        ["id"] = "tester.hat",
        ["name"] = "Hat",
        ["version"] = "1.0.0",
        ["slot"] = "hat",
        ["layer"] = "front",
        ["palette"] = new JsonObject { ["red"] = "#FF0000" },
        ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-12,0 L0,-26 L12,0 Z", ["fill"] = "$red", ["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 1 } }),
    };

    private static byte[] Wav()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + 400);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(8000);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(400);
        writer.Write(new byte[400]);
        return stream.ToArray();
    }

    private static Dictionary<string, byte[]> Files(JsonObject manifest)
    {
        var files = new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) };

        // Only a character can have sounds; an accessory that came with some would be refused for carrying a file it doesn't use.
        if (manifest["sounds"] is not null)
        {
            files["sounds/ding.wav"] = Wav();
            files["sounds/glug.wav"] = Wav();
        }

        return files;
    }

    private static IEnumerable<JsonNode?> WrongValues()
    {
        yield return "text";
        yield return "1";
        yield return 12345;
        yield return -1;
        yield return 3.5;
        yield return double.MaxValue;
        yield return true;
        yield return null;
        yield return new JsonObject();
        yield return new JsonArray();
        yield return new JsonArray(1, 2, 3);
        yield return new JsonArray("a", "b");
        yield return new JsonObject { ["a"] = new JsonObject { ["b"] = new JsonArray() } };
    }

    /// <summary>Every place in the document that holds a value: the parent that holds it, and its name or position.</summary>
    private static List<(JsonNode Parent, string? Key, int Index)> Slots(JsonNode node)
    {
        var slots = new List<(JsonNode, string?, int)>();
        void Walk(JsonNode current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(pair => pair.Key).ToList())
                    {
                        slots.Add((obj, key, -1));
                        if (obj[key] is { } child)
                        {
                            Walk(child);
                        }
                    }

                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        slots.Add((array, null, i));
                        if (array[i] is { } child)
                        {
                            Walk(child);
                        }
                    }

                    break;
            }
        }

        Walk(node);
        return slots;
    }

    /// <summary>
    /// "A bad pack must never crash the app." Take a complete, valid pack, put every wrong kind of value into every single place
    /// in turn (text where a number goes, an object where a list goes, null, a huge number...) and require that loading it
    /// always ends in a clear yes or no and never in an exception.
    /// </summary>
    [Theory]
    [InlineData("character")]
    [InlineData("accessory")]
    public void No_value_of_the_wrong_type_anywhere_in_a_pack_can_make_loading_throw(string kind)
    {
        var template = kind == "character" ? FullCharacter() : FullAccessory();
        Assert.True(PackLoader.FromFiles(Files(template), App).Ok, "the starting pack must be valid");

        var crashes = new List<string>();
        var tried = 0;
        foreach (var (parent, key, index) in Slots(template))
        {
            foreach (var wrong in WrongValues())
            {
                var copy = template.DeepClone().AsObject();

                // Find the same place in the copy by walking the same route.
                var copySlot = Slots(copy)[Slots(template).FindIndex(slot => ReferenceEquals(slot.Parent, parent) && slot.Key == key && slot.Index == index)];
                if (copySlot.Key is not null)
                {
                    copySlot.Parent.AsObject()[copySlot.Key] = wrong?.DeepClone();
                }
                else
                {
                    copySlot.Parent.AsArray()[copySlot.Index] = wrong?.DeepClone();
                }

                tried++;
                var failure = Record.Exception(() => PackLoader.FromFiles(Files(copy), App));
                if (failure is not null)
                {
                    crashes.Add($"{failure.GetType().Name} ({failure.Message}) with {wrong?.ToJsonString() ?? "null"} at {key ?? "[" + index + "]"}");
                }
            }
        }

        Assert.True(tried > 200, $"only {tried} variations were tried; the test is not covering the pack");
        Assert.True(crashes.Count == 0, $"{crashes.Count} of {tried} variations threw:\n" + string.Join('\n', crashes.Take(15)));
    }

    [Fact]
    public void A_format_version_that_is_not_a_number_is_a_refusal_not_a_crash()
    {
        foreach (var wrong in new JsonNode?[] { "1", true, null, new JsonObject(), new JsonArray(1), 1.5 })
        {
            var manifest = FullCharacter();
            manifest["formatVersion"] = wrong;

            var result = PackLoader.FromFiles(Files(manifest), App);

            Assert.False(result.Ok, wrong?.ToJsonString());
            Assert.NotNull(result.Error);
        }
    }

    [Fact]
    public void The_same_holds_when_a_pack_comes_from_a_zip_or_a_folder()
    {
        var manifest = FullCharacter();
        manifest["formatVersion"] = "one";
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());

        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "pack.json"), bytes);
        var fromFolder = PackLoader.FromFolder(_root, App);

        using var zipStream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry("pack.json").Open())
        {
            entry.Write(bytes);
        }

        zipStream.Position = 0;
        var fromZip = PackLoader.FromZip(zipStream, App);

        Assert.False(fromFolder.Ok);
        Assert.False(fromZip.Ok);
    }

    // ---- sounds that lie about how long they are

    private static byte[] OggPage(long granule, byte[] packet, byte headerType)
    {
        var page = new byte[27 + 1 + packet.Length];
        Encoding.ASCII.GetBytes("OggS").CopyTo(page, 0);
        page[5] = headerType;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        page[26] = 1;
        page[27] = (byte)packet.Length;
        packet.CopyTo(page, 28);
        return page;
    }

    private static byte[] Ogg(double seconds, uint sampleRate = 48000)
    {
        var ident = new byte[30];
        ident[0] = 1;
        Encoding.ASCII.GetBytes("vorbis").CopyTo(ident, 1);
        ident[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(ident.AsSpan(12), sampleRate);
        return [.. OggPage(0, ident, 2), .. OggPage((long)(seconds * sampleRate), new byte[10], 4)];
    }

    [Fact]
    public void A_short_fake_page_added_to_the_end_cannot_make_a_long_ogg_pass_for_a_short_one()
    {
        var honest = Ogg(8);
        var disguised = honest.Concat(OggPage(granule: 100, new byte[10], headerType: 4)).ToArray();

        Assert.Contains("longer than 5 seconds", SoundCheck.Validate("a.ogg", honest)!);
        Assert.Contains("longer than 5 seconds", SoundCheck.Validate("a.ogg", disguised)!);
    }

    [Fact]
    public void A_stray_marker_with_nonsense_after_it_does_not_hide_the_real_length_either()
    {
        var withNoise = Ogg(8).Concat(Encoding.ASCII.GetBytes("OggS")).Concat(new byte[40]).ToArray();

        Assert.NotNull(SoundCheck.Validate("a.ogg", withNoise));
    }

    [Fact]
    public void A_short_honest_ogg_still_passes()
    {
        Assert.Null(SoundCheck.Validate("a.ogg", Ogg(2)));
    }
}
