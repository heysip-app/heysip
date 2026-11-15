namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The property names and fixed words of the pack.json format, in one place so the parser has no loose strings.</summary>
internal static class PackKeys
{
    // Header
    public const string Kind = "kind";
    public const string Id = "id";
    public const string Name = "name";
    public const string Version = "version";
    public const string MinAppVersion = "minAppVersion";
    public const string Requires = "requires";
    public const string Distribution = "distribution";
    public const string Tier = "tier";
    public const string StoreId = "storeId";
    public const string Author = "author";
    public const string License = "license";
    public const string Description = "description";

    // Kinds and tiers
    public const string KindCharacter = "character";
    public const string KindAccessory = "accessory";
    public const string TierFree = "free";
    public const string TierPremium = "premium";

    // Palette
    public const string Palette = "palette";
    public const string PaletteLight = "light";
    public const string PaletteDeep = "deep";
    public const string PaletteOutline = "outline";
    public const char PaletteReference = '$';

    /// <summary>The colours every character palette must define.</summary>
    public static readonly string[] RequiredPaletteColors = [PaletteLight, PaletteDeep, PaletteOutline];

    // Character
    public const string Anchors = "anchors";
    public const string Body = "body";
    public const string Feet = "feet";
    public const string Motion = "motion";
    public const string Arms = "arms";
    public const string Poses = "poses";
    public const string Face = "face";
    public const string Sounds = "sounds";

    // Motion
    public const string Bob = "bob";
    public const string BobSpeed = "bobSpeed";
    public const string Squash = "squash";
    public const string BlinkEveryMs = "blinkEveryMs";
    public const string BlinkMs = "blinkMs";

    // Arms
    public const string Fill = "fill";
    public const string Outline = "outline";
    public const string Width = "width";
    public const string OutlineWidth = "outlineWidth";

    // Poses
    public const string Left = "left";
    public const string Right = "right";
    public const string Sway = "sway";
    public const string SwayArm = "arm";
    public const string SwayAmplitude = "amplitude";
    public const string SwaySpeed = "speed";
    public const string Effects = "effects";
    public const string Eyes = "eyes";
    public const string Mouth = "mouth";
    public const string Brows = "brows";

    // Face
    public const string FaceEyes = "eyes";
    public const string FaceMouths = "mouths";
    public const string FaceBrows = "brows";
    public const string EyesMirrored = "eyesMirrored";
    public const string RightEyeSuffix = "Right";

    // Sounds
    public const string SoundReminder = "reminder";
    public const string SoundDrank = "drank";

    // Accessory
    public const string Slot = "slot";
    public const string Layer = "layer";
    public const string LayerFront = "front";
    public const string LayerBack = "back";
    public const string Parts = "parts";

    // Shapes
    public const string ShapePath = "path";
    public const string ShapeEllipse = "ellipse";
    public const string ShapeLine = "line";
    public const string Opacity = "opacity";
    public const string Stroke = "stroke";
    public const string StrokeColor = "color";
    public const string StrokeWidth = "width";
    public const string StrokeCap = "cap";
    public const string StrokeJoin = "join";
    public const string CapRound = "round";
    public const string CapFlat = "flat";
    public const string JoinRound = "round";
    public const string JoinMiter = "miter";
    public const string FillGradient = "gradient";

    /// <summary>A gradient has exactly this many colours.</summary>
    public const int GradientColors = 2;

    /// <summary>The numbers a point, an ellipse and a line are written with.</summary>
    public const int PointNumbers = 2;

    public const int EllipseNumbers = 4;
    public const int LineNumbers = 4;
}
