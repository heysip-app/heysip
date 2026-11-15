using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.App.Shared.Controls;

/// <summary>The moment being drawn: how long the app has been running, and whether the character is walking or still.</summary>
internal readonly record struct CharacterFrame(double Seconds, long TickMs, bool Animated, bool Walking);

/// <summary>One shape of a pack, turned into brushes, pens and geometry once so that drawing a frame creates nothing new.</summary>
internal sealed class PreparedShape
{
    private enum Kind
    {
        Path,
        Ellipse,
        Line,
    }

    private const double DiameterPerRadius = 2;
    private const double FullOpacity = 1;

    /// <summary>A gradient runs from a quarter of the way across the top to three quarters across the bottom, a little slanted.</summary>
    private const double GradientStartX = 0.25;

    private const double GradientEndX = 0.75;

    private readonly Kind _kind;
    private readonly Geometry? _geometry;
    private readonly Point _from;
    private readonly Point _to;
    private readonly double _radiusX;
    private readonly double _radiusY;
    private readonly IBrush? _fill;
    private readonly Pen? _pen;
    private readonly double _opacity;

    private PreparedShape(Kind kind, Geometry? geometry, Point from, Point to, double rx, double ry, IBrush? fill, Pen? pen, double opacity, Rect reach)
    {
        Reach = reach;
        _kind = kind;
        _geometry = geometry;
        _from = from;
        _to = to;
        _radiusX = rx;
        _radiusY = ry;
        _fill = fill;
        _pen = pen;
        _opacity = opacity;
    }

    /// <summary>The area the shape covers, stroke included, in the coordinates it is drawn in.</summary>
    public Rect Reach { get; }

    public static PreparedShape? Create(PackShape shape)
    {
        var fill = ToBrush(shape.Fill);
        var pen = shape.Stroke is { } stroke
            ? new Pen(
                new ImmutableSolidColorBrush(ToColor(stroke.Color)),
                stroke.Width,
                lineCap: stroke.RoundCap ? PenLineCap.Round : PenLineCap.Flat,
                lineJoin: stroke.RoundJoin ? PenLineJoin.Round : PenLineJoin.Miter)
            : null;

        switch (shape)
        {
            case PathShape path:
                try
                {
                    var geometry = Geometry.Parse(path.Data);
                    return new PreparedShape(Kind.Path, geometry, default, default, 0, 0, fill, pen, shape.Opacity, Grow(geometry.Bounds, pen));
                }
                catch (Exception exception) when (exception is FormatException or ArgumentException)
                {
                    return null; // the pack was already checked; a path the drawing engine dislikes is just skipped
                }

            case EllipseShape ellipse:
                return new PreparedShape(
                    Kind.Ellipse, null, new Point(ellipse.CenterX, ellipse.CenterY), default, ellipse.RadiusX, ellipse.RadiusY, fill, pen, shape.Opacity,
                    Grow(new Rect(ellipse.CenterX - ellipse.RadiusX, ellipse.CenterY - ellipse.RadiusY, ellipse.RadiusX * DiameterPerRadius, ellipse.RadiusY * DiameterPerRadius), pen));
            case LineShape line:
                return new PreparedShape(
                    Kind.Line, null, new Point(line.X1, line.Y1), new Point(line.X2, line.Y2), 0, 0, fill, pen, shape.Opacity,
                    Grow(new Rect(new Point(line.X1, line.Y1), new Point(line.X2, line.Y2)), pen));
            default:
                return null;
        }
    }

    public static PreparedShape[] CreateAll(IEnumerable<PackShape> shapes) =>
        [.. shapes.Select(Create).OfType<PreparedShape>()];

    public void Draw(DrawingContext context)
    {
        using var fade = _opacity < FullOpacity ? context.PushOpacity(_opacity) : default;
        switch (_kind)
        {
            case Kind.Path:
                context.DrawGeometry(_fill, _pen, _geometry!);
                break;
            case Kind.Ellipse:
                context.DrawEllipse(_fill, _pen, _from, _radiusX, _radiusY);
                break;
            default:
                if (_pen is not null)
                {
                    context.DrawLine(_pen, _from, _to);
                }

                break;
        }
    }

