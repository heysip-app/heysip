using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>A pack failed a check. The message is short and safe to show to the user.</summary>
internal sealed class InvalidPackException(string message) : Exception(message);

/// <summary>Turns the text of a pack.json into a pack, checking every rule in the format as it goes.</summary>
internal sealed partial class PackParser
{
    private readonly record struct Box(double MinX, double MaxX, double MinY, double MaxY);

    private static readonly Box Absolute = new(PackLimits.MinX, PackLimits.MaxX, PackLimits.MinY, PackLimits.MaxY);
    private static readonly Box Relative = new(-PackLimits.MaxRelative, PackLimits.MaxRelative, -PackLimits.MaxRelative, PackLimits.MaxRelative);

    private readonly PackLoadOptions _options;
    private readonly Dictionary<string, PackColor> _palette = new(StringComparer.Ordinal);
    private int _shapesLeft = PackLimits.MaxShapesTotal;

    private PackParser(PackLoadOptions options) => _options = options;

    public static Pack Parse(ReadOnlyMemory<byte> json, PackLoadOptions options)
    {
        var documentOptions = new JsonDocumentOptions
        {
            MaxDepth = PackLimits.MaxJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };

        // Editors on Windows (Notepad, PowerShell) save UTF-8 with a three-byte "byte order mark" in front. It is not part of
        // the JSON, so it must not make a perfectly good pack invalid.
        var span = json.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            json = json[3..];
        }

