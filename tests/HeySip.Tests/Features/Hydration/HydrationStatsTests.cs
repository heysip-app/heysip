using HeySip.Core.Features.Hydration;

namespace HeySip.Tests.Features.Hydration;

public sealed class HydrationStatsTests
{
    private static DayTotal Day(int offset, int ml) => new(new DateOnly(2026, 3, 1).AddDays(offset), ml);

    [Fact]
    public void Streak_counts_back_from_today_when_today_met_the_goal()
    {
        var days = new[] { Day(0, 0), Day(1, 2000), Day(2, 2500), Day(3, 2000) };

        Assert.Equal(3, HydrationStats.CurrentStreak(days, 2000));
    }

    [Fact]
    public void An_unfinished_today_does_not_break_the_streak()
    {
        var days = new[] { Day(0, 2000), Day(1, 2000), Day(2, 300) };

        Assert.Equal(2, HydrationStats.CurrentStreak(days, 2000));
    }

    [Fact]
    public void A_missed_yesterday_resets_the_streak()
    {
        var days = new[] { Day(0, 2000), Day(1, 100), Day(2, 300) };

        Assert.Equal(0, HydrationStats.CurrentStreak(days, 2000));
    }

    [Fact]
    public void Best_streak_is_the_longest_run_anywhere()
    {
        var days = new[] { Day(0, 2000), Day(1, 2000), Day(2, 2000), Day(3, 0), Day(4, 2000) };

        Assert.Equal(3, HydrationStats.BestStreak(days, 2000));
    }

    [Fact]
    public void Daily_zero_fills_days_without_drinks_and_groups_by_the_zone_day()
    {
        var entries = new[]
        {
            new IntakeEntry(1, new DateTimeOffset(2026, 3, 2, 23, 30, 0, TimeSpan.Zero), 250),
            new IntakeEntry(2, new DateTimeOffset(2026, 3, 3, 0, 30, 0, TimeSpan.Zero), 500),
        };

        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test");
        var days = HydrationStats.Daily(entries, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4), plusTwo);

        // In UTC+2 both drinks happened on March 3rd.
        Assert.Equal([0, 750, 0], days.Select(day => day.Milliliters));
    }

    [Fact]
    public void Summarize_reports_average_total_and_goal_days()
    {
        var days = new[] { Day(0, 1000), Day(1, 3000) };

        var summary = HydrationStats.Summarize(days, 2000);

        Assert.Equal(2000, summary.AverageMilliliters);
        Assert.Equal(4000, summary.TotalMilliliters);
        Assert.Equal(1, summary.GoalDays);
    }

    [Fact]
    public void Drinks_are_grouped_into_days_newest_first_with_oldest_drink_first_inside_a_day()
    {
        var utc = TimeZoneInfo.Utc;
        var entries = new[]
        {
            new IntakeEntry(1, new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero), 250),
            new IntakeEntry(3, new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero), 500),
            new IntakeEntry(2, new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), 300),
        };

        var days = HydrationStats.GroupByDay(entries, utc, goal: 2000, today: new DateOnly(2026, 3, 10));

        Assert.Equal([new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 8)], days.Select(day => day.Date));
        Assert.Equal([2L, 3L], days[0].Entries.Select(entry => entry.Id));
        Assert.Equal(800, days[0].Milliliters);
        Assert.Equal(250, days[1].Milliliters);
    }

    [Fact]
    public void Today_is_always_listed_even_when_empty_but_empty_earlier_days_are_not()
    {
        var entries = new[] { new IntakeEntry(1, new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.Zero), 250) };

        var days = HydrationStats.GroupByDay(entries, TimeZoneInfo.Utc, 2000, new DateOnly(2026, 3, 10));

        Assert.Equal([new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 7)], days.Select(day => day.Date));
        Assert.Empty(days[0].Entries);
        Assert.Equal(0, days[0].Milliliters);
    }

    [Fact]
    public void A_day_knows_whether_it_met_the_goal()
    {
        var entries = new[]
        {
            new IntakeEntry(1, new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.Zero), 2000),
            new IntakeEntry(2, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), 1999),
        };

        var days = HydrationStats.GroupByDay(entries, TimeZoneInfo.Utc, 2000, new DateOnly(2026, 3, 10));

        Assert.False(days[0].GoalMet);
        Assert.True(days[1].GoalMet);
    }
}
