using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace HeySip.App.Shared.Controls;

/// <summary>A one-shot burst of falling water drops and sparkles, drawn directly so it needs no per-particle controls.</summary>
public sealed class ConfettiControl : Control
{
    private static readonly Color[] Colors =
    [
        Color.Parse("#4DB6AC"), Color.Parse("#8EDDD2"), Color.Parse("#34D399"), Color.Parse("#FBBF24"), Color.Parse("#F472B6"),
    ];

    private readonly List<Particle> _particles = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Random _random = new();
    private long _lastTick;

    public ConfettiControl()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Step();
    }

    public void Burst(int count = 46)
    {
        for (var i = 0; i < count; i++)
        {
            _particles.Add(new Particle
            {
                X = Bounds.Width * (0.15 + _random.NextDouble() * 0.7),
                Y = Bounds.Height * (0.25 + _random.NextDouble() * 0.2),
                Vx = (_random.NextDouble() - 0.5) * 260,
                Vy = -120 - _random.NextDouble() * 220,
                Rotation = _random.NextDouble() * 360,
                Spin = (_random.NextDouble() - 0.5) * 720,
                Size = 5 + _random.NextDouble() * 6,
                Drop = _random.NextDouble() < 0.45,
                Color = Colors[_random.Next(Colors.Length)],
                Life = 1.4 + _random.NextDouble() * 0.8,
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
        var dt = Math.Min(0.05, (now - _lastTick) / 1000.0);
        _lastTick = now;

        foreach (var particle in _particles)
        {
            particle.Vy += 520 * dt;
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
            var alpha = Math.Clamp(particle.Life / 0.5, 0, 1);
            var brush = new SolidColorBrush(particle.Color, alpha);
            using (context.PushTransform(Matrix.CreateRotation(particle.Rotation * Math.PI / 180) * Matrix.CreateTranslation(particle.X, particle.Y)))
            {
                if (particle.Drop)
                {
                    context.DrawEllipse(brush, null, new Point(0, 0), particle.Size * 0.5, particle.Size * 0.7);
                }
                else
                {
                    context.DrawRectangle(brush, null, new Rect(-particle.Size / 2, -particle.Size / 4, particle.Size, particle.Size / 2));
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