    private static Rect Grow(Rect area, Pen? pen) => pen is null ? area : area.Inflate(pen.Thickness / 2);

    private static Color ToColor(PackColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static IBrush? ToBrush(PackFill? fill) => fill switch
    {
        SolidFill solid => new ImmutableSolidColorBrush(ToColor(solid.Color)),
        GradientFill gradient => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(GradientStartX, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(GradientEndX, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(ToColor(gradient.From), 0), new GradientStop(ToColor(gradient.To), 1)],
        },
        _ => null,
    };
}

/// <summary>An accessory (hat, glasses, scarf, held item) ready to draw on any character.</summary>
internal sealed class PreparedAccessory(AccessoryPack pack)
{
    // The measurements of the original droplet. Accessories are drawn for it and scaled to fit: hats, scarves and held
    // things by shoulder width, glasses by the distance between the eyes (so they fit a wide-eyed axolotl too).
    private const double ReferenceWidth = 68;
    private const double ReferenceEyeDistance = 22;

    /// <summary>A character whose measured width is no more than this is drawn at the reference size, not scaled by it.</summary>
    private const double SmallestMeasuredWidth = 1;

    private const double ReferenceScale = 1;

    private readonly PreparedShape[] _parts = PreparedShape.CreateAll(pack.Parts);

    // Worked out the first time it is needed: how far the parts reach, before they are placed on a character.
    private Rect? _localReach;

    public bool InFront => pack.InFront;

    public AccessorySlot Slot => pack.Slot;

    /// <summary>The area the accessory covers when worn, in the character's design coordinates. Used to leave room for it.</summary>
    public Rect Reach(CharacterPack character, PackPoint hand)
    {
        var (anchor, scale) = Placement(character, hand);
        var local = _localReach ??= _parts.Select(part => part.Reach).DefaultIfEmpty().Aggregate((all, next) => all.Union(next));
        return new Rect(anchor.X + local.X * scale, anchor.Y + local.Y * scale, local.Width * scale, local.Height * scale);
    }

    /// <summary>Where the accessory attaches, and how much it is scaled to fit this character.</summary>
    private (PackPoint Anchor, double Scale) Placement(CharacterPack character, PackPoint hand)
    {
        var anchors = character.Anchors;
        var anchor = pack.Slot switch
        {
            AccessorySlot.Hat => anchors[PackAnchors.HeadTop],
            AccessorySlot.Eyes => anchors[PackAnchors.EyeLine],
            AccessorySlot.Neck => anchors[PackAnchors.Neck],
            _ => hand,
        };

        var width = Math.Abs(anchors[PackAnchors.ShoulderRight].X - anchors[PackAnchors.ShoulderLeft].X);
        var eyes = Math.Abs(anchors[PackAnchors.EyeRight].X - anchors[PackAnchors.EyeLeft].X);
        var reference = pack.Slot == AccessorySlot.Eyes ? (eyes, ReferenceEyeDistance) : (width, ReferenceWidth);
        return (anchor, reference.Item1 > SmallestMeasuredWidth ? reference.Item1 / reference.Item2 : ReferenceScale);
    }

    /// <summary>Draws the accessory on the character. A held item goes where the right hand is, so it moves with it.</summary>
    public void Draw(DrawingContext context, CharacterPack character, PackPoint hand)
    {
        var (anchor, scale) = Placement(character, hand);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(anchor.X, anchor.Y)))
        {
            foreach (var part in _parts)
            {
                part.Draw(context);
            }
        }
    }
}

