using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Hydration;

/// <summary>Logs drinks and answers "how am I doing today / lately". Day boundaries follow the configured time zone.</summary>
public sealed class HydrationService(IntakeStore store, SettingsService settings, TimeProvider time)
{
    private const int StreakLookbackDays = 366;

    public event EventHandler? Changed;

    /// <summary>Raised after a drink is logged (not for removals), so a reminder can treat it as answered wherever it came from.</summary>
    public event EventHandler? DrinkLogged;

    /// <summary>Raised after a single drink is removed, with what was removed, so the app can offer to put it back.</summary>
    public event EventHandler<IntakeEntry>? Removed;

    public int GoalMilliliters => settings.Current.DailyGoalMilliliters;

    public IntakeEntry Log(int milliliters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliliters);
        var entry = store.Add(time.GetUtcNow(), milliliters);
        Changed?.Invoke(this, EventArgs.Empty);
        DrinkLogged?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public IntakeEntry LogGlass() => Log(settings.Current.GlassMilliliters);

    public bool Remove(long id)
    {
        var entry = store.Find(id);
        var removed = store.Delete(id);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            if (entry is not null)
            {
                Removed?.Invoke(this, entry);
            }
        }

        return removed;
    }

    /// <summary>
    /// Puts a removed drink back at its original time. Like a backfilled drink it does not count as answering a reminder.
    /// </summary>
    public IntakeEntry Restore(IntakeEntry removed)
    {
        var entry = store.Add(removed.At, removed.Milliliters);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void ClearHistory()
    {
        store.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The last <paramref name="days"/> days of drinks, newest first. Today is always included; empty earlier days are not.</summary>
    public IReadOnlyList<DayLog> History(int days)
    {
        var zone = Zone();
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var first = today.AddDays(-(Math.Max(1, days) - 1));
        var entries = store.Between(DayBounds(first, zone).Start, DayBounds(today, zone).End);
        return HydrationStats.GroupByDay(entries, zone, GoalMilliliters, today);
    }

    /// <summary>
    /// A page of the history for browsing: up to <paramref name="days"/> days that have drinks in them, newest first. Start
    /// with <c>before: null</c> (today included), then pass the previous page's <see cref="Hydration.HistoryPage.Next"/> to go further back.
    /// Days are never split across pages.
    /// </summary>
    public HistoryPage BrowseHistory(DateOnly? before, int days = 14)
    {
        days = Math.Max(1, days);
        var zone = Zone();
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var end = before is { } day ? DayBounds(day, zone).Start : DayBounds(today, zone).End;
        var limit = days * 12;

        while (true)
        {
            var batch = store.Latest(end, limit);
            var full = batch.Count == limit;
            var grouped = batch
                .GroupBy(entry => ZoneResolver.LocalDate(entry.At, zone))
                .OrderByDescending(group => group.Key)
                .ToList();

            // A full batch may have cut its oldest day in half, so that day is held back for the next page.
            if (full)
            {
                if (grouped.Count == 1)
                {
                    limit *= 2; // one enormous day: look at more of it
                    continue;
                }

                grouped.RemoveAt(grouped.Count - 1);
            }

            var goal = GoalMilliliters;
            var logs = grouped
                .Take(days)
                .Select(group =>
                {
                    var entries = group.OrderBy(entry => entry.At).ThenBy(entry => entry.Id).ToList();
                    var total = entries.Sum(entry => entry.Milliliters);
                    return new DayLog(group.Key, total, entries, total >= goal);
                })
                .ToList();

            var more = full || grouped.Count > days;
            return new HistoryPage(logs, more && logs.Count > 0 ? logs[^1].Date : null);
        }
    }

    /// <summary>Everything logged on one calendar day (possibly nothing).</summary>
    public DayLog Day(DateOnly day)
    {
        var zone = Zone();
        var (start, end) = DayBounds(day, zone);
        var entries = store.Between(start, end).OrderBy(entry => entry.At).ThenBy(entry => entry.Id).ToList();
        var total = entries.Sum(entry => entry.Milliliters);
        return new DayLog(day, total, entries, total >= GoalMilliliters);
    }

    /// <summary>
    /// Adds a drink that was forgotten earlier, at a local time on a given day. It can't be in the future. Backfilling is
    /// housekeeping, so it does not count as answering a reminder (unlike <see cref="Log"/>).
    /// </summary>
    public IntakeEntry LogAt(DateOnly day, TimeOnly clock, int milliliters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliliters);
        var zone = Zone();
        var local = day.ToDateTime(clock);
        var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
        if (at > time.GetUtcNow())
        {
            throw new ArgumentOutOfRangeException(nameof(clock), "That time hasn't happened yet.");
        }

        var entry = store.Add(at, milliliters);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public IReadOnlyList<IntakeEntry> Today()
    {
        var zone = Zone();
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var (start, end) = DayBounds(today, zone);
        return store.Between(start, end);
    }

    public int TodayMilliliters() => Today().Sum(entry => entry.Milliliters);

    public double TodayProgress() => Math.Min(1.0, (double)TodayMilliliters() / GoalMilliliters);

    public bool GoalReachedToday() => TodayMilliliters() >= GoalMilliliters;

    /// <summary>Totals for the last <paramref name="days"/> days (today included), with streaks computed over a longer lookback.</summary>
    public HydrationSummary Summary(int days)
    {
        var zone = Zone();
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var lookback = Math.Max(days, StreakLookbackDays);
        var first = today.AddDays(-(lookback - 1));
        var entries = store.Between(DayBounds(first, zone).Start, DayBounds(today, zone).End);
        var all = HydrationStats.Daily(entries, first, today, zone);
        var goal = GoalMilliliters;

        var window = all.Skip(all.Count - days).ToList();
        var summary = HydrationStats.Summarize(window, goal);
        return summary with
        {
            CurrentStreak = HydrationStats.CurrentStreak(all, goal),
            BestStreak = HydrationStats.BestStreak(all, goal),
        };
    }

    private TimeZoneInfo Zone() => ZoneResolver.Resolve(settings.Current.TimeZoneId, time);

    private static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day, TimeZoneInfo zone)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        var nextDay = day.AddDays(1);
        var end = new DateTimeOffset(nextDay.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(nextDay.ToDateTime(TimeOnly.MinValue)));
        return (start, end);
    }
}
