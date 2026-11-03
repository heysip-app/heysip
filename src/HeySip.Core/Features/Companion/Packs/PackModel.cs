namespace HeySip.Core.Features.Companion.Packs;

public enum PackKind
{
    Character,
    Accessory,
}

public enum PackTier
{
    Free,
    Premium,
}

public enum AccessorySlot
{
    Hat,
    Eyes,
    Neck,
    Held,
}

public enum EyeStyle
{
    Open,
    Happy,
    Rest,
    Closed,
}

public enum MouthStyle
{
    Smile,
    Grin,
    Small,
    Worried,
    Sleepy,
    O,
    Sip,
}

public enum BrowStyle
{
    None,
    Worried,
    Curious,
}

public enum PoseEffect
{
    Zzz,
    Sparkle,
    Sweat,
    Music,
    Glass,
}

/// <summary>The poses every character has. A pack may override how one looks; it cannot add poses.</summary>
public enum StandardPose
{
    Idle,
    Waving,
    Worried,
    Drinking,
    Happy,
    Sleepy,
    Sleeping,
    Cheering,
    Thinking,
    Stretching,
    Shivering,
}

public readonly record struct PackPoint(double X, double Y);

public readonly record struct PackColor(byte R, byte G, byte B, byte A = 255);

public abstract record PackFill;

public sealed record SolidFill(PackColor Color) : PackFill;

public sealed record GradientFill(PackColor From, PackColor To) : PackFill;

public sealed record PackStroke(PackColor Color, double Width, bool RoundCap = true, bool RoundJoin = true);

/// <summary>One drawable. Coordinates are in the 100 by 140 design box (or relative to an anchor, for faces and accessories).</summary>
public abstract record PackShape
{
    public PackFill? Fill { get; init; }

    public PackStroke? Stroke { get; init; }

    public double Opacity { get; init; } = 1;
}

public sealed record PathShape(string Data) : PackShape;

public sealed record EllipseShape(double CenterX, double CenterY, double RadiusX, double RadiusY) : PackShape;

public sealed record LineShape(double X1, double Y1, double X2, double Y2) : PackShape;

public sealed record PackHeader(
    int FormatVersion,
    PackKind Kind,
    string Id,
    string Name,
    string Version,
    string Author,
    string License,
    string Description,
    string? MinAppVersion,
    PackTier Tier,
    string? StoreId);

public sealed record PackMotion(double Bob, double BobSpeed, double Squash, int BlinkEveryMs, int BlinkMs)
{
    public static PackMotion Default { get; } = new(1.5, 2.2, 0.012, 4200, 130);
}

public sealed record ArmStyle(PackColor Fill, PackColor Outline, double Width, double OutlineWidth);

public sealed record ArmSway(bool RightArm, double Amplitude, double Speed);

public sealed record PoseOverride(
    EyeStyle? Eyes,
    MouthStyle? Mouth,
    BrowStyle? Brows,
    PackPoint? LeftHand,
    PackPoint? RightHand,
    ArmSway? Sway,
    IReadOnlyList<PoseEffect>? Effects);

/// <summary>A pack's own face shapes, relative to the face anchors. Anything missing falls back to the app's neutral shapes.</summary>
public sealed record FaceShapes(
    IReadOnlyDictionary<EyeStyle, IReadOnlyList<PackShape>> Eyes,
    IReadOnlyDictionary<EyeStyle, IReadOnlyList<PackShape>> RightEyes,
    IReadOnlyDictionary<MouthStyle, IReadOnlyList<PackShape>> Mouths,
    IReadOnlyDictionary<BrowStyle, IReadOnlyList<PackShape>> Brows,
    bool EyesMirrored)
{
    public static FaceShapes None { get; } = new(
        new Dictionary<EyeStyle, IReadOnlyList<PackShape>>(),
        new Dictionary<EyeStyle, IReadOnlyList<PackShape>>(),
        new Dictionary<MouthStyle, IReadOnlyList<PackShape>>(),
        new Dictionary<BrowStyle, IReadOnlyList<PackShape>>(),
        EyesMirrored: true);
}

/// <summary>File names (inside the pack) of the optional sounds.</summary>
public sealed record PackSounds(string? Reminder, string? Drank);

public abstract record Pack(PackHeader Header);

public sealed record CharacterPack(
    PackHeader Header,
    IReadOnlyDictionary<string, PackColor> Palette,
    PackMotion Motion,
    IReadOnlyDictionary<string, PackPoint> Anchors,
    IReadOnlyList<PackShape> Body,
    IReadOnlyList<PackShape> Feet,
    ArmStyle Arms,
    IReadOnlyDictionary<StandardPose, PoseOverride> Poses,
    FaceShapes Face,
    PackSounds Sounds) : Pack(Header)
{
    /// <summary>The anchors every character must define, so accessories fit every body.</summary>
    public static IReadOnlyList<string> RequiredAnchors { get; } =
    [
        "eyeLeft", "eyeRight", "mouth", "cheekLeft", "cheekRight", "shoulderLeft", "shoulderRight",
        "headTop", "eyeLine", "neck", "handHold",
    ];
}

public sealed record AccessoryPack(PackHeader Header, AccessorySlot Slot, bool InFront, IReadOnlyList<PackShape> Parts) : Pack(Header);

/// <summary>The outcome of loading a pack: the pack and its sound files, or a short reason it was refused.</summary>
public sealed record PackLoadResult(Pack? Pack, IReadOnlyDictionary<string, byte[]> Sounds, string? Error, IReadOnlyDictionary<string, byte[]>? Files = null)
{
    public bool Ok => Pack is not null;

    /// <summary>Every file of a valid pack (pack.json, its signature if any, its sounds), exactly as they were checked.</summary>
    public IReadOnlyDictionary<string, byte[]> ValidatedFiles => Files ?? new Dictionary<string, byte[]>();

    public static PackLoadResult Failure(string error) => new(null, new Dictionary<string, byte[]>(), error);
}

public sealed record PackLoadOptions(Version AppVersion, bool BuiltIn = false);