/// <summary>
/// A character pack made ready to draw. Everything that never changes (shapes, pens, the neutral face every pack falls
/// back on, every standard pose) is built once, here.
/// </summary>
internal sealed class PreparedCharacter
{
    private const double DesignWidth = PackLimits.DesignWidth;
    private const double DesignHeight = PackLimits.DesignHeight;

    // The ground shadow under the feet (an ellipse), and the blush on the cheeks.
    private const double ShadowCenterX = 50;
    private const double ShadowCenterY = 133;
    private const double ShadowRadiusX = 30;
    private const double ShadowRadiusY = 4;
    private const byte ShadowAlpha = 31;
    private const byte CheekAlpha = 115;
    private const double CheekRadiusX = 5.5;
    private const double CheekRadiusY = 3.2;

    // Whole-body movement, in design units and radians per second. Walking hops faster and higher than idling.
    private const double WalkHopSpeed = 9;
    private const double WalkHopHeight = 6;
    private const double BounceSpeed = 8;
    private const double ShakeSpeed = 45;

    /// <summary>The squash follows the bob, a little behind it (in radians).</summary>
    private const double SquashPhase = 1.2;

    /// <summary>The space added where the outfit sticks out past the character's box (see <see cref="WithMargin"/>).</summary>
    private const double OutfitMargin = 3;

    // The effects drawn around the head, positioned from the head-top anchor.
    private const int ZzzLetters = 3;
    private const double ZzzCyclesPerSecond = 0.45;
    private const double ZzzFirstSize = 0.6;
    private const double ZzzSizeStep = 0.2;
    private const double ZzzStartX = 12;
    private const double ZzzStartY = 30;
    private const double ZzzDriftX = 12;
    private const double ZzzDriftY = 24;

    private const double SparklePulseBase = 0.9;
    private const double SparklePulseSize = 0.3;
    private const double SparklePulseSpeed = 5;
    private const double SparklePulseOffset = 1.7;
    private static readonly (double X, double Y)[] SparkleSpots = [(-30, 24), (32, 20), (-24, 8), (26, 6)];

    private const double SweatOffsetX = 17;
    private const double SweatOffsetY = -12;
    private const double SweatFallSpeed = 2;
    private const double SweatFallDistance = 3;

    private const double NoteDriftSpeed = 3;
    private const double NoteDriftOffset = 2;
    private const double NoteDriftDistance = 2;
    private static readonly (double X, double Y)[] NoteSpots = [(-28, 22), (28, 14)];

    /// <summary>The draw order of worn things, back to front: neck first and hat last, so glasses and hats sit on a scarf or balloon.</summary>
    private const int NeckOrder = 0;

    private const int HeldOrder = 1;
    private const int EyesOrder = 2;
    private const int HatOrder = 3;

