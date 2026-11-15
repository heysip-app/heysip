namespace HeySip.Core.Features.Companion.Packs;

/// <summary>A closed range of numbers a pack value is kept inside. The range is checked when it is made.</summary>
public readonly record struct PackRange
{
    public PackRange(double min, double max)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);
        Min = min;
        Max = max;
    }

    public double Min { get; }

    public double Max { get; }

    public double Clamp(double value) => Math.Clamp(value, Min, Max);

    public bool Contains(double value) => value >= Min && value <= Max;
}
