namespace HeySip.Core.Features.Companion.Packs;

/// <summary>A pose with every choice made: what the face does, where the hands go, and which effects show.</summary>
public sealed record ResolvedPose(
    EyeStyle Eyes,
    MouthStyle Mouth,
    BrowStyle Brows,
    PackPoint LeftHand,
    PackPoint RightHand,
    ArmSway? Sway,
    IReadOnlyList<PoseEffect> Effects,
    double Bounce,
    double Shake);

/// <summary>
/// Works out how a character looks in a pose. Every character gets the standard version of every pose, computed from its
/// anchors, so even a minimal pack can wave, sleep and cheer; a pack's own overrides then replace any part of it.
/// </summary>
public static class PoseResolver
{
    private static readonly IReadOnlyList<PoseEffect> NoEffects = [];

    // How far a hand sits from its shoulder in each pose, in design units (x grows to the right, y downwards).
    // The left hand is given as written; the right hand mirrors it unless a pose says otherwise.
    private static readonly PackPoint RestHand = new(8, 14);
    private static readonly PackPoint HappyHand = new(11, -28);
    private static readonly PackPoint CheerHand = new(11, -34);
    private static readonly PackPoint StretchHand = new(6, -44);
    private static readonly PackPoint WorriedHand = new(10, 16);
    private static readonly PackPoint ShiverHand = new(20, 12);
    private static readonly PackPoint WaveRaisedHand = new(10, -30);

    /// <summary>The hand that holds the chin when thinking sits this far from the mouth anchor.</summary>
    private static readonly PackPoint ThinkingHandFromMouth = new(12, 6);

    /// <summary>The waving arm swings by this much, this fast (see <see cref="ArmSway"/>).</summary>
    private const double WaveSwayAmplitude = 7;

    private const double WaveSwaySpeed = 7;
    private const double CheerBounce = 5;
    private const double ShiverShake = 1.2;

    /// <summary>Half of the arm's outline is added to this to find how close to the picture's edge a hand may go.</summary>
    private const double HandEdgePadding = 2.5;

    public static ResolvedPose Resolve(CharacterPack pack, StandardPose pose)
    {
        var standard = Standard(pack, pose);
        if (!pack.Poses.TryGetValue(pose, out var custom))
        {
            return standard;
        }

        return standard with
        {
            Eyes = custom.Eyes ?? standard.Eyes,
            Mouth = custom.Mouth ?? standard.Mouth,
            Brows = custom.Brows ?? standard.Brows,
            LeftHand = custom.LeftHand ?? standard.LeftHand,
            RightHand = custom.RightHand ?? standard.RightHand,
            Sway = custom.Sway ?? standard.Sway,
            Effects = custom.Effects ?? standard.Effects,
        };
    }

    /// <summary>Every standard pose, resolved once so drawing never has to work it out again.</summary>
    public static IReadOnlyDictionary<StandardPose, ResolvedPose> ResolveAll(CharacterPack pack) =>
        Enum.GetValues<StandardPose>().ToDictionary(pose => pose, pose => Resolve(pack, pose));

    private static ResolvedPose Standard(CharacterPack pack, StandardPose pose)
    {
        var left = pack.Anchors[PackAnchors.ShoulderLeft];
        var right = pack.Anchors[PackAnchors.ShoulderRight];
        var hold = pack.Anchors[PackAnchors.HandHold];
        var mouth = pack.Anchors[PackAnchors.Mouth];

        // The outline of an arm is wider than the arm itself; keep it, as well as the hand, inside the picture.
        var reach = (pack.Arms.OutlineWidth / 2) + HandEdgePadding;
        PackPoint Inside(double x, double y) => new(Math.Clamp(x, reach, PackLimits.DesignWidth - reach), Math.Max(y, reach));
        PackPoint L(PackPoint offset) => Inside(left.X + offset.X, left.Y + offset.Y);
        PackPoint R(PackPoint offset) => Inside(right.X + offset.X, right.Y + offset.Y);
        PackPoint Mirror(PackPoint offset) => new(-offset.X, offset.Y);

        ResolvedPose Make(
            EyeStyle eyes,
            MouthStyle mouthStyle,
            BrowStyle brows,
            PackPoint leftHand,
            PackPoint rightHand,
            ArmSway? sway = null,
            IReadOnlyList<PoseEffect>? effects = null,
            double bounce = 0,
            double shake = 0) =>
            new(eyes, mouthStyle, brows, leftHand, rightHand, sway, effects ?? NoEffects, bounce, shake);

        return pose switch
        {
            StandardPose.Waving => Make(EyeStyle.Open, MouthStyle.Smile, BrowStyle.None, L(Mirror(RestHand)), R(WaveRaisedHand), new ArmSway(true, WaveSwayAmplitude, WaveSwaySpeed)),
            StandardPose.Worried => Make(EyeStyle.Open, MouthStyle.Worried, BrowStyle.Worried, L(WorriedHand), R(Mirror(WorriedHand))),
            StandardPose.Drinking => Make(EyeStyle.Rest, MouthStyle.Sip, BrowStyle.None, L(Mirror(RestHand)), hold, effects: [PoseEffect.Glass]),
            StandardPose.Happy => Make(EyeStyle.Happy, MouthStyle.Grin, BrowStyle.None, L(Mirror(HappyHand)), R(HappyHand)),
            StandardPose.Sleepy => Make(EyeStyle.Rest, MouthStyle.Sleepy, BrowStyle.None, L(Mirror(RestHand)), R(RestHand)),
            StandardPose.Sleeping => Make(EyeStyle.Closed, MouthStyle.Small, BrowStyle.None, L(Mirror(RestHand)), R(RestHand), effects: [PoseEffect.Zzz]),
            StandardPose.Cheering => Make(EyeStyle.Happy, MouthStyle.Grin, BrowStyle.None, L(Mirror(CheerHand)), R(CheerHand), effects: [PoseEffect.Sparkle], bounce: CheerBounce),
            StandardPose.Thinking => Make(EyeStyle.Open, MouthStyle.Small, BrowStyle.Curious, L(Mirror(RestHand)), new PackPoint(mouth.X + ThinkingHandFromMouth.X, mouth.Y + ThinkingHandFromMouth.Y)),
            StandardPose.Stretching => Make(EyeStyle.Rest, MouthStyle.O, BrowStyle.None, L(Mirror(StretchHand)), R(StretchHand)),
            StandardPose.Shivering => Make(EyeStyle.Open, MouthStyle.Worried, BrowStyle.None, L(ShiverHand), R(Mirror(ShiverHand)), effects: [PoseEffect.Sweat], shake: ShiverShake),
            _ => Make(EyeStyle.Open, MouthStyle.Smile, BrowStyle.None, L(Mirror(RestHand)), R(RestHand)),
        };
    }
}