    private static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromArgb(ShadowAlpha, 0, 0, 0));
    private static readonly IBrush CheekBrush = new ImmutableSolidColorBrush(Color.FromArgb(CheekAlpha, NeutralArtwork.Pink.R, NeutralArtwork.Pink.G, NeutralArtwork.Pink.B));

    private readonly PreparedShape[] _body;
    private readonly PreparedShape[] _feet;
    private readonly Pen _armOutline;
    private readonly Pen _armFill;
    private readonly IReadOnlyDictionary<StandardPose, ResolvedPose> _poses;
    private readonly Dictionary<EyeStyle, PreparedShape[]> _eyes = [];
    private readonly Dictionary<EyeStyle, PreparedShape[]> _rightEyes = [];
    private readonly Dictionary<MouthStyle, PreparedShape[]> _mouths = [];
    private readonly Dictionary<BrowStyle, PreparedShape[]> _brows = [];

    public PreparedCharacter(CharacterPack pack)
    {
        Pack = pack;
        _body = PreparedShape.CreateAll(pack.Body);
        _feet = PreparedShape.CreateAll(pack.Feet);
        _armOutline = new Pen(new ImmutableSolidColorBrush(Convert(pack.Arms.Outline)), pack.Arms.OutlineWidth, lineCap: PenLineCap.Round);
        _armFill = new Pen(new ImmutableSolidColorBrush(Convert(pack.Arms.Fill)), pack.Arms.Width, lineCap: PenLineCap.Round);
        _poses = PoseResolver.ResolveAll(pack);

        foreach (var (style, shapes) in pack.Face.Eyes)
        {
            _eyes[style] = PreparedShape.CreateAll(shapes);
        }

        foreach (var (style, shapes) in pack.Face.RightEyes)
        {
            _rightEyes[style] = PreparedShape.CreateAll(shapes);
        }

        foreach (var (style, shapes) in pack.Face.Mouths)
        {
            _mouths[style] = PreparedShape.CreateAll(shapes);
        }

        foreach (var (style, shapes) in pack.Face.Brows)
        {
            _brows[style] = PreparedShape.CreateAll(shapes);
        }
    }

    public CharacterPack Pack { get; }

    public ResolvedPose Look(StandardPose pose) => _poses[pose];

    /// <summary>Draws the character, with its feet on the bottom of <paramref name="size"/>, in the given pose.</summary>
    public void Draw(DrawingContext context, Size size, StandardPose pose, in CharacterFrame frame, IReadOnlyList<PreparedAccessory>? outfit)
    {
        var look = _poses[pose];

        // Leave room for what is worn (a tall hat, a balloon): the picture is the character's box plus whatever sticks out.
        var area = new Rect(0, 0, DesignWidth, DesignHeight);
        if (outfit is not null)
        {
            foreach (var item in outfit)
            {
                area = area.Union(item.Reach(Pack, look.RightHand));
            }
        }

        area = WithMargin(area);

        var scale = Math.Min(size.Width / area.Width, size.Height / area.Height);
        if (scale <= 0)
        {
            return;
        }

        var motion = Pack.Motion;
        var seconds = frame.Seconds;
        var animated = frame.Animated;

        var bob = !animated ? 0 : frame.Walking ? Math.Abs(Math.Sin(seconds * WalkHopSpeed)) * -WalkHopHeight : Math.Sin(seconds * motion.BobSpeed) * -motion.Bob;
        if (animated && look.Bounce > 0)
        {
            bob -= Math.Abs(Math.Sin(seconds * BounceSpeed)) * look.Bounce;
        }

        var shake = animated ? Math.Sin(seconds * ShakeSpeed) * look.Shake : 0;
        var squash = !animated || frame.Walking ? 1 : 1 + (Math.Sin((seconds * motion.BobSpeed) + SquashPhase) * motion.Squash);
        var blinking = animated && frame.TickMs % motion.BlinkEveryMs < motion.BlinkMs;

        var offsetX = (size.Width - area.Width * scale) / 2 - area.X * scale;
        var offsetY = size.Height - area.Bottom * scale;
        var matrix = Matrix.CreateScale(1, squash)
            * Matrix.CreateTranslation(shake, DesignHeight * (1 - squash) + bob)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(offsetX, offsetY);

        using (context.PushTransform(matrix))
        {
            context.DrawEllipse(ShadowBrush, null, new Point(ShadowCenterX, ShadowCenterY), ShadowRadiusX, ShadowRadiusY);

            var (leftHand, rightHand) = Hands(look, animated, seconds);

            DrawAccessories(context, outfit, inFront: false, rightHand);
            Draw(context, _feet);
            Draw(context, _body);
            DrawArms(context, look, leftHand, rightHand, holdingSomething: outfit?.Any(item => item.Slot == AccessorySlot.Held) == true);
            DrawFace(context, look, blinking);
            DrawEffects(context, look, animated, seconds);
            DrawAccessories(context, outfit, inFront: true, rightHand);
        }
    }

    /// <summary>
    /// Where the outfit sticks out past the character's own box, add a little space, so a tall hat or a balloon is not
    /// squeezed hard against the edge of the picture. A character that wears nothing keeps exactly its own box.
    /// </summary>
    private static Rect WithMargin(Rect area)
    {
        var left = area.X < 0 ? OutfitMargin : 0;
        var top = area.Y < 0 ? OutfitMargin : 0;
        var right = area.Right > DesignWidth ? OutfitMargin : 0;
        return new Rect(area.X - left, area.Y - top, area.Width + left + right, area.Height + top);
    }

    private static Color Convert(PackColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static void Draw(DrawingContext context, PreparedShape[] shapes)
    {
        foreach (var shape in shapes)
        {
            shape.Draw(context);
        }
    }

    private static void DrawAt(DrawingContext context, PackPoint at, PreparedShape[] shapes, bool mirrored = false)
    {
        var matrix = mirrored ? Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(at.X, at.Y) : Matrix.CreateTranslation(at.X, at.Y);
        using (context.PushTransform(matrix))
        {
            Draw(context, shapes);
        }
    }

    private static int DrawOrder(AccessorySlot slot) => slot switch
    {
        AccessorySlot.Neck => NeckOrder,
        AccessorySlot.Held => HeldOrder,
        AccessorySlot.Eyes => EyesOrder,
        _ => HatOrder,
    };

    private void DrawAccessories(DrawingContext context, IReadOnlyList<PreparedAccessory>? outfit, bool inFront, PackPoint rightHand)
    {
        if (outfit is null)
        {
            return;
        }

        foreach (var accessory in outfit.Where(item => item.InFront == inFront).OrderBy(item => DrawOrder(item.Slot)))
        {
            accessory.Draw(context, Pack, rightHand);
        }
    }

    /// <summary>Where both hands are right now, including the sway of a wave.</summary>
    private static (PackPoint Left, PackPoint Right) Hands(ResolvedPose look, bool animated, double seconds)
    {
        var left = look.LeftHand;
        var right = look.RightHand;
        if (look.Sway is { } sway && animated)
        {
            var offset = Math.Sin(seconds * sway.Speed) * sway.Amplitude;
            if (sway.RightArm)
            {
                right = new PackPoint(right.X + offset, right.Y);
            }
            else
            {
                left = new PackPoint(left.X + offset, left.Y);
            }
        }

        return (left, right);
    }

    private void DrawArms(DrawingContext context, ResolvedPose look, PackPoint leftHand, PackPoint rightHand, bool holdingSomething)
    {
        Arm(context, Pack.Anchors[PackAnchors.ShoulderLeft], leftHand);

        // The glass of a drinking pose would clash with whatever is already in the hand.
        if (look.Effects.Contains(PoseEffect.Glass) && !holdingSomething)
        {
            DrawAt(context, Pack.Anchors[PackAnchors.HandHold], NeutralArtwork.Glass);
        }

        Arm(context, Pack.Anchors[PackAnchors.ShoulderRight], rightHand);
    }

    private void Arm(DrawingContext context, PackPoint from, PackPoint to)
    {
        var start = new Point(from.X, from.Y);
        var end = new Point(to.X, to.Y);
        context.DrawLine(_armOutline, start, end);
        context.DrawLine(_armFill, start, end);
    }

    private void DrawFace(DrawingContext context, ResolvedPose look, bool blinking)
    {
        var anchors = Pack.Anchors;
        context.DrawEllipse(CheekBrush, null, new Point(anchors[PackAnchors.CheekLeft].X, anchors[PackAnchors.CheekLeft].Y), CheekRadiusX, CheekRadiusY);
        context.DrawEllipse(CheekBrush, null, new Point(anchors[PackAnchors.CheekRight].X, anchors[PackAnchors.CheekRight].Y), CheekRadiusX, CheekRadiusY);

        var eyes = blinking && look.Eyes == EyeStyle.Open ? EyeStyle.Closed : look.Eyes;
        var left = _eyes.GetValueOrDefault(eyes) ?? NeutralArtwork.Eyes[eyes];
        DrawAt(context, anchors[PackAnchors.EyeLeft], left);

        if (_rightEyes.TryGetValue(eyes, out var right))
        {
            DrawAt(context, anchors[PackAnchors.EyeRight], right);
        }
        else
        {
            // A pack's own left eye is mirrored for the right unless the pack says otherwise; the neutral eye is simply repeated.
            var mirror = Pack.Face.EyesMirrored && _eyes.ContainsKey(eyes);
            DrawAt(context, anchors[PackAnchors.EyeRight], left, mirror);
        }

        DrawAt(context, anchors[PackAnchors.Mouth], _mouths.GetValueOrDefault(look.Mouth) ?? NeutralArtwork.Mouths[look.Mouth]);

        if (look.Brows != BrowStyle.None)
        {
            if (_brows.TryGetValue(look.Brows, out var custom))
            {
                DrawAt(context, anchors[PackAnchors.EyeLeft], custom);
                DrawAt(context, anchors[PackAnchors.EyeRight], custom, Pack.Face.EyesMirrored);
            }
            else
            {
                var (leftBrow, rightBrow) = NeutralArtwork.Brows[look.Brows];
                DrawAt(context, anchors[PackAnchors.EyeLeft], leftBrow);
                DrawAt(context, anchors[PackAnchors.EyeRight], rightBrow);
            }
        }
    }

    private void DrawEffects(DrawingContext context, ResolvedPose look, bool animated, double seconds)
    {
        var top = Pack.Anchors[PackAnchors.HeadTop];

        foreach (var effect in look.Effects)
        {
            switch (effect)
            {
                case PoseEffect.Zzz:
                    // The letters drift up and fade, each an equal share of a cycle behind the last (still, if not animated).
                    for (var i = 0; i < ZzzLetters; i++)
                    {
                        var share = (double)i / ZzzLetters;
                        var phase = animated ? ((seconds * ZzzCyclesPerSecond) + share) % 1 : share;
                        var size = ZzzFirstSize + (i * ZzzSizeStep);
                        using (context.PushOpacity(1 - phase))
                        using (context.PushTransform(Matrix.CreateScale(size, size) * Matrix.CreateTranslation(top.X + ZzzStartX + (phase * ZzzDriftX), top.Y + ZzzStartY - (phase * ZzzDriftY))))
                        {
                            NeutralArtwork.Letter.Draw(context);
                        }
                    }

                    break;
                case PoseEffect.Sparkle:
                    for (var i = 0; i < SparkleSpots.Length; i++)
                    {
                        var pulse = animated ? SparklePulseBase + (Math.Sin((seconds * SparklePulseSpeed) + (i * SparklePulseOffset)) * SparklePulseSize) : 1;
                        using (context.PushTransform(Matrix.CreateScale(pulse, pulse) * Matrix.CreateTranslation(top.X + SparkleSpots[i].X, top.Y + SparkleSpots[i].Y)))
                        {
                            NeutralArtwork.Star.Draw(context);
                        }
                    }

                    break;
                case PoseEffect.Sweat:
                    var eye = Pack.Anchors[PackAnchors.EyeRight];
                    var fall = animated ? Math.Abs(Math.Sin(seconds * SweatFallSpeed)) * SweatFallDistance : 0;
                    using (context.PushTransform(Matrix.CreateTranslation(eye.X + SweatOffsetX, eye.Y + SweatOffsetY + fall)))
                    {
                        NeutralArtwork.Drop.Draw(context);
                    }

                    break;
                case PoseEffect.Music:
                    for (var i = 0; i < NoteSpots.Length; i++)
                    {
                        var drift = animated ? Math.Sin((seconds * NoteDriftSpeed) + (i * NoteDriftOffset)) * NoteDriftDistance : 0;
                        DrawAt(context, new PackPoint(top.X + NoteSpots[i].X, top.Y + NoteSpots[i].Y + drift), NeutralArtwork.Note);
                    }

                    break;
            }
        }
    }
}
