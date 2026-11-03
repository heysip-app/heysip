using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Tests.Features.Companion;

public sealed class PackLoaderTests : IDisposable
{
    private static readonly PackLoadOptions App = new(new Version(1, 0, 0));
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "heysip-pack-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // ---- building blocks

    private static JsonObject Anchors() => new()
    {
        ["eyeLeft"] = new JsonArray(39, 86),
        ["eyeRight"] = new JsonArray(61, 86),
        ["mouth"] = new JsonArray(50, 98),
        ["cheekLeft"] = new JsonArray(29, 98),
        ["cheekRight"] = new JsonArray(71, 98),
        ["shoulderLeft"] = new JsonArray(16, 92),
        ["shoulderRight"] = new JsonArray(84, 92),
        ["headTop"] = new JsonArray(50, 6),
        ["eyeLine"] = new JsonArray(50, 86),
        ["neck"] = new JsonArray(50, 64),
        ["handHold"] = new JsonArray(72, 108),
    };

    private static JsonObject Character() => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "character",
        ["id"] = "tester.drip",
        ["name"] = "Test Drip",
        ["version"] = "1.0.0",
        ["author"] = "Tester",
        ["palette"] = new JsonObject { ["light"] = "#A6EDE3", ["deep"] = "#2BB5A6", ["outline"] = "#127A70" },
        ["anchors"] = Anchors(),
        ["body"] = new JsonArray(new JsonObject
        {
            ["path"] = "M50,6 C50,6 14,54 14,88 A36,36 0 0 0 86,88 C86,54 50,6 50,6 Z",
            ["fill"] = new JsonObject { ["gradient"] = new JsonArray("$light", "$deep") },
            ["stroke"] = new JsonObject { ["color"] = "$outline", ["width"] = 2.5 },
        }),
        ["feet"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(38, 125, 9, 5), ["fill"] = "$deep" }),
    };

    private static JsonObject Accessory() => new()
    {
        ["formatVersion"] = 1,
        ["kind"] = "accessory",
        ["id"] = "tester.party-hat",
        ["name"] = "Party hat",
        ["version"] = "1.0.0",
        ["slot"] = "hat",
        ["parts"] = new JsonArray(new JsonObject { ["path"] = "M-12,0 L0,-26 L12,0 Z", ["fill"] = "#F472B6" }),
    };

    private static PackLoadResult Load(JsonObject manifest, PackLoadOptions? options = null) =>
        PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()) }, options ?? App);

    private static PackLoadResult LoadText(string text) =>
        PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = Encoding.UTF8.GetBytes(text) }, App);

    private static byte[] Zip(IEnumerable<(string Name, byte[] Data)> entries, Action<ZipArchiveEntry>? tweak = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                tweak?.Invoke(entry);
                using var content = entry.Open();
                content.Write(data);
            }
        }

        return stream.ToArray();
    }

    private static PackLoadResult LoadZip(byte[] zip, PackLoadOptions? options = null)
    {
        using var stream = new MemoryStream(zip);
        return PackLoader.FromZip(stream, options ?? App);
    }

    private static byte[] Json(JsonObject manifest) => Encoding.UTF8.GetBytes(manifest.ToJsonString());

    private static byte[] Wav(double seconds, int sampleRate = 8000)
    {
        var dataSize = (int)(seconds * sampleRate); // 8-bit mono
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        return stream.ToArray();
    }

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
        ident[11] = 1; // channels
        BinaryPrimitives.WriteUInt32LittleEndian(ident.AsSpan(12), sampleRate);
        return [.. OggPage(0, ident, 2), .. OggPage((long)(seconds * sampleRate), new byte[10], 4)];
    }

    private static JsonObject WithSounds(JsonObject manifest, string reminder = "sounds/chime.wav")
    {
        manifest["sounds"] = new JsonObject { ["reminder"] = reminder };
        return manifest;
    }

    // ---- a good pack

    [Fact]
    public void A_complete_character_loads_with_every_part()
    {
        var manifest = Character();
        manifest["motion"] = new JsonObject { ["bob"] = 2.0 };
        manifest["poses"] = new JsonObject
        {
            ["happy"] = new JsonObject
            {
                ["eyes"] = "happy",
                ["mouth"] = "grin",
                ["arms"] = new JsonObject { ["left"] = new JsonArray(5, 64), ["right"] = new JsonArray(95, 64) },
                ["effects"] = new JsonArray("sparkle"),
            },
        };

        var result = Load(manifest);

        Assert.True(result.Ok, result.Error);
        var pack = Assert.IsType<CharacterPack>(result.Pack);
        Assert.Equal("tester.drip", pack.Header.Id);
        Assert.Equal(PackKind.Character, pack.Header.Kind);
        Assert.Equal(11, pack.Anchors.Count);
        Assert.Single(pack.Body);
        Assert.Equal(2.0, pack.Motion.Bob);
        Assert.Equal(new PackColor(0x2B, 0xB5, 0xA6), pack.Arms.Fill);

        var body = Assert.IsType<PathShape>(pack.Body[0]);
        var gradient = Assert.IsType<GradientFill>(body.Fill);
        Assert.Equal(new PackColor(0xA6, 0xED, 0xE3), gradient.From);
        Assert.Equal(new PackColor(0x12, 0x7A, 0x70), body.Stroke!.Color);

        var happy = pack.Poses[StandardPose.Happy];
        Assert.Equal(EyeStyle.Happy, happy.Eyes);
        Assert.Equal(MouthStyle.Grin, happy.Mouth);
        Assert.Equal(new PackPoint(95, 64), happy.RightHand);
        Assert.Equal([PoseEffect.Sparkle], happy.Effects);
    }

    [Fact]
    public void An_accessory_loads_with_its_slot_and_parts()
    {
        var result = Load(Accessory());

        Assert.True(result.Ok, result.Error);
        var pack = Assert.IsType<AccessoryPack>(result.Pack);
        Assert.Equal(AccessorySlot.Hat, pack.Slot);
        Assert.True(pack.InFront);
        Assert.Single(pack.Parts);
    }

    [Fact]
    public void Eight_digit_colours_carry_their_transparency()
    {
        var manifest = Character();
        ((JsonObject)((JsonArray)manifest["body"]!)[0]!)["stroke"] = new JsonObject { ["color"] = "#FFFFFF8C", ["width"] = 4 };

        var body = (PathShape)((CharacterPack)Load(manifest).Pack!).Body[0];

        Assert.Equal(new PackColor(255, 255, 255, 0x8C), body.Stroke!.Color);
    }

    [Fact]
    public void Motion_outside_the_safe_range_is_brought_back_in_range_not_refused()
    {
        var manifest = Character();
        manifest["motion"] = new JsonObject { ["bob"] = 99, ["bobSpeed"] = 0, ["blinkEveryMs"] = 5, ["squash"] = 3 };

        var motion = ((CharacterPack)Load(manifest).Pack!).Motion;

        Assert.Equal(4, motion.Bob);
        Assert.Equal(0.5, motion.BobSpeed);
        Assert.Equal(2000, motion.BlinkEveryMs);
        Assert.Equal(0.05, motion.Squash);
    }

    [Fact]
    public void A_pose_the_app_does_not_know_is_ignored_because_packs_cannot_add_poses()
    {
        var manifest = Character();
        manifest["poses"] = new JsonObject { ["dancing"] = new JsonObject { ["eyes"] = "happy" } };

        var pack = (CharacterPack)Load(manifest).Pack!;

        Assert.Empty(pack.Poses);
    }

    [Fact]
    public void Reserved_keys_for_later_versions_are_accepted_and_ignored()
    {
        var manifest = Character();
        manifest["messages"] = "messages.json";
        manifest["editor"] = new JsonObject { ["recipe"] = "anything" };

        Assert.True(Load(manifest).Ok);
    }

    // ---- rules a pack must follow

    [Fact]
    public void Every_rule_in_the_format_is_enforced_with_a_short_reason()
    {
        var cases = new List<(string Name, JsonObject Manifest, string Expected)>();

        void Add(string name, Action<JsonObject> change, string expected, Func<JsonObject>? start = null)
        {
            var manifest = (start ?? Character)();
            change(manifest);
            cases.Add((name, manifest, expected));
        }

        JsonObject FirstBody(JsonObject m) => (JsonObject)((JsonArray)m["body"]!)[0]!;

        Add("missing anchor", m => ((JsonObject)m["anchors"]!).Remove("mouth"), "mouth anchor");
        Add("anchor out of range", m => ((JsonObject)m["anchors"]!)["mouth"] = new JsonArray(900, 98), "out of range");
        Add("anchor is not two numbers", m => ((JsonObject)m["anchors"]!)["mouth"] = new JsonArray(1), "needs 2 numbers");
        Add("newer format", m => m["formatVersion"] = 2, "newer version");
        Add("no format version", m => m.Remove("formatVersion"), "format version");
        Add("format version zero", m => m["formatVersion"] = 0, "invalid format version");
        Add("bad kind", m => m["kind"] = "monster", "kind");
        Add("bad id", m => m["id"] = "Bad Id", "id must look like");
        Add("id without author", m => m["id"] = "justname", "id must look like");
        Add("reserved id", m => m["id"] = "heysip.thief", "reserved");
        Add("empty name", m => m["name"] = "  ", "empty");
        Add("long name", m => m["name"] = new string('x', 40), "too long");
        Add("control characters in name", m => m["name"] = "bad\u0007name", "aren't allowed");
        Add("bad version", m => m["version"] = "one", "version must look like");
        Add("needs newer app", m => m["minAppVersion"] = "9.0.0", "needs HeySip 9.0.0");
        Add("unknown required feature", m => m["requires"] = new JsonArray("holograms"), "features");
        Add("bad tier", m => m["distribution"] = new JsonObject { ["tier"] = "gold" }, "tier");
        Add("palette missing light", m => ((JsonObject)m["palette"]!).Remove("light"), "missing \"light\"");
        Add("palette colour invalid", m => ((JsonObject)m["palette"]!)["extra"] = "red", "isn't a valid colour");
        Add("palette name invalid", m => ((JsonObject)m["palette"]!)["Bad Name"] = "#FFFFFF", "isn't allowed");
        Add("too many palette colours", m =>
        {
            for (var i = 0; i < 14; i++)
            {
                ((JsonObject)m["palette"]!)["c" + i] = "#FFFFFF";
            }
        }, "too many colours");
        Add("unknown palette reference", m => FirstBody(m)["fill"] = "$nope", "isn't in the palette");
        Add("empty body", m => m["body"] = new JsonArray(), "no shapes");
        Add("no body", m => m.Remove("body"), "no \"body\"");
        Add("shape of no kind", m => FirstBody(m).Remove("path"), "exactly one");
        Add("shape of two kinds", m => FirstBody(m)["line"] = new JsonArray(0, 0, 1, 1), "exactly one");
        Add("shape with no paint", m => { FirstBody(m).Remove("fill"); FirstBody(m).Remove("stroke"); }, "neither a fill nor a stroke");
        Add("path with a forbidden command", m => FirstBody(m)["path"] = "M0,0 X5,5", "isn't allowed");
        Add("path starting with a line", m => FirstBody(m)["path"] = "L5,5", "must start with M");
        Add("path with a missing number", m => FirstBody(m)["path"] = "M5 L5", "malformed number");
        Add("path that is too long", m => FirstBody(m)["path"] = "M0,0 " + string.Concat(Enumerable.Repeat("L1,1 ", 1200)), "too long");
        Add("path with too many segments", m => FirstBody(m)["path"] = "M0,0 " + string.Concat(Enumerable.Repeat("L1,1 ", 300)), "too many segments");
        Add("path number too large", m => FirstBody(m)["path"] = "M0,0 L99999,5", "too large");
        Add("ellipse out of range", m => m["feet"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(500, 0, 5, 5), ["fill"] = "#FFFFFF" }), "out of range");
        Add("ellipse with no size", m => m["feet"] = new JsonArray(new JsonObject { ["ellipse"] = new JsonArray(5, 5, 0, 5), ["fill"] = "#FFFFFF" }), "out of range");
        Add("bad stroke join", m => FirstBody(m)["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 2, ["join"] = "spiky" }, "cap must be");
        Add("stroke too wide", m => FirstBody(m)["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 99 }, "\"width\"");
        Add("bad opacity", m => FirstBody(m)["opacity"] = 2, "\"opacity\"");
        Add("line with no stroke", m => m["feet"] = new JsonArray(new JsonObject { ["line"] = new JsonArray(0, 0, 5, 5), ["fill"] = "#FFFFFF" }), "needs a stroke");
        Add("too many shapes in one list", m =>
        {
            var many = new JsonArray();
            for (var i = 0; i < 65; i++)
            {
                many.Add(new JsonObject { ["ellipse"] = new JsonArray(50, 50, 5, 5), ["fill"] = "#FFFFFF" });
            }

            m["body"] = many;
        }, "too many shapes");
        Add("too many shapes in total", m =>
        {
            JsonArray Sixty() => new(Enumerable.Range(0, 64).Select(_ => (JsonNode)new JsonObject { ["ellipse"] = new JsonArray(0, 0, 5, 5), ["fill"] = "#FFFFFF" }).ToArray());
            var eyes = new JsonObject();
            foreach (var style in new[] { "open", "happy", "rest", "closed", "openRight", "happyRight" })
            {
                eyes[style] = Sixty();
            }

            m["body"] = Sixty();
            m["feet"] = Sixty();
            m["face"] = new JsonObject { ["eyes"] = eyes, ["mouths"] = new JsonObject { ["smile"] = Sixty(), ["grin"] = Sixty() } };
        }, "too many shapes in total");
        Add("bad pose value", m => m["poses"] = new JsonObject { ["happy"] = new JsonObject { ["eyes"] = "laser" } }, "allowed values");
        Add("numeric enum is not a name", m => m["poses"] = new JsonObject { ["happy"] = new JsonObject { ["eyes"] = "1" } }, "allowed values");
        Add("hand out of range", m => m["poses"] = new JsonObject { ["happy"] = new JsonObject { ["arms"] = new JsonObject { ["left"] = new JsonArray(0, 9999) } } }, "out of range");
        Add("bad sound name", m => WithSounds(m, "../evil.wav"), "file name that isn't allowed");
        Add("accessory without a slot", m => m.Remove("slot"), "no \"slot\"", Accessory);
        Add("accessory with a bad slot", m => m["slot"] = "crown", "allowed values", Accessory);
        Add("accessory with a bad layer", m => m["layer"] = "middle", "layer", Accessory);
        Add("accessory with no parts", m => m["parts"] = new JsonArray(), "no shapes", Accessory);

        var failures = new List<string>();
        foreach (var (name, manifest, expected) in cases)
        {
            var result = Load(manifest);
            if (result.Ok || result.Error is null || !result.Error.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{name}: expected \"{expected}\" but got {(result.Ok ? "an accepted pack" : $"\"{result.Error}\"")}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Built_in_packs_may_use_the_reserved_prefix_and_others_may_not()
    {
        var manifest = Character();
        manifest["id"] = "heysip.drip";

        Assert.False(Load(manifest).Ok);
        Assert.True(Load(manifest, App with { BuiltIn = true }).Ok);
    }

    [Fact]
    public void A_pack_for_this_or_an_older_app_version_is_accepted()
    {
        var manifest = Character();
        manifest["minAppVersion"] = "1.0.0";

        Assert.True(Load(manifest).Ok);
    }

    [Fact]
    public void Text_that_is_not_json_or_is_nested_too_deeply_is_refused_not_thrown()
    {
        Assert.Equal("pack.json isn't valid", LoadText("{ not json").Error);
        Assert.Equal("pack.json isn't valid", LoadText("").Error);
        Assert.Equal("pack.json isn't valid", LoadText("{\"a\":" + new string('[', 40) + new string(']', 40) + "}").Error);
        Assert.Equal("pack.json must be an object", LoadText("[1,2,3]").Error);
        Assert.Equal("pack.json isn't valid", LoadText("{\"formatVersion\":1, /* comment */ \"kind\":\"character\"}").Error);
    }

    [Fact]
    public void A_manifest_saved_with_a_byte_order_mark_loads_like_any_other()
    {
        // Notepad and Windows PowerShell put these three bytes at the start of a UTF-8 file.
        byte[] withMark = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Character().ToJsonString())];

        var result = PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = withMark }, App);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("tester.drip", result.Pack!.Header.Id);
    }

    [Fact]
    public void A_manifest_saved_with_a_byte_order_mark_loads_from_a_zip_and_a_folder_too()
    {
        byte[] withMark = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Character().ToJsonString())];

        Assert.True(LoadZip(Zip([("pack.json", withMark)])).Ok);

        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "pack.json"), withMark);
        Assert.True(PackLoader.FromFolder(_folder, App).Ok);
    }

    [Fact]
    public void Garbage_that_only_starts_like_a_byte_order_mark_is_still_refused()
    {
        var result = PackLoader.FromFiles(new Dictionary<string, byte[]> { ["pack.json"] = [0xEF, 0xBB, 0x00, 0x7B, 0x7D] }, App);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Missing_manifest_is_refused()
    {
        var result = PackLoader.FromFiles(new Dictionary<string, byte[]>(), App);

        Assert.Contains("no pack.json", result.Error);
    }

    // ---- hostile archives

    [Fact]
    public void A_valid_zip_loads()
    {
        var zip = Zip([("pack.json", Json(Character()))]);

        var result = LoadZip(zip);

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void Something_that_is_not_a_zip_is_refused()
    {
        Assert.Equal("This isn't a valid pack file", LoadZip(Encoding.UTF8.GetBytes("definitely not a zip file")).Error);
        Assert.Equal("This isn't a valid pack file", LoadZip([]).Error);
    }

    [Fact]
    public void A_pack_file_over_one_megabyte_is_refused()
    {
        var noise = new byte[PackLimits.MaxPackBytes + 10];
        new Random(1).NextBytes(noise);

        Assert.Contains("too big", LoadZip(noise).Error);
    }

    [Fact]
    public void A_zip_that_expands_far_beyond_its_size_is_refused()
    {
        // 3 MB of spaces compresses to a few KB, so the packed size looks innocent.
        var manifest = Encoding.UTF8.GetBytes(Character().ToJsonString() + new string(' ', 3 * 1024 * 1024));
        var zip = Zip([("pack.json", manifest)]);
        Assert.True(zip.Length < PackLimits.MaxPackBytes);

        Assert.Contains("too big", LoadZip(zip).Error);
    }

    [Fact]
    public void Too_many_files_are_refused()
    {
        var entries = new List<(string, byte[])> { ("pack.json", Json(Character())) };
        for (var i = 0; i < PackLimits.MaxFiles; i++)
        {
            entries.Add(($"sounds/s{i}.wav", Wav(0.1)));
        }

        Assert.Contains("too many files", LoadZip(Zip(entries)).Error);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sounds/../../evil.wav")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("sounds\\evil.wav")]
    [InlineData("readme.txt")]
    [InlineData("docs/readme.txt")]
    [InlineData("Pack.json")]
    [InlineData("sounds/Evil.WAV")]
    [InlineData("sounds/noextension")]
    public void Files_the_format_does_not_name_are_refused(string name)
    {
        var zip = Zip([("pack.json", Json(Character())), (name, [1, 2, 3])]);

        var result = LoadZip(zip);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void A_file_listed_twice_is_refused()
    {
        var json = Json(Character());
        var zip = Zip([("pack.json", json), ("pack.json", json)]);

        Assert.Contains("twice", LoadZip(zip).Error);
    }

    [Fact]
    public void A_symbolic_link_inside_a_zip_is_refused()
    {
        const int symlinkMode = 0xA000 | 0x1FF; // unix "lrwxrwxrwx"
        var zip = Zip([("pack.json", Json(Character()))], entry => entry.ExternalAttributes = symlinkMode << 16);

        Assert.Contains("link", LoadZip(zip).Error);
    }

    [Fact]
    public void An_oversized_manifest_is_refused()
    {
        var zip = Zip([("pack.json", new byte[PackLimits.MaxJsonBytes + 1])]);

        Assert.Contains("too big", LoadZip(zip).Error);
    }

    [Fact]
    public void A_zip_without_a_manifest_is_refused()
    {
        var zip = Zip([("pack.sig", [1, 2, 3])]);

        Assert.Contains("no pack.json", LoadZip(zip).Error);
    }

    // ---- sounds

    [Fact]
    public void A_pack_with_a_valid_wav_sound_loads_it()
    {
        var wav = Wav(0.5);
        var zip = Zip([("pack.json", Json(WithSounds(Character()))), ("sounds/chime.wav", wav)]);

        var result = LoadZip(zip);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(wav, result.Sounds["sounds/chime.wav"]);
    }

    [Fact]
    public void A_pack_with_a_valid_ogg_sound_loads_it()
    {
        var zip = Zip([("pack.json", Json(WithSounds(Character(), "sounds/chime.ogg"))), ("sounds/chime.ogg", Ogg(1.5))]);

        Assert.True(LoadZip(zip).Ok);
    }

    [Fact]
    public void A_sound_longer_than_five_seconds_is_refused()
    {
        var wav = Zip([("pack.json", Json(WithSounds(Character()))), ("sounds/chime.wav", Wav(6))]);
        var ogg = Zip([("pack.json", Json(WithSounds(Character(), "sounds/chime.ogg"))), ("sounds/chime.ogg", Ogg(6))]);

        Assert.Contains("longer than 5 seconds", LoadZip(wav).Error);
        Assert.Contains("longer than 5 seconds", LoadZip(ogg).Error);
    }

    [Fact]
    public void A_sound_that_is_not_really_audio_is_refused()
    {
        var zip = Zip([("pack.json", Json(WithSounds(Character()))), ("sounds/chime.wav", Encoding.ASCII.GetBytes("not audio at all"))]);

        Assert.Contains("isn't a valid WAV or OGG", LoadZip(zip).Error);
    }

    [Fact]
    public void An_ogg_with_a_wav_name_or_a_truncated_header_is_refused()
    {
        var ogg = Ogg(1);
        var wrongName = Zip([("pack.json", Json(WithSounds(Character()))), ("sounds/chime.wav", ogg)]);
        var truncated = Zip([("pack.json", Json(WithSounds(Character(), "sounds/chime.ogg"))), ("sounds/chime.ogg", ogg[..40])]);

        Assert.False(LoadZip(wrongName).Ok);
        Assert.False(LoadZip(truncated).Ok);
    }

    [Fact]
    public void A_sound_over_two_hundred_kilobytes_is_refused()
    {
        var zip = Zip([("pack.json", Json(WithSounds(Character()))), ("sounds/chime.wav", Wav(4.5, 48000))]);

        Assert.Contains("too big", LoadZip(zip).Error);
    }

    [Fact]
    public void A_sound_the_pack_names_but_does_not_include_is_refused()
    {
        var zip = Zip([("pack.json", Json(WithSounds(Character())))]);

        Assert.Contains("missing", LoadZip(zip).Error);
    }

    [Fact]
    public void A_sound_the_pack_includes_but_does_not_use_is_refused()
    {
        var zip = Zip([("pack.json", Json(Character())), ("sounds/extra.wav", Wav(0.2))]);

        Assert.Contains("doesn't use", LoadZip(zip).Error);
    }

    // ---- folders

    [Fact]
    public void A_folder_loads_like_a_zip()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "sounds"));
        File.WriteAllBytes(Path.Combine(_folder, "pack.json"), Json(WithSounds(Character())));
        File.WriteAllBytes(Path.Combine(_folder, "sounds", "chime.wav"), Wav(0.3));

        var result = PackLoader.FromFolder(_folder, App);

        Assert.True(result.Ok, result.Error);
        Assert.Single(result.Sounds);
    }

    [Fact]
    public void A_folder_with_other_folders_or_files_is_refused()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "scripts"));
        File.WriteAllBytes(Path.Combine(_folder, "pack.json"), Json(Character()));

        Assert.Contains("unexpected folder", PackLoader.FromFolder(_folder, App).Error);

        Directory.Delete(Path.Combine(_folder, "scripts"));
        File.WriteAllText(Path.Combine(_folder, "run.bat"), "calc");

        Assert.Contains("shouldn't", PackLoader.FromFolder(_folder, App).Error);
    }

    [Fact]
    public void A_missing_folder_is_refused()
    {
        Assert.Contains("wasn't found", PackLoader.FromFolder(Path.Combine(_folder, "nope"), App).Error);
    }

    // ---- path data and sounds on their own

    [Theory]
    [InlineData("M50,6 C50,6 14,54 14,88 A36,36 0 0 0 86,88 C86,54 50,6 50,6 Z")]
    [InlineData("M0 0 L10 10 H5 V3 Q1 2 3 4 T9 9 S1 2 3 4 z")]
    [InlineData("m10,10 l5,5 5,5 5,5")]
    [InlineData("M-6,2 Q0,-7 6,2")]
    [InlineData("M0,0 a5,5 0 1110,0")]
    [InlineData("M1e1,2E-1 L3,4")]
    public void Ordinary_path_data_is_accepted(string data)
    {
        Assert.Null(PathDataCheck.Validate(data));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Z")]
    [InlineData("M")]
    [InlineData("M1")]
    [InlineData("M1,2 L")]
    [InlineData("M1,2 L3")]
    [InlineData("M1,2 R3,4")]
    [InlineData("M1,2 L3,x")]
    [InlineData("M1,2 L3,4 javascript")]
    [InlineData("M1,2 A5,5 0 2 0 3,3")]
    [InlineData("M1,2 L1e999,4")]
    [InlineData("M1,2 L5000,4")]
    public void Malformed_or_forbidden_path_data_is_refused(string data)
    {
        Assert.NotNull(PathDataCheck.Validate(data));
    }

    [Fact]
    public void Sound_files_are_judged_by_their_contents_not_their_names()
    {
        Assert.Null(SoundCheck.Validate("a.wav", Wav(1)));
        Assert.Null(SoundCheck.Validate("a.ogg", Ogg(1)));
        Assert.NotNull(SoundCheck.Validate("a.mp3", Wav(1)));
        Assert.NotNull(SoundCheck.Validate("a.wav", []));
        Assert.NotNull(SoundCheck.Validate("a.ogg", Wav(1)));
    }
}
