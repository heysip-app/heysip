using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Scheduling;

/// <summary>Edge cases around midnight, overnight windows and changed schedules.</summary>
public sealed class ScheduleEdgeTests : IDisposable
{
    // Clock starts Wednesday 11 March 2026, 00:30 UTC (machine zone UTC): the tail of Tuesday's overnight window.
    private readonly CoreHarness _core = new(new DateTimeOffset(2026, 3, 11, 0, 30, 0, TimeSpan.Zero));
    private readonly ReminderScheduler _scheduler;
    private readonly ReminderService _reminders;

    public ScheduleEdgeTests()
    {
        _scheduler = new ReminderScheduler(_core.Time);
        _reminders = new ReminderService(_core.Settings, _scheduler, new AlwaysAvailable(), _core.Time, hydration: _core.Hydration);
    }

    public void Dispose() => _core.Dispose();

    [Fact]
    public void Quiet_until_tomorrow_after_midnight_in_an_overnight_window_keeps_the_coming_afternoon()
    {
        _core.Settings.Update(WorkHoursPresets.All.Single(p => p.Id == WorkHoursPresets.NightOwlId).Apply);
        Assert.True(ActiveHours.Contains(_core.Settings.Current, new DateTime(2026, 3, 11, 0, 30, 0))); // still "Tuesday night"

        _reminders.PauseUntilTomorrow();

        // The user's "tomorrow" is the next window (today 14:00), not the one after it.
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 14, 0, 0, TimeSpan.Zero), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Next_reminder_skips_disabled_days()
    {
        // Wednesday 00:30; only Friday is on.
        _core.Settings.Update(s => s with { ActiveDays = [DayOfWeek.Friday] });

        var next = _scheduler.NextReminderAt(_core.Settings.Current);

        Assert.Equal(new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero), next);
    }

    [Theory]
    [InlineData(2026, 3, 11, 23, 0, 2026, 3, 12, 14, 0)] // evening of an overnight window: tomorrow's window is next
    [InlineData(2026, 3, 11, 13, 59, 2026, 3, 12, 14, 0)] // before the window starts: "tomorrow" is still literal
    [InlineData(2026, 3, 11, 2, 0, 2026, 3, 12, 14, 0)]   // the window has just ended (end is exclusive), so not the tail
    [InlineData(2026, 3, 11, 1, 59, 2026, 3, 11, 14, 0)]  // last minute of the tail
    public void Quiet_until_tomorrow_in_an_overnight_window_picks_the_right_start(
        int year, int month, int day, int hour, int minute, int expectedYear, int expectedMonth, int expectedDay, int expectedHour, int expectedMinute)
    {
        _core.Settings.Update(WorkHoursPresets.ById(WorkHoursPresets.NightOwlId).Apply);
        _core.Time.SetUtcNow(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

        _reminders.PauseUntilTomorrow();

        Assert.Equal(new DateTimeOffset(expectedYear, expectedMonth, expectedDay, expectedHour, expectedMinute, 0, TimeSpan.Zero), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_until_tomorrow_is_unchanged_for_an_ordinary_daytime_window()
    {
        // Default window 10:00-23:00; 00:30 is outside it and not an overnight tail.
        _reminders.PauseUntilTomorrow();

        Assert.Equal(new DateTimeOffset(2026, 3, 12, 10, 0, 0, TimeSpan.Zero), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_until_tomorrow_with_a_round_the_clock_window_does_not_treat_midnight_as_a_tail()
    {
        _core.Settings.Update(s => s with { ActiveStart = new TimeOnly(8, 0), ActiveEnd = new TimeOnly(8, 0) });

        _reminders.PauseUntilTomorrow();

        Assert.Equal(new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void The_status_says_today_tomorrow_or_the_weekday_when_the_next_nudge_is_in_the_future()
    {
        // Wednesday 00:30, outside the default 10:00-23:00 window: next nudge is today at 10:00.
        var settings = _core.Settings.Current;
        Assert.Equal("Quiet until 10:00", ReminderStatusText.Brief(_reminders.Status(), settings, _core.Time));

        // Only Thursday is on: tomorrow.
        _core.Settings.Update(s => s with { ActiveDays = [DayOfWeek.Thursday] });
        Assert.Equal("Quiet until tomorrow 10:00", ReminderStatusText.Brief(_reminders.Status(), _core.Settings.Current, _core.Time));
        Assert.Equal("Outside your active hours. Next nudge tomorrow at 10:00", ReminderStatusText.Full(_reminders.Status(), _core.Settings.Current, _core.Time));

        // Only Friday is on: named by weekday.
        _core.Settings.Update(s => s with { ActiveDays = [DayOfWeek.Friday] });
        Assert.Equal("Quiet until Friday 10:00", ReminderStatusText.Brief(_reminders.Status(), _core.Settings.Current, _core.Time));
    }

    [Fact]
    public void The_status_says_when_no_day_is_on()
    {
        _core.Settings.Update(s => s with { ActiveDays = [] });

        Assert.Equal("No reminders scheduled", ReminderStatusText.Brief(_reminders.Status(), _core.Settings.Current, _core.Time));
    }

    [Fact]
    public void The_night_owl_preset_describes_itself()
    {
        Assert.Equal("14:00 to 2:00, all week", WorkHoursPresets.ById(WorkHoursPresets.NightOwlId).Describe(new AppSettings()));
    }
}
