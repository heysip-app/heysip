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
                    Grow(new Rect(ellipse.CenterX - ellipse.RadiusX, ellipse.CenterY - ellipse.RadiusY, ellipse.RadiusX * 2, ellipse.RadiusY * 2), pen));
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
        using var fade = _opacity < 1 ? context.PushOpacity(_opacity) : default;
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
            StartPoint = new RelativePoint(0.25, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.75, 1, RelativeUnit.Relative),
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
            AccessorySlot.Hat => anchors["headTop"],
            AccessorySlot.Eyes => anchors["eyeLine"],
            AccessorySlot.Neck => anchors["neck"],
            _ => hand,
        };

        var width = Math.Abs(anchors["shoulderRight"].X - anchors["shoulderLeft"].X);
        var eyes = Math.Abs(anchors["eyeRight"].X - anchors["eyeLeft"].X);
        var reference = pack.Slot == AccessorySlot.Eyes ? (eyes, ReferenceEyeDistance) : (width, ReferenceWidth);
        return (anchor, reference.Item1 > 1 ? reference.Item1 / reference.Item2 : 1);
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
    private const double DesignWidth = 100;
    private const double DesignHeight = 140;

    private static readonly PackColor Ink = new(0x0F, 0x17, 0x2A);
    private static readonly PackColor Pink = new(0xFB, 0x71, 0x85);
    private static readonly PackColor Soft = new(0x94, 0xA3, 0xB8);
    private static readonly PackColor White = new(255, 255, 255);

    private static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromArgb(31, 0, 0, 0));
    private static readonly IBrush CheekBrush = new ImmutableSolidColorBrush(Color.FromArgb(115, 0xFB, 0x71, 0x85));

    // The neutral face every pack can fall back on; coordinates are relative to the matching anchor.
    private static readonly Dictionary<EyeStyle, PreparedShape[]> NeutralEyes = new()
    {
        [EyeStyle.Open] = PreparedShape.CreateAll(
        [
            new EllipseShape(0, 0, 4.6, 6.2) { Fill = new SolidFill(Ink) },
            new EllipseShape(1.6, -2.6, 1.7, 1.7) { Fill = new SolidFill(White) },
        ]),
        [EyeStyle.Happy] = PreparedShape.CreateAll([new PathShape("M-6,2 Q0,-7 6,2") { Stroke = new PackStroke(Ink, 3) }]),
        [EyeStyle.Rest] = PreparedShape.CreateAll([new PathShape("M-6,1 Q0,6 6,1") { Stroke = new PackStroke(Ink, 3) }]),
        [EyeStyle.Closed] = PreparedShape.CreateAll([new LineShape(-5, 1, 5, 1) { Stroke = new PackStroke(Ink, 3) }]),
    };

    private static readonly Dictionary<MouthStyle, PreparedShape[]> NeutralMouths = new()
    {
        [MouthStyle.Smile] = PreparedShape.CreateAll([new PathShape("M-7,0 Q0,8 7,0") { Stroke = new PackStroke(Ink, 2.8) }]),
        [MouthStyle.Grin] = PreparedShape.CreateAll(
        [
            new PathShape("M-9,-1 Q0,16 9,-1 Z") { Fill = new SolidFill(Ink) },
            new EllipseShape(0, 8.5, 4.5, 2.6) { Fill = new SolidFill(Pink) },
        ]),
        [MouthStyle.Small] = PreparedShape.CreateAll([new PathShape("M-4,3 Q0,5 4,3") { Stroke = new PackStroke(Ink, 2.5) }]),
        [MouthStyle.Worried] = PreparedShape.CreateAll([new PathShape("M-7,7 Q0,0 7,7") { Stroke = new PackStroke(Ink, 2.8) }]),
        [MouthStyle.Sleepy] = PreparedShape.CreateAll([new PathShape("M-5,4 Q0,1 5,4") { Stroke = new PackStroke(Ink, 2.5) }]),
        [MouthStyle.O] = PreparedShape.CreateAll([new EllipseShape(0, 5, 3.5, 4.5) { Fill = new SolidFill(Ink) }]),
        [MouthStyle.Sip] = PreparedShape.CreateAll([new EllipseShape(5, 2, 3, 2.6) { Fill = new SolidFill(Ink) }]),
    };

    private static readonly Dictionary<BrowStyle, (PreparedShape[] Left, PreparedShape[] Right)> NeutralBrows = new()
    {
        [BrowStyle.None] = ([], []),
        [BrowStyle.Worried] = (
            PreparedShape.CreateAll([new LineShape(-6, -7, 6, -12) { Stroke = new PackStroke(Ink, 2.5) }]),
            PreparedShape.CreateAll([new LineShape(-6, -12, 6, -7) { Stroke = new PackStroke(Ink, 2.5) }])),
        [BrowStyle.Curious] = (
            PreparedShape.CreateAll([new LineShape(-6, -9, 6, -9) { Stroke = new PackStroke(Ink, 2.5) }]),
            PreparedShape.CreateAll([new LineShape(-6, -14, 6, -10) { Stroke = new PackStroke(Ink, 2.5) }])),
    };

    private static readonly PreparedShape[] GlassShapes = PreparedShape.CreateAll(
    [
        new PathShape("M-10,-18 L10,-18 L7,6 L-7,6 Z") { Fill = new SolidFill(White with { A = 128 }), Stroke = new PackStroke(White, 1.6) },
        new PathShape("M-8.8,-9 L8.8,-9 L7,6 L-7,6 Z") { Fill = new SolidFill(new PackColor(0x4D, 0xB6, 0xAC, 217)) },
    ]);

    private static readonly PreparedShape Letter = PreparedShape.Create(
        new PathShape("M0,0 L7,0 L0,9 L7,9") { Stroke = new PackStroke(Soft, 1.8) })!;

    private static readonly PreparedShape Star = PreparedShape.Create(
        new PathShape("M0,-6 L1.5,-1.5 L6,0 L1.5,1.5 L0,6 L-1.5,1.5 L-6,0 L-1.5,-1.5 Z") { Fill = new SolidFill(new PackColor(0xFD, 0xE6, 0x8A)) })!;

    private static readonly PreparedShape Drop = PreparedShape.Create(
        new PathShape("M0,-5 C0,-5 -4,0 -4,2.5 A4,4 0 0 0 4,2.5 C4,0 0,-5 0,-5 Z") { Fill = new SolidFill(new PackColor(0x7D, 0xD3, 0xFC)) })!;

    private static readonly PreparedShape[] Note = PreparedShape.CreateAll(
    [
        new EllipseShape(0, 0, 3.2, 2.3) { Fill = new SolidFill(Soft) },
        new LineShape(2.8, 0, 2.8, -9) { Stroke = new PackStroke(Soft, 1.5) },
        new PathShape("M2.8,-9 Q7,-7 6,-3") { Stroke = new PackStroke(Soft, 1.5) },
    ]);

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

        var bob = !animated ? 0 : frame.Walking ? Math.Abs(Math.Sin(seconds * 9)) * -6 : Math.Sin(seconds * motion.BobSpeed) * -motion.Bob;
        if (animated && look.Bounce > 0)
        {
            bob -= Math.Abs(Math.Sin(seconds * 8)) * look.Bounce;
        }

        var shake = animated ? Math.Sin(seconds * 45) * look.Shake : 0;
        var squash = !animated || frame.Walking ? 1 : 1 + Math.Sin(seconds * motion.BobSpeed + 1.2) * motion.Squash;
        var blinking = animated && frame.TickMs % motion.BlinkEveryMs < motion.BlinkMs;

        var offsetX = (size.Width - area.Width * scale) / 2 - area.X * scale;
        var offsetY = size.Height - area.Bottom * scale;
        var matrix = Matrix.CreateScale(1, squash)
            * Matrix.CreateTranslation(shake, DesignHeight * (1 - squash) + bob)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(offsetX, offsetY);

        using (context.PushTransform(matrix))
        {
            context.DrawEllipse(ShadowBrush, null, new Point(50, 133), 30, 4);

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
        const double margin = 3;
        var left = area.X < 0 ? margin : 0;
        var top = area.Y < 0 ? margin : 0;
        var right = area.Right > DesignWidth ? margin : 0;
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

    // Neck first and hat last, so glasses and hats sit on top of a scarf or a held balloon.
    private static int DrawOrder(AccessorySlot slot) => slot switch
    {
        AccessorySlot.Neck => 0,
        AccessorySlot.Held => 1,
        AccessorySlot.Eyes => 2,
        _ => 3,
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
        Arm(context, Pack.Anchors["shoulderLeft"], leftHand);

        // The glass of a drinking pose would clash with whatever is already in the hand.
        if (look.Effects.Contains(PoseEffect.Glass) && !holdingSomething)
        {
            DrawAt(context, Pack.Anchors["handHold"], GlassShapes);
        }

        Arm(context, Pack.Anchors["shoulderRight"], rightHand);
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
        context.DrawEllipse(CheekBrush, null, new Point(anchors["cheekLeft"].X, anchors["cheekLeft"].Y), 5.5, 3.2);
        context.DrawEllipse(CheekBrush, null, new Point(anchors["cheekRight"].X, anchors["cheekRight"].Y), 5.5, 3.2);

        var eyes = blinking && look.Eyes == EyeStyle.Open ? EyeStyle.Closed : look.Eyes;
        var left = _eyes.GetValueOrDefault(eyes) ?? NeutralEyes[eyes];
        DrawAt(context, anchors["eyeLeft"], left);

        if (_rightEyes.TryGetValue(eyes, out var right))
        {
            DrawAt(context, anchors["eyeRight"], right);
        }
        else
        {
            // A pack's own left eye is mirrored for the right unless the pack says otherwise; the neutral eye is simply repeated.
            var mirror = Pack.Face.EyesMirrored && _eyes.ContainsKey(eyes);
            DrawAt(context, anchors["eyeRight"], left, mirror);
        }

        DrawAt(context, anchors["mouth"], _mouths.GetValueOrDefault(look.Mouth) ?? NeutralMouths[look.Mouth]);

        if (look.Brows != BrowStyle.None)
        {
            if (_brows.TryGetValue(look.Brows, out var custom))
            {
                DrawAt(context, anchors["eyeLeft"], custom);
                DrawAt(context, anchors["eyeRight"], custom, Pack.Face.EyesMirrored);
            }
            else
            {
                var (leftBrow, rightBrow) = NeutralBrows[look.Brows];
                DrawAt(context, anchors["eyeLeft"], leftBrow);
                DrawAt(context, anchors["eyeRight"], rightBrow);
            }
        }
    }

    private void DrawEffects(DrawingContext context, ResolvedPose look, bool animated, double seconds)
    {
        var top = Pack.Anchors["headTop"];

        foreach (var effect in look.Effects)
        {
            switch (effect)
            {
                case PoseEffect.Zzz:
                    // Three letters drift up and fade, each a third of a cycle behind the last (still, if not animated).
                    for (var i = 0; i < 3; i++)
                    {
                        var phase = animated ? (seconds * 0.45 + i / 3.0) % 1 : i / 3.0;
                        var size = 0.6 + i * 0.2;
                        using (context.PushOpacity(1 - phase))
                        using (context.PushTransform(Matrix.CreateScale(size, size) * Matrix.CreateTranslation(top.X + 12 + phase * 12, top.Y + 30 - phase * 24)))
                        {
                            Letter.Draw(context);
                        }
                    }

                    break;
                case PoseEffect.Sparkle:
                    var spots = new (double X, double Y)[] { (-30, 24), (32, 20), (-24, 8), (26, 6) };
                    for (var i = 0; i < spots.Length; i++)
                    {
                        var pulse = animated ? 0.9 + Math.Sin(seconds * 5 + i * 1.7) * 0.3 : 1;
                        using (context.PushTransform(Matrix.CreateScale(pulse, pulse) * Matrix.CreateTranslation(top.X + spots[i].X, top.Y + spots[i].Y)))
                        {
                            Star.Draw(context);
                        }
                    }

                    break;
                case PoseEffect.Sweat:
                    var eye = Pack.Anchors["eyeRight"];
                    var fall = animated ? Math.Abs(Math.Sin(seconds * 2)) * 3 : 0;
                    using (context.PushTransform(Matrix.CreateTranslation(eye.X + 17, eye.Y - 12 + fall)))
                    {
                        Drop.Draw(context);
                    }

                    break;
                case PoseEffect.Music:
                    var notes = new (double X, double Y)[] { (-28, 22), (28, 14) };
                    for (var i = 0; i < notes.Length; i++)
                    {
                        var drift = animated ? Math.Sin(seconds * 3 + i * 2) * 2 : 0;
                        DrawAt(context, new PackPoint(top.X + notes[i].X, top.Y + notes[i].Y + drift), Note);
                    }

                    break;
            }
        }
    }
}
