using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace HeySip.App.Shared.Controls;

/// <summary>A one-shot burst of falling water drops and sparkles, drawn directly so it needs no per-particle controls.</summary>
public sealed class ConfettiControl : Control
{
    // Water-drop teals and mint, with a little sunshine and pink.
    // A sparkle is a rectangle: as wide as the particle's size and half as tall, centred on the particle.
    private const double SparkleHalfWidthRatio = 0.5;
    private const double SparkleHalfHeightRatio = 0.25;
    private const double SparkleHeightRatio = 0.5;

    private static readonly Color Teal = Color.FromRgb(0x4D, 0xB6, 0xAC);
    private static readonly Color Aqua = Color.FromRgb(0x8E, 0xDD, 0xD2);
    private static readonly Color Mint = Color.FromRgb(0x34, 0xD3, 0x99);
    private static readonly Color Sun = Color.FromRgb(0xFB, 0xBF, 0x24);
    private static readonly Color Rose = Color.FromRgb(0xF4, 0x72, 0xB6);

    private static readonly Color[] Colors = [Teal, Aqua, Mint, Sun, Rose];

    /// <summary>About 60 frames a second.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private const int DefaultParticleCount = 46;

    // Where a burst starts, as a fraction of the control: a band across the middle, a little above halfway.
    private const double StartLeft = 0.15;
    private const double StartWidth = 0.7;
    private const double StartTop = 0.25;
    private const double StartHeight = 0.2;

    // Motion, in pixels per second (and degrees per second for spin).
    private const double SidewaysSpeed = 260;
    private const double MinUpwardSpeed = 120;
    private const double UpwardSpeedSpread = 220;
    private const double Gravity = 520;
    private const double SpinSpeed = 720;
    private const double FullTurnDegrees = 360;
    private const double RadiansPerDegree = Math.PI / 180;

    private const double MinSize = 5;
    private const double SizeSpread = 6;
    private const double DropChance = 0.45;
    private const double DropWidthRatio = 0.5;
    private const double DropHeightRatio = 0.7;

    /// <summary>Seconds a particle lives: at least this, plus up to the spread.</summary>
    private const double MinLifeSeconds = 1.4;
    private const double LifeSpreadSeconds = 0.8;

    /// <summary>A particle fades out over its last half second.</summary>
    private const double FadeSeconds = 0.5;

    /// <summary>The longest single step, so a stalled UI doesn't make particles jump across the screen.</summary>
    private const double MaxStepSeconds = 0.05;

    private const double MillisecondsPerSecond = 1000.0;
    private const double HalfCentered = 0.5;

    private readonly List<Particle> _particles = [];
    private readonly DispatcherTimer _timer = new() { Interval = FrameInterval };
    private readonly Random _random = new();
    private long _lastTick;

    public ConfettiControl()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Step();
    }

    public void Burst(int count = DefaultParticleCount)
    {
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new Particle
            {
                X = Bounds.Width * (StartLeft + _random.NextDouble() * StartWidth),
                Y = Bounds.Height * (StartTop + _random.NextDouble() * StartHeight),
                Vx = (_random.NextDouble() - HalfCentered) * SidewaysSpeed,
                Vy = -MinUpwardSpeed - _random.NextDouble() * UpwardSpeedSpread,
                Rotation = _random.NextDouble() * FullTurnDegrees,
                Spin = (_random.NextDouble() - HalfCentered) * SpinSpeed,
                Size = MinSize + _random.NextDouble() * SizeSpread,
                Drop = _random.NextDouble() < DropChance,
                Color = Colors[_random.Next(Colors.Length)],
                Life = MinLifeSeconds + _random.NextDouble() * LifeSpreadSeconds,
            });
        }

        if (!_timer.IsEnabled)
        {
            _lastTick = Environment.TickCount64;
            _timer.Start();
        }
    }

    private void Step()
    {
        var now = Environment.TickCount64;
        var dt = Math.Min(MaxStepSeconds, (now - _lastTick) / MillisecondsPerSecond);
        _lastTick = now;

        foreach (var particle in _particles)
        {
            particle.Vy += Gravity * dt;
            particle.X += particle.Vx * dt;
            particle.Y += particle.Vy * dt;
            particle.Rotation += particle.Spin * dt;
            particle.Life -= dt;
        }

        _particles.RemoveAll(particle => particle.Life <= 0);
        if (_particles.Count == 0)
        {
            _timer.Stop();
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        foreach (var particle in _particles)
        {
            var alpha = Math.Clamp(particle.Life / FadeSeconds, 0, 1);
            var brush = new SolidColorBrush(particle.Color, alpha);
            using (context.PushTransform(Matrix.CreateRotation(particle.Rotation * RadiansPerDegree) * Matrix.CreateTranslation(particle.X, particle.Y)))
            {
                if (particle.Drop)
                {
                    context.DrawEllipse(brush, null, new Point(0, 0), particle.Size * DropWidthRatio, particle.Size * DropHeightRatio);
                }
                else
                {
                    context.DrawRectangle(brush, null, new Rect(-particle.Size * SparkleHalfWidthRatio, -particle.Size * SparkleHalfHeightRatio, particle.Size, particle.Size * SparkleHeightRatio));
                }
            }
        }
    }

    private sealed class Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Rotation;
        public double Spin;
        public double Size;
        public bool Drop;
        public Color Color;
        public double Life;
    }
}
