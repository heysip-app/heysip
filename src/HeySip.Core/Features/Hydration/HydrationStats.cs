using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Hydration;

public sealed record DayTotal(DateOnly Date, int Milliliters);

/// <summary>One calendar day of drinks, oldest drink first.</summary>
public sealed record DayLog(DateOnly Date, int Milliliters, IReadOnlyList<IntakeEntry> Entries, bool GoalMet);

/// <summary>One page of the drink history. <see cref="Next"/> is the day to continue from (pass it as "before"), or null when there is no more.</summary>
public sealed record HistoryPage(IReadOnlyList<DayLog> Days, DateOnly? Next);

public sealed record HydrationSummary(
    IReadOnlyList<DayTotal> Days,
    int GoalMilliliters,
    int CurrentStreak,
    int BestStreak,
    int GoalDays,
    int AverageMilliliters,
    int TotalMilliliters);

/// <summary>Pure calculations over the drink log, kept separate from storage so they are easy to test.</summary>
public static class HydrationStats
{
    /// <summary>One total per calendar day from <paramref name="first"/> to <paramref name="last"/> inclusive, zero-filled.</summary>
    public static IReadOnlyList<DayTotal> Daily(IEnumerable<IntakeEntry> entries, DateOnly first, DateOnly last, TimeZoneInfo zone)
    {
        var byDay = entries
            .GroupBy(entry => ZoneResolver.LocalDate(entry.At, zone))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Milliliters));

        var days = new List<DayTotal>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            days.Add(new DayTotal(day, byDay.GetValueOrDefault(day)));
        }

        return days;
    }

    /// <summary>
    /// Groups drinks into calendar days (in <paramref name="zone"/>), newest day first. Today is always present, even when
    /// empty; earlier days with no drinks are left out so a log list only shows days that have something in them.
    /// </summary>
    public static IReadOnlyList<DayLog> GroupByDay(IEnumerable<IntakeEntry> entries, TimeZoneInfo zone, int goal, DateOnly today)
    {
        var byDay = entries
            .GroupBy(entry => ZoneResolver.LocalDate(entry.At, zone))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<IntakeEntry>)group.OrderBy(entry => entry.At).ThenBy(entry => entry.Id).ToList());

        if (!byDay.ContainsKey(today))
        {
            byDay[today] = [];
        }

        return byDay
            .OrderByDescending(pair => pair.Key)
            .Select(pair =>
            {
                var total = pair.Value.Sum(entry => entry.Milliliters);
                return new DayLog(pair.Key, total, pair.Value, total >= goal);
            })
            .ToList();
    }

    /// <summary>
    /// Consecutive goal-met days ending today. A today that hasn't met the goal yet doesn't break the streak; the count
    /// then ends yesterday.
    /// </summary>
    public static int CurrentStreak(IReadOnlyList<DayTotal> daysAscending, int goal)
    {
        var index = daysAscending.Count - 1;
        if (index >= 0 && daysAscending[index].Milliliters < goal)
        {
            index--;
        }

        var streak = 0;
        for (; index >= 0 && daysAscending[index].Milliliters >= goal; index--)
        {
            streak++;
        }

        return streak;
    }

    public static int BestStreak(IReadOnlyList<DayTotal> daysAscending, int goal)
    {
        int best = 0, run = 0;
        foreach (var day in daysAscending)
        {
            run = day.Milliliters >= goal ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }

    public static HydrationSummary Summarize(IReadOnlyList<DayTotal> daysAscending, int goal)
    {
        var total = daysAscending.Sum(day => day.Milliliters);
        return new HydrationSummary(
            daysAscending,
            goal,
            CurrentStreak(daysAscending, goal),
            BestStreak(daysAscending, goal),
            daysAscending.Count(day => day.Milliliters >= goal),
            daysAscending.Count == 0 ? 0 : total / daysAscending.Count,
            total);
    }
}
