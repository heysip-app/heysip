using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Settings;

/// <summary>
/// What each numeric setting may be. <see cref="AppSettings.Normalize"/> clamps to these, and the settings screen shows
/// the same bounds, so a value the screen accepts is always one the file keeps.
/// </summary>
public static class SettingsLimits
{
    /// <summary>Minutes between reminders.</summary>
    public static readonly IntRange IntervalMinutes = new(min: 1, max: 720, @default: 45, step: 5);

    /// <summary>Minutes a snoozed reminder waits.</summary>
    public static readonly IntRange SnoozeMinutes = new(min: 1, max: 120, @default: 10, step: 5);

    public static readonly IntRange DailyGoalMilliliters = new(min: 250, max: 10_000, @default: 2_000, step: 250);

    /// <summary>What one tap of "drink a glass" logs.</summary>
    public static readonly IntRange GlassMilliliters = new(min: 50, max: 2_000, @default: 250, step: 50);

    /// <summary>One custom or backfilled drink.</summary>
    public static readonly IntRange DrinkMilliliters = new(min: 10, max: 2_000, @default: 300, step: 50);

    public static readonly IntRange SoundVolume = new(min: 0, max: 100, @default: 40, step: 5);

    /// <summary>1-based monitor number; 1 is the primary display.</summary>
    public static readonly IntRange Monitor = new(min: 1, max: 8, @default: 1);

    public const int NameMaxLength = 24;
}
