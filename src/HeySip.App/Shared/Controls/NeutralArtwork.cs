using System.Globalization;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.App.Shared.Controls;

/// <summary>
/// The app's own artwork: the neutral face every character pack falls back on, the glass of water and the little effects
/// (letters, star, drop, note). It is drawn in the character's 100 by 140 design box. Face parts are placed relative to
/// the matching anchor, so (0, 0) is the anchor itself; effects are placed relative to the head-top anchor.
/// Every measurement has a name; the path strings are built from those names rather than written out.
/// </summary>
internal static class NeutralArtwork
{
    // ---- Colours
    public static readonly PackColor Ink = new(0x0F, 0x17, 0x2A);
    public static readonly PackColor Pink = new(0xFB, 0x71, 0x85);
    public static readonly PackColor Soft = new(0x94, 0xA3, 0xB8);
    public static readonly PackColor White = new(PackColor.Opaque, PackColor.Opaque, PackColor.Opaque);
    private static readonly PackColor Water = new(0x4D, 0xB6, 0xAC);
    private static readonly PackColor Gold = new(0xFD, 0xE6, 0x8A);
    private static readonly PackColor Sky = new(0x7D, 0xD3, 0xFC);

    /// <summary>How much of the glass's water shows through (out of 255), and how much of its glass.</summary>
    private const byte WaterAlpha = 217;

    private const byte GlassAlpha = 128;

    private const string ClosePath = " Z";

    // ---- Line widths
    private const double EyeLine = 3;
    private const double BoldMouthLine = 2.8;
    private const double FineMouthLine = 2.5;
    private const double BrowLine = 2.5;
    private const double GlassRimLine = 1.6;
    private const double LetterLine = 1.8;
    private const double NoteLine = 1.5;

    // ---- Eyes
    private const double PupilWidth = 4.6;
    private const double PupilHeight = 6.2;
    private const double ShineX = 1.6;
    private const double ShineY = -2.6;
    private const double ShineRadius = 1.7;
    private const double EyeArcHalfWidth = 6;
    private const double HappyEyeY = 2;
    private const double HappyEyeLift = -7;
    private const double RestEyeY = 1;
    private const double RestEyeDip = 6;
    private const double ClosedEyeHalfWidth = 5;
    private const double ClosedEyeY = 1;

    // ---- Mouths
    private const double SmileHalfWidth = 7;
    private const double SmileY = 0;
    private const double SmileDip = 8;
    private const double GrinHalfWidth = 9;
    private const double GrinY = -1;
    private const double GrinDepth = 16;
    private const double TongueY = 8.5;
    private const double TongueWidth = 4.5;
    private const double TongueHeight = 2.6;
    private const double SmallHalfWidth = 4;
    private const double SmallY = 3;
    private const double SmallDip = 5;
    private const double WorriedHalfWidth = 7;
    private const double WorriedY = 7;
    private const double WorriedLift = 0;
    private const double SleepyHalfWidth = 5;
    private const double SleepyY = 4;
    private const double SleepyDip = 1;
    private const double OY = 5;
    private const double OWidth = 3.5;
    private const double OHeight = 4.5;
    private const double SipX = 5;
    private const double SipY = 2;
    private const double SipWidth = 3;
    private const double SipHeight = 2.6;

    // ---- Brows (a line across the eye, from -half to +half)
    private const double BrowHalfWidth = 6;
    private const double WorriedBrowInner = -7;
    private const double WorriedBrowOuter = -12;
    private const double CuriousLeftBrowY = -9;
    private const double CuriousRightBrowInner = -14;
    private const double CuriousRightBrowOuter = -10;

    // ---- The glass of water (drawn at the hand-hold anchor)
    private const double GlassRimHalfWidth = 10;
    private const double GlassTopY = -18;
    private const double GlassBaseHalfWidth = 7;
    private const double GlassBaseY = 6;
    private const double WaterHalfWidth = 8.8;
    private const double WaterTopY = -9;

