namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The names of the anchor points every character defines, so accessories and poses fit every body.</summary>
public static class PackAnchors
{
    public const string EyeLeft = "eyeLeft";
    public const string EyeRight = "eyeRight";
    public const string Mouth = "mouth";
    public const string CheekLeft = "cheekLeft";
    public const string CheekRight = "cheekRight";
    public const string ShoulderLeft = "shoulderLeft";
    public const string ShoulderRight = "shoulderRight";
    public const string HeadTop = "headTop";
    public const string EyeLine = "eyeLine";
    public const string Neck = "neck";
    public const string HandHold = "handHold";

    /// <summary>Every anchor a character must define.</summary>
    public static IReadOnlyList<string> Required { get; } =
    [
        EyeLeft, EyeRight, Mouth, CheekLeft, CheekRight, ShoulderLeft, ShoulderRight, HeadTop, EyeLine, Neck, HandHold,
    ];
}
