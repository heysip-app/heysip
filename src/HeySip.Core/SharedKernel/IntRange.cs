namespace HeySip.Core.SharedKernel;

/// <summary>
/// An allowed range for a whole-number setting, with the value it starts at and the step a spinner moves by. One place
/// says what is allowed, so the settings file, the screen and the tests can't drift apart.
/// </summary>
public readonly record struct IntRange
{
    public IntRange(int min, int max, int @default, int step = 1)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);
        ArgumentOutOfRangeException.ThrowIfLessThan(@default, min);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(@default, max);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        Min = min;
        Max = max;
        Default = @default;
        Step = step;
    }

    public int Min { get; }

    public int Max { get; }

    public int Default { get; }

    public int Step { get; }

    public bool Contains(int value) => value >= Min && value <= Max;

    /// <summary>The nearest allowed value.</summary>
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
}
