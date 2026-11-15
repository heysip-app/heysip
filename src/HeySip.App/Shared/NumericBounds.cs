using HeySip.Core.SharedKernel;

namespace HeySip.App.Shared;

/// <summary>
/// An <see cref="IntRange"/> in the number types the controls want (<c>NumericUpDown</c> takes decimals, <c>Slider</c> doubles),
/// so a screen binds to the same limits the settings file is clamped to instead of repeating them.
/// </summary>
public sealed class NumericBounds(IntRange range)
{
    public decimal Minimum => range.Min;

    public decimal Maximum => range.Max;

    public decimal Increment => range.Step;

    public double SliderMinimum => range.Min;

    public double SliderMaximum => range.Max;
}