    // ---- Sleep letter, sparkle star, sweat drop, music note
    private const double LetterWidth = 7;
    private const double LetterHeight = 9;
    private const double StarReach = 6;
    private const double StarWaist = 1.5;
    private const double DropTop = -5;
    private const double DropHalfWidth = 4;
    private const double DropBelly = 2.5;
    private const double NoteHeadWidth = 3.2;
    private const double NoteHeadHeight = 2.3;
    private const double NoteStemX = 2.8;
    private const double NoteStemTop = -9;
    private const double NoteFlagControlX = 7;
    private const double NoteFlagControlY = -7;
    private const double NoteFlagEndX = 6;
    private const double NoteFlagEndY = -3;

    public static readonly IReadOnlyDictionary<EyeStyle, PreparedShape[]> Eyes = new Dictionary<EyeStyle, PreparedShape[]>
    {
        [EyeStyle.Open] = PreparedShape.CreateAll(
        [
            new EllipseShape(0, 0, PupilWidth, PupilHeight) { Fill = new SolidFill(Ink) },
            new EllipseShape(ShineX, ShineY, ShineRadius, ShineRadius) { Fill = new SolidFill(White) },
        ]),
        [EyeStyle.Happy] = PreparedShape.CreateAll([new PathShape(Arc(EyeArcHalfWidth, HappyEyeY, HappyEyeLift)) { Stroke = new PackStroke(Ink, EyeLine) }]),
        [EyeStyle.Rest] = PreparedShape.CreateAll([new PathShape(Arc(EyeArcHalfWidth, RestEyeY, RestEyeDip)) { Stroke = new PackStroke(Ink, EyeLine) }]),
        [EyeStyle.Closed] = PreparedShape.CreateAll([new LineShape(-ClosedEyeHalfWidth, ClosedEyeY, ClosedEyeHalfWidth, ClosedEyeY) { Stroke = new PackStroke(Ink, EyeLine) }]),
    };

    public static readonly IReadOnlyDictionary<MouthStyle, PreparedShape[]> Mouths = new Dictionary<MouthStyle, PreparedShape[]>
    {
        [MouthStyle.Smile] = PreparedShape.CreateAll([new PathShape(Arc(SmileHalfWidth, SmileY, SmileDip)) { Stroke = new PackStroke(Ink, BoldMouthLine) }]),
        [MouthStyle.Grin] = PreparedShape.CreateAll(
        [
            new PathShape(Arc(GrinHalfWidth, GrinY, GrinDepth) + ClosePath) { Fill = new SolidFill(Ink) },
            new EllipseShape(0, TongueY, TongueWidth, TongueHeight) { Fill = new SolidFill(Pink) },
        ]),
        [MouthStyle.Small] = PreparedShape.CreateAll([new PathShape(Arc(SmallHalfWidth, SmallY, SmallDip)) { Stroke = new PackStroke(Ink, FineMouthLine) }]),
        [MouthStyle.Worried] = PreparedShape.CreateAll([new PathShape(Arc(WorriedHalfWidth, WorriedY, WorriedLift)) { Stroke = new PackStroke(Ink, BoldMouthLine) }]),
        [MouthStyle.Sleepy] = PreparedShape.CreateAll([new PathShape(Arc(SleepyHalfWidth, SleepyY, SleepyDip)) { Stroke = new PackStroke(Ink, FineMouthLine) }]),
        [MouthStyle.O] = PreparedShape.CreateAll([new EllipseShape(0, OY, OWidth, OHeight) { Fill = new SolidFill(Ink) }]),
        [MouthStyle.Sip] = PreparedShape.CreateAll([new EllipseShape(SipX, SipY, SipWidth, SipHeight) { Fill = new SolidFill(Ink) }]),
    };