        using var document = JsonDocument.Parse(json, documentOptions);
        return new PackParser(options).ReadPack(document.RootElement);
    }

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorPattern();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]{0,31}$")]
    private static partial Regex PaletteNamePattern();

    private static InvalidPackException Fail(string message) => new(message);

    // ---- pack

    private Pack ReadPack(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Fail("pack.json must be an object");
        }

        var header = ReadHeader(root);
        ReadPalette(root, header.Kind);

        return header.Kind == PackKind.Character ? ReadCharacter(root, header) : ReadAccessory(root, header);
    }

    private PackHeader ReadHeader(JsonElement root)
    {
        if (!root.TryGetProperty("formatVersion", out var versionElement) || versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var formatVersion))
        {
            throw Fail("the pack has no format version");
        }

        if (formatVersion > PackLimits.FormatVersion)
        {
            throw Fail("This pack needs a newer version of HeySip");
        }

        if (formatVersion < 1)
        {
            throw Fail("the pack has an invalid format version");
        }

        var kind = Text(root, "kind", required: true, 16) switch
        {
            "character" => PackKind.Character,
            "accessory" => PackKind.Accessory,
            _ => throw Fail("the pack's kind must be \"character\" or \"accessory\""),
        };

        var id = Text(root, "id", required: true, 64)!;
        if (!PackId.IsValid(id))
        {
            throw Fail("the pack's id must look like author.name, in lower case");
        }

        if (id.StartsWith(PackId.BuiltInPrefix, StringComparison.Ordinal) && !_options.BuiltIn)
        {
            throw Fail("ids starting with \"heysip.\" are reserved");
        }

        var name = Text(root, "name", required: true, PackLimits.MaxNameLength)!;
        var version = Text(root, "version", required: true, 40)!;
        if (!PackVersion.IsValid(version))
        {
            throw Fail("the pack's version must look like 1.2.3");
        }

        var minApp = Text(root, "minAppVersion", required: false, 20);
        if (minApp is not null)
        {
            if (!Version.TryParse(minApp, out var needed))
            {
                throw Fail("the pack's minAppVersion isn't a version number");
            }

            if (needed > _options.AppVersion)
            {
                throw Fail($"This pack needs HeySip {needed} or newer");
            }
        }

        if (root.TryGetProperty("requires", out var requires) && requires.ValueKind == JsonValueKind.Array && requires.GetArrayLength() > 0)
        {
            throw Fail("This pack needs features this version of HeySip doesn't have");
        }

        var tier = PackTier.Free;
        string? storeId = null;
        if (root.TryGetProperty("distribution", out var distribution) && distribution.ValueKind == JsonValueKind.Object)
        {
            tier = Text(distribution, "tier", required: false, 10) switch
            {
                null or "free" => PackTier.Free,
                "premium" => PackTier.Premium,
                _ => throw Fail("the pack's tier must be \"free\" or \"premium\""),
            };
            storeId = Text(distribution, "storeId", required: false, 64);
        }

        return new PackHeader(
            formatVersion,
            kind,
            id,
            name,
            version,
            Text(root, "author", required: false, 64) ?? "",
            Text(root, "license", required: false, 64) ?? "",
            Text(root, "description", required: false, 256) ?? "",
            minApp,
            tier,
            storeId);
    }

    private void ReadPalette(JsonElement root, PackKind kind)
    {
        if (root.TryGetProperty("palette", out var palette))
        {
            if (palette.ValueKind != JsonValueKind.Object)
            {
                throw Fail("the palette must be an object");
            }

            foreach (var entry in palette.EnumerateObject())
            {
                if (_palette.Count >= PackLimits.MaxPaletteColors)
                {
                    throw Fail("the palette has too many colours");
                }

                if (!PaletteNamePattern().IsMatch(entry.Name))
                {
                    throw Fail($"the palette colour name \"{Shorten(entry.Name)}\" isn't allowed");
                }

                if (entry.Value.ValueKind != JsonValueKind.String || ParseHex(entry.Value.GetString()!) is not { } color)
                {
                    throw Fail($"the palette colour \"{entry.Name}\" isn't a valid colour");
                }

                _palette[entry.Name] = color;
            }
        }

        if (kind == PackKind.Character)
        {
            foreach (var required in new[] { "light", "deep", "outline" })
            {
                if (!_palette.ContainsKey(required))
                {
                    throw Fail($"the palette is missing \"{required}\"");
                }
            }
        }
    }

    // ---- character

    private CharacterPack ReadCharacter(JsonElement root, PackHeader header)
    {
        var anchors = ReadAnchors(root);

        var body = RequireShapes(root, "body", Absolute);
        var feet = root.TryGetProperty("feet", out _) ? RequireShapes(root, "feet", Absolute, allowEmpty: true) : [];

        return new CharacterPack(
            header,
            new Dictionary<string, PackColor>(_palette),
            ReadMotion(root),
            anchors,
            body,
            feet,
            ReadArms(root),
            ReadPoses(root),
            ReadFace(root),
            ReadSounds(root));
    }

    private Dictionary<string, PackPoint> ReadAnchors(JsonElement root)
    {
        if (!root.TryGetProperty("anchors", out var anchors) || anchors.ValueKind != JsonValueKind.Object)
        {
            throw Fail("the character has no anchors");
        }

        var result = new Dictionary<string, PackPoint>(StringComparer.Ordinal);
        foreach (var name in CharacterPack.RequiredAnchors)
        {
            if (!anchors.TryGetProperty(name, out var point))
            {
                throw Fail($"This character is missing its {name} anchor");
            }

            result[name] = ReadPoint(point, $"the {name} anchor", Absolute);
        }

        return result;
    }

    private static PackMotion ReadMotion(JsonElement root)
    {
        var motion = PackMotion.Default;
        if (!root.TryGetProperty("motion", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return motion;
        }

        // Out-of-range values are brought into range, so a pack can never make the character flash or spin.
        return new PackMotion(
            Clamped(element, "bob", motion.Bob, 0, 4),
            Clamped(element, "bobSpeed", motion.BobSpeed, 0.5, 4),
            Clamped(element, "squash", motion.Squash, 0, 0.05),
            (int)Clamped(element, "blinkEveryMs", motion.BlinkEveryMs, 2000, 10000),
            (int)Clamped(element, "blinkMs", motion.BlinkMs, 80, 300));
    }

    private static double Clamped(JsonElement element, string name, double fallback, double min, double max) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? Math.Clamp(number, min, max)
            : fallback;

    private ArmStyle ReadArms(JsonElement root)
    {
        var fill = _palette["deep"];
        var outline = _palette["outline"];
        double width = 5;
        double outlineWidth = 8;

        if (root.TryGetProperty("arms", out var arms))
        {
            if (arms.ValueKind != JsonValueKind.Object)
            {
                throw Fail("arms must be an object");
            }

            fill = OptionalColor(arms, "fill") ?? fill;
            outline = OptionalColor(arms, "outline") ?? outline;
            width = OptionalNumber(arms, "width", 0, PackLimits.MaxStrokeWidth) ?? width;
            outlineWidth = OptionalNumber(arms, "outlineWidth", 0, PackLimits.MaxStrokeWidth) ?? outlineWidth;
        }

        return new ArmStyle(fill, outline, width, outlineWidth);
    }

    private Dictionary<StandardPose, PoseOverride> ReadPoses(JsonElement root)
    {
        var result = new Dictionary<StandardPose, PoseOverride>();
        if (!root.TryGetProperty("poses", out var poses))
        {
            return result;
        }

        if (poses.ValueKind != JsonValueKind.Object)
        {
            throw Fail("poses must be an object");
        }

        foreach (var entry in poses.EnumerateObject())
        {
            // Packs override the standard poses only; a name the app doesn't know is simply never used.
            if (!TryParseEnum<StandardPose>(entry.Name, out var pose))
            {
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                throw Fail($"the {entry.Name} pose must be an object");
            }

            result[pose] = ReadPose(entry.Value, entry.Name);
        }

        return result;
    }

    private PoseOverride ReadPose(JsonElement element, string name)
    {
        PackPoint? left = null;
        PackPoint? right = null;
        if (element.TryGetProperty("arms", out var arms))
        {
            if (arms.ValueKind != JsonValueKind.Object)
            {
                throw Fail($"the {name} pose's arms must be an object");
            }

            left = arms.TryGetProperty("left", out var l) ? ReadPoint(l, $"the {name} pose's left hand", Absolute) : null;
            right = arms.TryGetProperty("right", out var r) ? ReadPoint(r, $"the {name} pose's right hand", Absolute) : null;
        }

        ArmSway? sway = null;
        if (element.TryGetProperty("sway", out var swayElement))
        {
            if (swayElement.ValueKind != JsonValueKind.Object)
            {
                throw Fail($"the {name} pose's sway must be an object");
            }

            sway = new ArmSway(
                Text(swayElement, "arm", required: false, 8) != "left",
                Clamped(swayElement, "amplitude", 5, 0, 15),
                Clamped(swayElement, "speed", 6, 0, 12));
        }

        List<PoseEffect>? effects = null;
        if (element.TryGetProperty("effects", out var effectList))
        {
            effects = [];
            if (effectList.ValueKind != JsonValueKind.Array || effectList.GetArrayLength() > 8)
            {
                throw Fail($"the {name} pose's effects must be a short list");
            }

            foreach (var item in effectList.EnumerateArray())
            {
                effects.Add(ParseEnumValue<PoseEffect>(item, $"an effect in the {name} pose"));
            }
        }

        return new PoseOverride(
            OptionalEnum<EyeStyle>(element, "eyes", name),
            OptionalEnum<MouthStyle>(element, "mouth", name),
            OptionalEnum<BrowStyle>(element, "brows", name),
            left,
            right,
            sway,
            effects?.Distinct().ToList());
    }

    private FaceShapes ReadFace(JsonElement root)
    {
        if (!root.TryGetProperty("face", out var face))
        {
            return FaceShapes.None;
        }

        if (face.ValueKind != JsonValueKind.Object)
        {
            throw Fail("face must be an object");
        }

        var eyes = new Dictionary<EyeStyle, IReadOnlyList<PackShape>>();
        var rightEyes = new Dictionary<EyeStyle, IReadOnlyList<PackShape>>();
        var mouths = new Dictionary<MouthStyle, IReadOnlyList<PackShape>>();
        var brows = new Dictionary<BrowStyle, IReadOnlyList<PackShape>>();

        if (face.TryGetProperty("eyes", out var eyeElement))
        {
            RequireObject(eyeElement, "face.eyes");
            foreach (var entry in eyeElement.EnumerateObject())
            {
                var isRight = entry.Name.EndsWith("Right", StringComparison.OrdinalIgnoreCase);
                var styleName = isRight ? entry.Name[..^"Right".Length] : entry.Name;
                if (!TryParseEnum<EyeStyle>(styleName, out var style))
                {
                    continue;
                }

                (isRight ? rightEyes : eyes)[style] = Shapes(entry.Value, $"face.eyes.{entry.Name}", Relative);
            }
        }

        if (face.TryGetProperty("mouths", out var mouthElement))
        {
            RequireObject(mouthElement, "face.mouths");
            foreach (var entry in mouthElement.EnumerateObject())
            {
                if (TryParseEnum<MouthStyle>(entry.Name, out var style))
                {
                    mouths[style] = Shapes(entry.Value, $"face.mouths.{entry.Name}", Relative);
                }
            }
        }

        if (face.TryGetProperty("brows", out var browElement))
        {
            RequireObject(browElement, "face.brows");
            foreach (var entry in browElement.EnumerateObject())
            {
                if (TryParseEnum<BrowStyle>(entry.Name, out var style))
                {
                    brows[style] = Shapes(entry.Value, $"face.brows.{entry.Name}", Relative);
                }
            }
        }

        var mirrored = !(face.TryGetProperty("eyesMirrored", out var flag) && flag.ValueKind == JsonValueKind.False);
        return new FaceShapes(eyes, rightEyes, mouths, brows, mirrored);
    }

    private static PackSounds ReadSounds(JsonElement root)
    {
        if (!root.TryGetProperty("sounds", out var sounds))
        {
            return new PackSounds(null, null);
        }

        if (sounds.ValueKind != JsonValueKind.Object)
        {
            throw Fail("sounds must be an object");
        }

        return new PackSounds(
            SoundName(sounds, "reminder"),
            SoundName(sounds, "drank"));
    }

    private static string? SoundName(JsonElement sounds, string key)
    {
        var name = Text(sounds, key, required: false, 64);
        if (name is not null && !PackLoader.IsSoundFileName(name))
        {
            throw Fail($"the {key} sound has a file name that isn't allowed");
        }

        return name;
    }

    // ---- accessory

    private AccessoryPack ReadAccessory(JsonElement root, PackHeader header)
    {
        var slot = ParseEnumValue<AccessorySlot>(Require(root, "slot"), "the accessory slot");
        var inFront = Text(root, "layer", required: false, 8) switch
        {
            null or "front" => true,
            "back" => false,
            _ => throw Fail("an accessory's layer must be \"front\" or \"back\""),
        };

        return new AccessoryPack(header, slot, inFront, RequireShapes(root, "parts", Relative));
    }

    // ---- shapes

    private List<PackShape> RequireShapes(JsonElement root, string name, Box box, bool allowEmpty = false)
    {
        var shapes = Shapes(Require(root, name), name, box);
        if (shapes.Count == 0 && !allowEmpty)
        {
            throw Fail($"{name} has no shapes");
        }

        return shapes;
    }

    private List<PackShape> Shapes(JsonElement list, string where, Box box)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Fail($"{where} must be a list of shapes");
        }

        if (list.GetArrayLength() > PackLimits.MaxShapesPerList)
        {
            throw Fail($"{where} has too many shapes");
        }

        var result = new List<PackShape>();
        foreach (var item in list.EnumerateArray())
        {
            if (--_shapesLeft < 0)
            {
                throw Fail("the pack has too many shapes in total");
            }

            result.Add(ReadShape(item, where, box));
        }

        return result;
    }

    private PackShape ReadShape(JsonElement element, string where, Box box)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Fail($"a shape in {where} must be an object");
        }

        var hasPath = element.TryGetProperty("path", out var path);
        var hasEllipse = element.TryGetProperty("ellipse", out var ellipse);
        var hasLine = element.TryGetProperty("line", out var line);
        if ((hasPath ? 1 : 0) + (hasEllipse ? 1 : 0) + (hasLine ? 1 : 0) != 1)
        {
            throw Fail($"each shape in {where} must be exactly one of path, ellipse or line");
        }

        var fill = ReadFill(element);
        var stroke = ReadStroke(element);
        var opacity = OptionalNumber(element, "opacity", 0, 1) ?? 1;

        if (fill is null && stroke is null)
        {
            throw Fail($"a shape in {where} has neither a fill nor a stroke");
        }

        PackShape shape;
        if (hasPath)
        {
            if (path.ValueKind != JsonValueKind.String)
            {
                throw Fail($"a path in {where} must be text");
            }

            var data = path.GetString()!;
            if (PathDataCheck.Validate(data) is { } problem)
            {
                throw Fail($"in {where}, {problem}");
            }

            shape = new PathShape(data);
        }
        else if (hasEllipse)
        {
            var values = Numbers(ellipse, 4, $"an ellipse in {where}");
            if (!InBox(box, values[0], values[1]) || values[2] <= 0 || values[3] <= 0 || values[2] > PackLimits.MaxRadius || values[3] > PackLimits.MaxRadius)
            {
                throw Fail($"an ellipse in {where} is out of range");
            }

            shape = new EllipseShape(values[0], values[1], values[2], values[3]);
        }
        else
        {
            var values = Numbers(line, 4, $"a line in {where}");
            if (!InBox(box, values[0], values[1]) || !InBox(box, values[2], values[3]))
            {
                throw Fail($"a line in {where} is out of range");
            }

            if (stroke is null)
            {
                throw Fail($"a line in {where} needs a stroke");
            }

            shape = new LineShape(values[0], values[1], values[2], values[3]);
        }

        return shape with { Fill = fill, Stroke = stroke, Opacity = opacity };
    }

    private PackFill? ReadFill(JsonElement element)
    {
        if (!element.TryGetProperty("fill", out var fill))
        {
            return null;
        }

        if (fill.ValueKind == JsonValueKind.String)
        {
            return new SolidFill(ResolveColor(fill.GetString()!));
        }

        if (fill.ValueKind == JsonValueKind.Object
            && fill.TryGetProperty("gradient", out var gradient)
            && gradient.ValueKind == JsonValueKind.Array
            && gradient.GetArrayLength() == 2
            && gradient[0].ValueKind == JsonValueKind.String
            && gradient[1].ValueKind == JsonValueKind.String)
        {
            return new GradientFill(ResolveColor(gradient[0].GetString()!), ResolveColor(gradient[1].GetString()!));
        }

        throw Fail("a fill must be a colour or a gradient of two colours");
    }

    private PackStroke? ReadStroke(JsonElement element)
    {
        if (!element.TryGetProperty("stroke", out var stroke))
        {
            return null;
        }

        if (stroke.ValueKind != JsonValueKind.Object)
        {
            throw Fail("a stroke must be an object");
        }

        var color = OptionalColor(stroke, "color") ?? throw Fail("a stroke needs a colour");
        var width = OptionalNumber(stroke, "width", 0, PackLimits.MaxStrokeWidth) ?? throw Fail("a stroke needs a width");
        var cap = Text(stroke, "cap", required: false, 8);
        var join = Text(stroke, "join", required: false, 8);
        if (cap is not (null or "round" or "flat") || join is not (null or "round" or "miter"))
        {
            throw Fail("a stroke's cap must be round or flat, and its join round or miter");
        }

        return new PackStroke(color, width, cap != "flat", join != "miter");
    }

    // ---- small readers

    private static JsonElement Require(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw Fail($"the pack has no \"{name}\"");

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Fail($"{name} must be an object");
        }
    }

    private static string? Text(JsonElement element, string name, bool required, int maxLength)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return required ? throw Fail($"the pack has no \"{name}\"") : null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Fail($"\"{name}\" must be text");
        }

        var text = value.GetString()!.Trim();
        if (text.Length == 0 && required)
        {
            throw Fail($"\"{name}\" is empty");
        }

        if (text.Length > maxLength)
        {
            throw Fail($"\"{name}\" is too long");
        }

        if (text.Any(char.IsControl))
        {
            throw Fail($"\"{name}\" has characters that aren't allowed");
        }

        return text.Length == 0 ? null : text;
    }

    private static double[] Numbers(JsonElement element, int count, string what)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != count)
        {
            throw Fail($"{what} needs {count} numbers");
        }

        var result = new double[count];
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                throw Fail($"{what} has a value that isn't a number");
            }

            result[i++] = number;
        }

        return result;
    }

    private static PackPoint ReadPoint(JsonElement element, string what, Box box)
    {
        var values = Numbers(element, 2, what);
        return InBox(box, values[0], values[1]) ? new PackPoint(values[0], values[1]) : throw Fail($"{what} is out of range");
    }

    private static bool InBox(Box box, double x, double y) => x >= box.MinX && x <= box.MaxX && y >= box.MinY && y <= box.MaxY;

    private static double? OptionalNumber(JsonElement element, string name, double min, double max)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < min || number > max)
        {
            throw Fail($"\"{name}\" must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}");
        }

        return number;
    }

    private PackColor? OptionalColor(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? ResolveColor(value.GetString()!) : throw Fail($"\"{name}\" must be a colour");
    }

    private PackColor ResolveColor(string text)
    {
        if (text.StartsWith('$'))
        {
            return _palette.TryGetValue(text[1..], out var named) ? named : throw Fail($"the colour {Shorten(text)} isn't in the palette");
        }

        return ParseHex(text) ?? throw Fail($"\"{Shorten(text)}\" isn't a valid colour");
    }

    private static PackColor? ParseHex(string text)
    {
        if (!HexColorPattern().IsMatch(text))
        {
            return null;
        }

        byte Part(int index) => byte.Parse(text.AsSpan(1 + index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new PackColor(Part(0), Part(1), Part(2), text.Length == 9 ? Part(3) : (byte)255);
    }

    private static bool TryParseEnum<T>(string text, out T value)
        where T : struct, Enum
    {
        // Only names count; a number such as "1" must not be taken for a value.
        if (text.Length > 0 && char.IsLetter(text[0]) && Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static T ParseEnumValue<T>(JsonElement element, string what)
        where T : struct, Enum =>
        element.ValueKind == JsonValueKind.String && TryParseEnum<T>(element.GetString()!, out var value)
            ? value
            : throw Fail($"{what} isn't one of the allowed values");

    private static T? OptionalEnum<T>(JsonElement element, string name, string pose)
        where T : struct, Enum =>
        element.TryGetProperty(name, out var value) ? ParseEnumValue<T>(value, $"{name} in the {pose} pose") : null;

    private static string Shorten(string text) => text.Length <= 24 ? text : text[..24] + "…";
}
