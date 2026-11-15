using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>A pack failed a check. The message is short and safe to show to the user.</summary>
internal sealed class InvalidPackException(string message) : Exception(message);

/// <summary>Turns the text of a pack.json into a pack, checking every rule in the format as it goes.</summary>
internal sealed partial class PackParser
{
    private readonly record struct Box(double MinX, double MaxX, double MinY, double MaxY);

    private static readonly Box Absolute = new(PackLimits.MinX, PackLimits.MaxX, PackLimits.MinY, PackLimits.MaxY);
    private static readonly Box Relative = new(-PackLimits.MaxRelative, PackLimits.MaxRelative, -PackLimits.MaxRelative, PackLimits.MaxRelative);

    /// <summary>A shape sets exactly one of path, ellipse and line.</summary>
    private const int ExactlyOneShapeKind = 1;

    // Where each number sits in the list a point, an ellipse and a line are written as.
    private const int PointX = 0;
    private const int PointY = 1;
    private const int EllipseCenterX = 0;
    private const int EllipseCenterY = 1;
    private const int EllipseRadiusX = 2;
    private const int EllipseRadiusY = 3;
    private const int LineStartX = 0;
    private const int LineStartY = 1;
    private const int LineEndX = 2;
    private const int LineEndY = 3;

    // A colour is "#" and 6 hex digits (red, green, blue), or 8 with alpha last.
    private const int HexPrefixLength = 1;
    private const int HexDigitsPerPart = 2;
    private const int HexWithAlphaLength = HexPrefixLength + (4 * HexDigitsPerPart);
    private const int RedPart = 0;
    private const int GreenPart = 1;
    private const int BluePart = 2;
    private const int AlphaPart = 3;

    private readonly PackLoadOptions _options;
    private readonly Dictionary<string, PackColor> _palette = new(StringComparer.Ordinal);
    private int _shapesLeft = PackLimits.MaxShapesTotal;

    private PackParser(PackLoadOptions options) => _options = options;

    public static Pack Parse(ReadOnlyMemory<byte> json, PackLoadOptions options)
    {
        var documentOptions = JsonInput.Strict(PackLimits.MaxJsonDepth);

        // Editors on Windows (Notepad, PowerShell) save UTF-8 with a three-byte "byte order mark" in front. It is not part of
        // the JSON, so it must not make a perfectly good pack invalid.
        using var document = JsonDocument.Parse(JsonInput.WithoutByteOrderMark(json), documentOptions);
        return new PackParser(options).ReadPack(document.RootElement);
    }

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorPattern();