    public static readonly IReadOnlyDictionary<BrowStyle, (PreparedShape[] Left, PreparedShape[] Right)> Brows = new Dictionary<BrowStyle, (PreparedShape[] Left, PreparedShape[] Right)>
    {
        [BrowStyle.None] = ([], []),
        [BrowStyle.Worried] = (
            Brow(WorriedBrowInner, WorriedBrowOuter),
            Brow(WorriedBrowOuter, WorriedBrowInner)),
        [BrowStyle.Curious] = (
            Brow(CuriousLeftBrowY, CuriousLeftBrowY),
            Brow(CuriousRightBrowInner, CuriousRightBrowOuter)),
    };

    public static readonly PreparedShape[] Glass = PreparedShape.CreateAll(
    [
        new PathShape(Trapezoid(GlassRimHalfWidth, GlassTopY, GlassBaseHalfWidth, GlassBaseY)) { Fill = new SolidFill(White with { A = GlassAlpha }), Stroke = new PackStroke(White, GlassRimLine) },
        new PathShape(Trapezoid(WaterHalfWidth, WaterTopY, GlassBaseHalfWidth, GlassBaseY)) { Fill = new SolidFill(Water with { A = WaterAlpha }) },
    ]);

    public static readonly PreparedShape Letter = PreparedShape.Create(
        new PathShape(Text($"M0,0 L{LetterWidth},0 L0,{LetterHeight} L{LetterWidth},{LetterHeight}")) { Stroke = new PackStroke(Soft, LetterLine) })!;

    /// <summary>A four-pointed star: alternating long reaches and short waists around the centre.</summary>
    public static readonly PreparedShape Star = PreparedShape.Create(
        new PathShape(Text($"M0,{-StarReach} L{StarWaist},{-StarWaist} L{StarReach},0 L{StarWaist},{StarWaist} L0,{StarReach} L{-StarWaist},{StarWaist} L{-StarReach},0 L{-StarWaist},{-StarWaist} Z")) { Fill = new SolidFill(Gold) })!;

    public static readonly PreparedShape Drop = PreparedShape.Create(
        new PathShape(Text($"M0,{DropTop} C0,{DropTop} {-DropHalfWidth},0 {-DropHalfWidth},{DropBelly} A{DropHalfWidth},{DropHalfWidth} 0 0 0 {DropHalfWidth},{DropBelly} C{DropHalfWidth},0 0,{DropTop} 0,{DropTop} Z")) { Fill = new SolidFill(Sky) })!;

    public static readonly PreparedShape[] Note = PreparedShape.CreateAll(
    [
        new EllipseShape(0, 0, NoteHeadWidth, NoteHeadHeight) { Fill = new SolidFill(Soft) },
        new LineShape(NoteStemX, 0, NoteStemX, NoteStemTop) { Stroke = new PackStroke(Soft, NoteLine) },
        new PathShape(Text($"M{NoteStemX},{NoteStemTop} Q{NoteFlagControlX},{NoteFlagControlY} {NoteFlagEndX},{NoteFlagEndY}")) { Stroke = new PackStroke(Soft, NoteLine) },
    ]);

    /// <summary>A curve from (-half, y) to (+half, y) that bends towards <paramref name="bend"/> in the middle.</summary>
    private static string Arc(double halfWidth, double y, double bend) =>
        Text($"M{-halfWidth},{y} Q0,{bend} {halfWidth},{y}");

    /// <summary>A four-sided shape that is <paramref name="topHalf"/> wide either side at the top and <paramref name="baseHalf"/> at the bottom.</summary>
    private static string Trapezoid(double topHalf, double topY, double baseHalf, double baseY) =>
        Text($"M{-topHalf},{topY} L{topHalf},{topY} L{baseHalf},{baseY} L{-baseHalf},{baseY} Z");

    private static PreparedShape[] Brow(double fromY, double toY) =>
        PreparedShape.CreateAll([new LineShape(-BrowHalfWidth, fromY, BrowHalfWidth, toY) { Stroke = new PackStroke(Ink, BrowLine) }]);

    /// <summary>Numbers inside path data are always written with a point, whatever the language of the computer.</summary>
    private static string Text(FormattableString path) => path.ToString(CultureInfo.InvariantCulture);
}
