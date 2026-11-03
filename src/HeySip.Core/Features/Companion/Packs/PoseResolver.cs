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
        var left = pack.Anchors["shoulderLeft"];
        var right = pack.Anchors["shoulderRight"];
        var hold = pack.Anchors["handHold"];
        var mouth = pack.Anchors["mouth"];

        // The outline of an arm is wider than the arm itself; keep it, as well as the hand, inside the 100 by 140 picture.
        var reach = (pack.Arms.OutlineWidth / 2) + 2.5;
        PackPoint Inside(double x, double y) => new(Math.Clamp(x, reach, 100 - reach), Math.Max(y, reach));
        PackPoint L(double dx, double dy) => Inside(left.X + dx, left.Y + dy);
        PackPoint R(double dx, double dy) => Inside(right.X + dx, right.Y + dy);

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
            StandardPose.Waving => Make(EyeStyle.Open, MouthStyle.Smile, BrowStyle.None, L(-8, 14), R(10, -30), new ArmSway(true, 7, 7)),
            StandardPose.Worried => Make(EyeStyle.Open, MouthStyle.Worried, BrowStyle.Worried, L(10, 16), R(-10, 16)),
            StandardPose.Drinking => Make(EyeStyle.Rest, MouthStyle.Sip, BrowStyle.None, L(-8, 14), hold, effects: [PoseEffect.Glass]),
            StandardPose.Happy => Make(EyeStyle.Happy, MouthStyle.Grin, BrowStyle.None, L(-11, -28), R(11, -28)),
            StandardPose.Sleepy => Make(EyeStyle.Rest, MouthStyle.Sleepy, BrowStyle.None, L(-8, 14), R(8, 14)),
            StandardPose.Sleeping => Make(EyeStyle.Closed, MouthStyle.Small, BrowStyle.None, L(-8, 14), R(8, 14), effects: [PoseEffect.Zzz]),
            StandardPose.Cheering => Make(EyeStyle.Happy, MouthStyle.Grin, BrowStyle.None, L(-11, -34), R(11, -34), effects: [PoseEffect.Sparkle], bounce: 5),
            StandardPose.Thinking => Make(EyeStyle.Open, MouthStyle.Small, BrowStyle.Curious, L(-8, 14), new PackPoint(mouth.X + 12, mouth.Y + 6)),
            StandardPose.Stretching => Make(EyeStyle.Rest, MouthStyle.O, BrowStyle.None, L(-6, -44), R(6, -44)),
            StandardPose.Shivering => Make(EyeStyle.Open, MouthStyle.Worried, BrowStyle.None, L(20, 12), R(-20, 12), effects: [PoseEffect.Sweat], shake: 1.2),
            _ => Make(EyeStyle.Open, MouthStyle.Smile, BrowStyle.None, L(-8, 14), R(8, 14)),
        };
    }
}
