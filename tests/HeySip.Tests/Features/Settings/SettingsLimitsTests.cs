using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

/// <summary>The shared limits, weekday sets and preset descriptions that replaced numbers and text written out in several places.</summary>
public sealed class SettingsLimitsTests
{
    [Fact]
    public void A_range_clamps_to_its_bounds()
    {
        var range = new IntRange(min: 5, max: 20, @default: 10, step: 5);

        Assert.Equal(5, range.Clamp(-3));
        Assert.Equal(20, range.Clamp(999));
        Assert.Equal(12, range.Clamp(12));
        Assert.True(range.Contains(5));
        Assert.False(range.Contains(21));
    }

    [Theory]
    [InlineData(10, 5, 7, 1)]   // min above max
    [InlineData(1, 5, 9, 1)]    // default above max
    [InlineData(5, 9, 1, 1)]    // default below min
    [InlineData(1, 5, 3, 0)]    // no step
    public void A_range_that_makes_no_sense_is_refused(int min, int max, int @default, int step) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntRange(min, max, @default, step));

    [Fact]
    public void Every_default_is_inside_its_own_limits_and_survives_normalizing()
    {
        var normalized = new AppSettings().Normalize();

        Assert.Equal(SettingsLimits.IntervalMinutes.Default, normalized.IntervalMinutes);
        Assert.Equal(SettingsLimits.SnoozeMinutes.Default, normalized.SnoozeMinutes);
        Assert.Equal(SettingsLimits.DailyGoalMilliliters.Default, normalized.DailyGoalMilliliters);
        Assert.Equal(SettingsLimits.GlassMilliliters.Default, normalized.GlassMilliliters);
        Assert.Equal(SettingsLimits.SoundVolume.Default, normalized.SoundVolume);
        Assert.Equal(SettingsLimits.Monitor.Default, normalized.Monitor);
    }

    [Fact]
    public void Normalizing_pulls_wild_values_to_the_limits()
    {
        var wild = new AppSettings { IntervalMinutes = int.MaxValue, SnoozeMinutes = -5, DailyGoalMilliliters = 1, GlassMilliliters = 99_999, SoundVolume = 500, Monitor = 0 }.Normalize();

        Assert.Equal(SettingsLimits.IntervalMinutes.Max, wild.IntervalMinutes);
        Assert.Equal(SettingsLimits.SnoozeMinutes.Min, wild.SnoozeMinutes);
        Assert.Equal(SettingsLimits.DailyGoalMilliliters.Min, wild.DailyGoalMilliliters);
        Assert.Equal(SettingsLimits.GlassMilliliters.Max, wild.GlassMilliliters);
        Assert.Equal(SettingsLimits.SoundVolume.Max, wild.SoundVolume);
        Assert.Equal(SettingsLimits.Monitor.Min, wild.Monitor);
    }

    [Fact]
    public void A_name_is_cut_to_the_allowed_length()
    {
        var name = new string('x', SettingsLimits.NameMaxLength + 10);

        Assert.Equal(SettingsLimits.NameMaxLength, new AppSettings { Name = name }.Normalize().Name.Length);
    }

    [Fact]
    public void Monday_comes_first_and_the_week_has_seven_days()
    {
        Assert.Equal(7, WeekDays.DaysInWeek);
        Assert.Equal(DayOfWeek.Monday, WeekDays.MondayFirst[0]);
        Assert.Equal(DayOfWeek.Sunday, WeekDays.MondayFirst[^1]);
        Assert.Equal(0, WeekDays.PositionInWeek(DayOfWeek.Monday));
        Assert.Equal(6, WeekDays.PositionInWeek(DayOfWeek.Sunday));
    }

    [Fact]
    public void Settings_do_not_share_one_days_array()
    {
        var first = new AppSettings();
        var second = new AppSettings();

        Assert.NotSame(first.ActiveDays, second.ActiveDays);
        Assert.NotSame(first.WeekendDays, second.WeekendDays);
    }

    [Fact]
    public void Every_preset_describes_its_own_hours_and_days()
    {
        var settings = new AppSettings();

        Assert.Equal("10:00 to 23:00, all week", WorkHoursPresets.ById(WorkHoursPresets.EverydayId).Describe(settings));
        Assert.Equal("9:00 to 18:00, Monday to Friday", WorkHoursPresets.ById(WorkHoursPresets.OfficeId).Describe(settings));
        Assert.Equal("6:00 to 20:00, all week", WorkHoursPresets.ById(WorkHoursPresets.EarlyBirdId).Describe(settings));
    }

    [Fact]
    public void Applying_a_preset_matches_it_and_no_other()
    {
        foreach (var preset in WorkHoursPresets.All)
        {
            var applied = preset.Apply(new AppSettings());

            Assert.Equal(preset.Id, WorkHoursPresets.Matching(applied)?.Id);
        }
    }

    [Fact]
    public void Preset_ids_are_unique()
    {
        Assert.Equal(WorkHoursPresets.All.Count, WorkHoursPresets.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Looking_up_a_preset_that_does_not_exist_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => WorkHoursPresets.ById("nope"));

    [Fact]
    public void The_pause_options_are_in_increasing_order_and_include_the_quick_one()
    {
        var durations = PauseOptions.Timed.Select(option => option.Duration).ToList();

        Assert.Equal(durations.Order().ToList(), durations);
        Assert.Contains(PauseOptions.Quick, PauseOptions.Timed);
        Assert.All(durations, duration => Assert.True(duration > TimeSpan.Zero));
    }

    [Fact]
    public void A_pause_must_last_some_time()
    {
        using var core = new CoreHarness();
        var scheduler = new ReminderScheduler(core.Time);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.PauseFor(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.PauseFor(TimeSpan.FromMinutes(-5)));
    }

    [Theory]
    [InlineData(0, 2000, 0)]
    [InlineData(1000, 2000, 50)]
    [InlineData(5000, 2000, 100)]
    [InlineData(500, 0, 100)] // no goal never divides by zero
    public void Progress_is_a_percentage_capped_at_one_hundred(int milliliters, int goal, double expected) =>
        Assert.Equal(expected, HeySip.Core.Features.Hydration.HydrationStats.Percent(milliliters, goal));
}