    /// <summary>A letter, then up to <c>PackLimits.MaxPaletteNameLength - 1</c> letters or digits (a test keeps the two equal).</summary>
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
        if (!root.TryGetProperty(JsonInput.FormatVersionKey, out var versionElement) || versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var formatVersion))
        {
            throw Fail("the pack has no format version");
        }

        if (formatVersion > PackLimits.FormatVersion)
        {
            throw Fail("This pack needs a newer version of HeySip");
        }

        if (formatVersion < PackLimits.FirstFormatVersion)
        {
            throw Fail("the pack has an invalid format version");
        }

        var kind = Text(root, PackKeys.Kind, required: true, PackLimits.MaxKindLength) switch
        {
            PackKeys.KindCharacter => PackKind.Character,
            PackKeys.KindAccessory => PackKind.Accessory,
            _ => throw Fail("the pack's kind must be \"character\" or \"accessory\""),
        };

        var id = Text(root, PackKeys.Id, required: true, PackLimits.MaxIdLength)!;
        if (!PackId.IsValid(id))
        {
            throw Fail("the pack's id must look like author.name, in lower case");
        }

        if (id.StartsWith(PackId.BuiltInPrefix, StringComparison.Ordinal) && !_options.BuiltIn)
        {
            throw Fail("ids starting with \"heysip.\" are reserved");
        }

        var name = Text(root, PackKeys.Name, required: true, PackLimits.MaxNameLength)!;
        var version = Text(root, PackKeys.Version, required: true, PackLimits.MaxVersionLength)!;
        if (!PackVersion.IsValid(version))
        {
            throw Fail("the pack's version must look like 1.2.3");
        }

        var minApp = Text(root, PackKeys.MinAppVersion, required: false, PackLimits.MaxMinAppVersionLength);
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

        if (root.TryGetProperty(PackKeys.Requires, out var requires) && requires.ValueKind == JsonValueKind.Array && requires.GetArrayLength() > 0)
        {
            throw Fail("This pack needs features this version of HeySip doesn't have");
        }

        var tier = PackTier.Free;
        string? storeId = null;
        if (root.TryGetProperty(PackKeys.Distribution, out var distribution) && distribution.ValueKind == JsonValueKind.Object)
        {
            tier = Text(distribution, PackKeys.Tier, required: false, PackLimits.MaxTierLength) switch
            {
                null or PackKeys.TierFree => PackTier.Free,
                PackKeys.TierPremium => PackTier.Premium,
                _ => throw Fail("the pack's tier must be \"free\" or \"premium\""),
            };
            storeId = Text(distribution, PackKeys.StoreId, required: false, PackLimits.MaxStoreIdLength);
        }

        return new PackHeader(
            formatVersion,
            kind,
            id,
            name,
            version,
            Text(root, PackKeys.Author, required: false, PackLimits.MaxAuthorLength) ?? "",
            Text(root, PackKeys.License, required: false, PackLimits.MaxLicenseLength) ?? "",
            Text(root, PackKeys.Description, required: false, PackLimits.MaxDescriptionLength) ?? "",
            minApp,
            tier,
            storeId);
    }

    private void ReadPalette(JsonElement root, PackKind kind)
    {
        if (root.TryGetProperty(PackKeys.Palette, out var palette))
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
            foreach (var required in PackKeys.RequiredPaletteColors)
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

        var body = RequireShapes(root, PackKeys.Body, Absolute);
        var feet = root.TryGetProperty(PackKeys.Feet, out _) ? RequireShapes(root, PackKeys.Feet, Absolute, allowEmpty: true) : [];

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
        if (!root.TryGetProperty(PackKeys.Anchors, out var anchors) || anchors.ValueKind != JsonValueKind.Object)
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
        if (!root.TryGetProperty(PackKeys.Motion, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return motion;
        }

        // Out-of-range values are brought into range, so a pack can never make the character flash or spin.
        return new PackMotion(
            Clamped(element, PackKeys.Bob, motion.Bob, PackLimits.Bob),
            Clamped(element, PackKeys.BobSpeed, motion.BobSpeed, PackLimits.BobSpeed),
            Clamped(element, PackKeys.Squash, motion.Squash, PackLimits.Squash),
            (int)Clamped(element, PackKeys.BlinkEveryMs, motion.BlinkEveryMs, PackLimits.BlinkEveryMs),
            (int)Clamped(element, PackKeys.BlinkMs, motion.BlinkMs, PackLimits.BlinkMs));
    }

    private static double Clamped(JsonElement element, string name, double fallback, PackRange range) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? range.Clamp(number)
            : fallback;

    private ArmStyle ReadArms(JsonElement root)
    {
        var fill = _palette[PackKeys.PaletteDeep];
        var outline = _palette[PackKeys.PaletteOutline];
        var width = PackLimits.DefaultArmWidth;
        var outlineWidth = PackLimits.DefaultArmOutlineWidth;

        if (root.TryGetProperty(PackKeys.Arms, out var arms))
        {
            if (arms.ValueKind != JsonValueKind.Object)
            {
                throw Fail("arms must be an object");
            }

            fill = OptionalColor(arms, PackKeys.Fill) ?? fill;
            outline = OptionalColor(arms, PackKeys.Outline) ?? outline;
            width = OptionalNumber(arms, PackKeys.Width, PackLimits.Stroke) ?? width;
            outlineWidth = OptionalNumber(arms, PackKeys.OutlineWidth, PackLimits.Stroke) ?? outlineWidth;
        }

        return new ArmStyle(fill, outline, width, outlineWidth);
    }

    private Dictionary<StandardPose, PoseOverride> ReadPoses(JsonElement root)
    {
        var result = new Dictionary<StandardPose, PoseOverride>();
        if (!root.TryGetProperty(PackKeys.Poses, out var poses))
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
        if (element.TryGetProperty(PackKeys.Arms, out var arms))
        {
            if (arms.ValueKind != JsonValueKind.Object)
            {
                throw Fail($"the {name} pose's arms must be an object");
            }

            left = arms.TryGetProperty(PackKeys.Left, out var l) ? ReadPoint(l, $"the {name} pose's left hand", Absolute) : null;
            right = arms.TryGetProperty(PackKeys.Right, out var r) ? ReadPoint(r, $"the {name} pose's right hand", Absolute) : null;
        }

        ArmSway? sway = null;
        if (element.TryGetProperty(PackKeys.Sway, out var swayElement))
        {
            if (swayElement.ValueKind != JsonValueKind.Object)
            {
                throw Fail($"the {name} pose's sway must be an object");
            }

            sway = new ArmSway(
                Text(swayElement, PackKeys.SwayArm, required: false, PackLimits.MaxKeywordLength) != PackKeys.Left,
                Clamped(swayElement, PackKeys.SwayAmplitude, PackLimits.DefaultSwayAmplitude, PackLimits.SwayAmplitude),
                Clamped(swayElement, PackKeys.SwaySpeed, PackLimits.DefaultSwaySpeed, PackLimits.SwaySpeed));
        }

        List<PoseEffect>? effects = null;
        if (element.TryGetProperty(PackKeys.Effects, out var effectList))
        {
            effects = [];
            if (effectList.ValueKind != JsonValueKind.Array || effectList.GetArrayLength() > PackLimits.MaxEffectsPerPose)
            {
                throw Fail($"the {name} pose's effects must be a short list");
            }

            foreach (var item in effectList.EnumerateArray())
            {
                effects.Add(ParseEnumValue<PoseEffect>(item, $"an effect in the {name} pose"));
            }
        }

        return new PoseOverride(
            OptionalEnum<EyeStyle>(element, PackKeys.Eyes, name),
            OptionalEnum<MouthStyle>(element, PackKeys.Mouth, name),
            OptionalEnum<BrowStyle>(element, PackKeys.Brows, name),
            left,
            right,
            sway,
            effects?.Distinct().ToList());
    }

    private FaceShapes ReadFace(JsonElement root)
    {
        if (!root.TryGetProperty(PackKeys.Face, out var face))
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

        if (face.TryGetProperty(PackKeys.FaceEyes, out var eyeElement))
        {
            RequireObject(eyeElement, FacePath(PackKeys.FaceEyes));
            foreach (var entry in eyeElement.EnumerateObject())
            {
                var isRight = entry.Name.EndsWith(PackKeys.RightEyeSuffix, StringComparison.OrdinalIgnoreCase);
                var styleName = isRight ? entry.Name[..^PackKeys.RightEyeSuffix.Length] : entry.Name;
                if (!TryParseEnum<EyeStyle>(styleName, out var style))
                {
                    continue;
                }

                (isRight ? rightEyes : eyes)[style] = Shapes(entry.Value, $"{FacePath(PackKeys.FaceEyes)}.{entry.Name}", Relative);
            }
        }

        if (face.TryGetProperty(PackKeys.FaceMouths, out var mouthElement))
        {
            RequireObject(mouthElement, FacePath(PackKeys.FaceMouths));
            foreach (var entry in mouthElement.EnumerateObject())
            {
                if (TryParseEnum<MouthStyle>(entry.Name, out var style))
                {
                    mouths[style] = Shapes(entry.Value, $"{FacePath(PackKeys.FaceMouths)}.{entry.Name}", Relative);
                }
            }
        }

        if (face.TryGetProperty(PackKeys.FaceBrows, out var browElement))
        {
            RequireObject(browElement, FacePath(PackKeys.FaceBrows));
            foreach (var entry in browElement.EnumerateObject())
            {
                if (TryParseEnum<BrowStyle>(entry.Name, out var style))
                {
                    brows[style] = Shapes(entry.Value, $"{FacePath(PackKeys.FaceBrows)}.{entry.Name}", Relative);
                }
            }
        }

        var mirrored = !(face.TryGetProperty(PackKeys.EyesMirrored, out var flag) && flag.ValueKind == JsonValueKind.False);
        return new FaceShapes(eyes, rightEyes, mouths, brows, mirrored);
    }

    /// <summary>How a part of the face is named in an error message, such as <c>face.eyes</c>.</summary>
    private static string FacePath(string part) => $"{PackKeys.Face}.{part}";

    private static PackSounds ReadSounds(JsonElement root)
    {
        if (!root.TryGetProperty(PackKeys.Sounds, out var sounds))
        {
            return new PackSounds(null, null);
        }

        if (sounds.ValueKind != JsonValueKind.Object)
        {
            throw Fail("sounds must be an object");
        }

        return new PackSounds(
            SoundName(sounds, PackKeys.SoundReminder),
            SoundName(sounds, PackKeys.SoundDrank));
    }

    private static string? SoundName(JsonElement sounds, string key)
    {
        var name = Text(sounds, key, required: false, PackLimits.MaxSoundNameLength);
        if (name is not null && !PackLoader.IsSoundFileName(name))
        {
            throw Fail($"the {key} sound has a file name that isn't allowed");
        }

        return name;
    }

    // ---- accessory

    private AccessoryPack ReadAccessory(JsonElement root, PackHeader header)
    {
        var slot = ParseEnumValue<AccessorySlot>(Require(root, PackKeys.Slot), "the accessory slot");
        var inFront = Text(root, PackKeys.Layer, required: false, PackLimits.MaxKeywordLength) switch
        {
            null or PackKeys.LayerFront => true,
            PackKeys.LayerBack => false,
            _ => throw Fail("an accessory's layer must be \"front\" or \"back\""),
        };

        return new AccessoryPack(header, slot, inFront, RequireShapes(root, PackKeys.Parts, Relative));
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

        var hasPath = element.TryGetProperty(PackKeys.ShapePath, out var path);
        var hasEllipse = element.TryGetProperty(PackKeys.ShapeEllipse, out var ellipse);
        var hasLine = element.TryGetProperty(PackKeys.ShapeLine, out var line);
        if ((hasPath ? 1 : 0) + (hasEllipse ? 1 : 0) + (hasLine ? 1 : 0) != ExactlyOneShapeKind)
        {
            throw Fail($"each shape in {where} must be exactly one of path, ellipse or line");
        }

        var fill = ReadFill(element);
        var stroke = ReadStroke(element);
        var opacity = OptionalNumber(element, PackKeys.Opacity, PackLimits.Opacity) ?? PackLimits.FullOpacity;

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
            var values = Numbers(ellipse, PackKeys.EllipseNumbers, $"an ellipse in {where}");
            if (!InBox(box, values[EllipseCenterX], values[EllipseCenterY]) || !ValidRadius(values[EllipseRadiusX]) || !ValidRadius(values[EllipseRadiusY]))
            {
                throw Fail($"an ellipse in {where} is out of range");
            }

            shape = new EllipseShape(values[EllipseCenterX], values[EllipseCenterY], values[EllipseRadiusX], values[EllipseRadiusY]);
        }
        else
        {
            var values = Numbers(line, PackKeys.LineNumbers, $"a line in {where}");
            if (!InBox(box, values[LineStartX], values[LineStartY]) || !InBox(box, values[LineEndX], values[LineEndY]))
            {
                throw Fail($"a line in {where} is out of range");
            }

            if (stroke is null)
            {
                throw Fail($"a line in {where} needs a stroke");
            }

            shape = new LineShape(values[LineStartX], values[LineStartY], values[LineEndX], values[LineEndY]);
        }

        return shape with { Fill = fill, Stroke = stroke, Opacity = opacity };
    }

    private PackFill? ReadFill(JsonElement element)
    {
        if (!element.TryGetProperty(PackKeys.Fill, out var fill))
        {
            return null;
        }

        if (fill.ValueKind == JsonValueKind.String)
        {
            return new SolidFill(ResolveColor(fill.GetString()!));
        }

        if (fill.ValueKind == JsonValueKind.Object
            && fill.TryGetProperty(PackKeys.FillGradient, out var gradient)
            && gradient.ValueKind == JsonValueKind.Array
            && gradient.GetArrayLength() == PackKeys.GradientColors
            && gradient[0].ValueKind == JsonValueKind.String
            && gradient[1].ValueKind == JsonValueKind.String)
        {
            return new GradientFill(ResolveColor(gradient[0].GetString()!), ResolveColor(gradient[1].GetString()!));
        }

        throw Fail("a fill must be a colour or a gradient of two colours");
    }

    private PackStroke? ReadStroke(JsonElement element)
    {
        if (!element.TryGetProperty(PackKeys.Stroke, out var stroke))
        {
            return null;
        }

        if (stroke.ValueKind != JsonValueKind.Object)
        {
            throw Fail("a stroke must be an object");
        }

        var color = OptionalColor(stroke, PackKeys.StrokeColor) ?? throw Fail("a stroke needs a colour");
        var width = OptionalNumber(stroke, PackKeys.StrokeWidth, PackLimits.Stroke) ?? throw Fail("a stroke needs a width");
        var cap = Text(stroke, PackKeys.StrokeCap, required: false, PackLimits.MaxKeywordLength);
        var join = Text(stroke, PackKeys.StrokeJoin, required: false, PackLimits.MaxKeywordLength);
        if (cap is not (null or PackKeys.CapRound or PackKeys.CapFlat) || join is not (null or PackKeys.JoinRound or PackKeys.JoinMiter))
        {
            throw Fail("a stroke's cap must be round or flat, and its join round or miter");
        }

        return new PackStroke(color, width, cap != PackKeys.CapFlat, join != PackKeys.JoinMiter);
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
        var values = Numbers(element, PackKeys.PointNumbers, what);
        return InBox(box, values[PointX], values[PointY]) ? new PackPoint(values[PointX], values[PointY]) : throw Fail($"{what} is out of range");
    }

    private static bool InBox(Box box, double x, double y) => x >= box.MinX && x <= box.MaxX && y >= box.MinY && y <= box.MaxY;

    private static double? OptionalNumber(JsonElement element, string name, PackRange range)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || !range.Contains(number))
        {
            throw Fail($"\"{name}\" must be a number from {range.Min.ToString(CultureInfo.InvariantCulture)} to {range.Max.ToString(CultureInfo.InvariantCulture)}");
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
        if (text.StartsWith(PackKeys.PaletteReference))
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

        byte Part(int index) => byte.Parse(text.AsSpan(HexPrefixLength + (index * HexDigitsPerPart), HexDigitsPerPart), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new PackColor(Part(RedPart), Part(GreenPart), Part(BluePart), text.Length == HexWithAlphaLength ? Part(AlphaPart) : PackColor.Opaque);
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

    private static string Shorten(string text) => PackText.Shorten(text, PackLimits.MaxQuotedValueLength);

    private static bool ValidRadius(double radius) => radius > 0 && radius <= PackLimits.MaxRadius;
}
