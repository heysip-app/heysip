using HeySip.Core.Features.Hydration;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Hydration;

public sealed class HydrationServiceTests : IDisposable
{
    private readonly CoreHarness _core = new();

    public void Dispose() => _core.Dispose();

    [Fact]
    public void Logged_drinks_add_up_for_today_and_raise_Changed()
    {
        var raised = 0;
        _core.Hydration.Changed += (_, _) => raised++;

        _core.Hydration.Log(250);
        _core.Hydration.Log(500);

        Assert.Equal(750, _core.Hydration.TodayMilliliters());
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Drinks_survive_a_restart_because_they_are_stored_on_disk()
    {
        _core.Hydration.Log(400);

        var reopened = new HydrationService(new IntakeStore(_core.Data.Location.DatabasePath), _core.Settings, _core.Time);

        Assert.Equal(400, reopened.TodayMilliliters());
    }

    [Fact]
    public void Progress_is_capped_at_one_and_goal_detection_works()
    {
        Assert.False(_core.Hydration.GoalReachedToday());

        _core.Hydration.Log(3000);

        Assert.Equal(1.0, _core.Hydration.TodayProgress());
        Assert.True(_core.Hydration.GoalReachedToday());
    }

    [Fact]
    public void Yesterdays_drinks_do_not_count_towards_today()
    {
        _core.Hydration.Log(250);
        _core.Time.Advance(TimeSpan.FromHours(24));

        Assert.Equal(0, _core.Hydration.TodayMilliliters());
    }

    [Fact]
    public void A_drink_can_be_removed()
    {
        var entry = _core.Hydration.Log(250);

        Assert.True(_core.Hydration.Remove(entry.Id));
        Assert.Equal(0, _core.Hydration.TodayMilliliters());
        Assert.False(_core.Hydration.Remove(entry.Id));
    }

    [Fact]
    public void The_glass_size_comes_from_settings()
    {
        _core.Settings.Update(s => s with { GlassMilliliters = 330 });

        Assert.Equal(330, _core.Hydration.LogGlass().Milliliters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_non_positive_amounts(int milliliters) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.Hydration.Log(milliliters));

    [Fact]
    public void Summary_builds_a_streak_across_consecutive_goal_days()
    {
        for (var day = 0; day < 3; day++)
        {
            _core.Hydration.Log(2100);
            _core.Time.Advance(TimeSpan.FromHours(24));
        }

        _core.Hydration.Log(500); // today, goal not met yet: streak is not broken

        var summary = _core.Hydration.Summary(7);
        Assert.Equal(3, summary.CurrentStreak);
        Assert.Equal(3, summary.BestStreak);
        Assert.Equal(3, summary.GoalDays);
        Assert.Equal(7, summary.Days.Count);
    }

    [Fact]
    public void Clearing_history_removes_everything()
    {
        _core.Hydration.Log(250);

        _core.Hydration.ClearHistory();

        Assert.Equal(0, _core.Hydration.TodayMilliliters());
    }

    [Fact]
    public void Today_is_the_real_calendar_day_even_on_a_23_hour_daylight_saving_day()
    {
        var zoneId = OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York";
        // Sunday 16:00 EDT, the day clocks spring forward. (A fake clock can't be rewound, so this gets its own.)
        using var core = new CoreHarness(new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero));
        core.Settings.Update(s => s with { TimeZoneId = zoneId });

        // Distinct amounts so the sum shows exactly which drinks counted.
        core.Store.Add(new DateTimeOffset(2026, 3, 8, 4, 59, 0, TimeSpan.Zero), 1);       // 23:59 EST Saturday: not today
        core.Store.Add(new DateTimeOffset(2026, 3, 8, 5, 1, 0, TimeSpan.Zero), 10);       // 00:01 EST Sunday
        core.Store.Add(new DateTimeOffset(2026, 3, 8, 6, 59, 0, TimeSpan.Zero), 100);     // 01:59 EST Sunday
        core.Store.Add(new DateTimeOffset(2026, 3, 8, 7, 1, 0, TimeSpan.Zero), 1000);     // 03:01 EDT Sunday (after the jump)
        core.Store.Add(new DateTimeOffset(2026, 3, 9, 3, 59, 0, TimeSpan.Zero), 10000);   // 23:59 EDT Sunday
        core.Store.Add(new DateTimeOffset(2026, 3, 9, 4, 1, 0, TimeSpan.Zero), 100000);   // 00:01 EDT Monday: not today

        Assert.Equal(11110, core.Hydration.TodayMilliliters());
    }

    [Fact]
    public void History_lists_recent_days_newest_first_and_skips_empty_ones()
    {
        _core.Hydration.Log(250);                       // Mar 10
        _core.Time.Advance(TimeSpan.FromDays(3));       // Mar 13
        _core.Hydration.Log(500);

        var days = _core.Hydration.History(14);

        Assert.Equal([new DateOnly(2026, 3, 13), new DateOnly(2026, 3, 10)], days.Select(day => day.Date));
        Assert.Equal(500, days[0].Milliliters);
    }

    [Fact]
    public void History_only_reaches_back_as_far_as_asked()
    {
        _core.Hydration.Log(250);
        _core.Time.Advance(TimeSpan.FromDays(20));

        var days = _core.Hydration.History(14);

        Assert.Single(days); // just (empty) today; the old drink is out of range
        Assert.Equal(0, days[0].Milliliters);
    }

    [Fact]
    public void Day_returns_only_that_days_drinks()
    {
        _core.Hydration.Log(250);
        _core.Time.Advance(TimeSpan.FromDays(1));
        _core.Hydration.Log(500);

        var yesterday = _core.Hydration.Day(new DateOnly(2026, 3, 10));

        Assert.Equal(250, yesterday.Milliliters);
        Assert.Single(yesterday.Entries);
        Assert.Empty(_core.Hydration.Day(new DateOnly(2026, 1, 1)).Entries);
    }

    [Fact]
    public void A_forgotten_drink_can_be_added_at_a_past_time_on_an_earlier_day()
    {
        var entry = _core.Hydration.LogAt(new DateOnly(2026, 3, 9), new TimeOnly(21, 30), 400);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 21, 30, 0, TimeSpan.Zero), entry.At);
        Assert.Equal(400, _core.Hydration.Day(new DateOnly(2026, 3, 9)).Milliliters);
        Assert.Equal(0, _core.Hydration.TodayMilliliters());
    }

    [Fact]
    public void A_backfilled_drink_is_interpreted_in_the_configured_time_zone()
    {
        var zoneId = OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata"; // UTC+5:30
        _core.Settings.Update(s => s with { TimeZoneId = zoneId });

        var entry = _core.Hydration.LogAt(new DateOnly(2026, 3, 10), new TimeOnly(9, 0), 250);

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 3, 30, 0, TimeSpan.Zero), entry.At.ToUniversalTime());
    }

    [Fact]
    public void A_drink_cannot_be_added_in_the_future()
    {
        // The clock reads 12:00 on Mar 10.
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.Hydration.LogAt(new DateOnly(2026, 3, 10), new TimeOnly(12, 1), 250));
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.Hydration.LogAt(new DateOnly(2026, 3, 11), new TimeOnly(1, 0), 250));
        Assert.Equal(250, _core.Hydration.LogAt(new DateOnly(2026, 3, 10), new TimeOnly(12, 0), 250).Milliliters); // right now is fine
    }

    [Fact]
    public void Backfilling_is_not_the_same_as_answering_a_reminder()
    {
        var changed = 0;
        var logged = 0;
        _core.Hydration.Changed += (_, _) => changed++;
        _core.Hydration.DrinkLogged += (_, _) => logged++;

        _core.Hydration.LogAt(new DateOnly(2026, 3, 9), new TimeOnly(10, 0), 250);

        Assert.Equal(1, changed);
        Assert.Equal(0, logged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_backfilled_amount_must_be_positive(int milliliters) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _core.Hydration.LogAt(new DateOnly(2026, 3, 9), new TimeOnly(10, 0), milliliters));
}
