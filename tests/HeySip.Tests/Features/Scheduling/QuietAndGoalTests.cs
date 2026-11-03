using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Scheduling;

/// <summary>"Quiet until", and reminding less once the day's goal is met.</summary>
public sealed class QuietAndGoalTests : IDisposable
{
    // The harness clock is Tuesday 10 March 2026, 12:00 UTC, and the machine zone is UTC.
    private readonly CoreHarness _core = new();
    private readonly ReminderScheduler _scheduler;
    private readonly ReminderService _reminders;

    public QuietAndGoalTests()
    {
        _scheduler = new ReminderScheduler(_core.Time);
        _reminders = new ReminderService(_core.Settings, _scheduler, new AlwaysAvailable(), _core.Time, hydration: _core.Hydration);
    }

    public void Dispose() => _core.Dispose();

    private DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 3, day, hour, minute, 0, TimeSpan.Zero);

    // ---- quiet until

    [Fact]
    public void Quiet_until_a_time_later_today_pauses_until_then()
    {
        _reminders.PauseUntil(new TimeOnly(15, 0));

        Assert.Equal(At(10, 15), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_until_a_time_that_has_passed_means_tomorrow()
    {
        _reminders.PauseUntil(new TimeOnly(11, 0));

        Assert.Equal(At(11, 11), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_until_the_current_minute_means_tomorrow_not_never()
    {
        _reminders.PauseUntil(new TimeOnly(12, 0));

        Assert.Equal(At(11, 12), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_until_tomorrow_ends_when_the_active_hours_begin_again()
    {
        _core.Settings.Update(s => s with { ActiveStart = new TimeOnly(8, 30) });

        _reminders.PauseUntilTomorrow();

        Assert.Equal(At(11, 8, 30), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Quiet_time_follows_the_configured_time_zone()
    {
        _core.Settings.Update(s => s with { TimeZoneId = "Asia/Dhaka" }); // UTC+6: it is 18:00 there
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

        _reminders.PauseUntil(new TimeOnly(20, 0));

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 20, 0, 0, zone.BaseUtcOffset), _reminders.Status().PausedUntil);
    }

    [Fact]
    public void Reminders_return_by_themselves_when_the_quiet_time_is_over()
    {
        _reminders.PauseUntil(new TimeOnly(15, 0));
        _core.Time.Advance(TimeSpan.FromHours(2));
        Assert.False(_reminders.Tick()); // 14:00, still quiet

        _core.Time.Advance(TimeSpan.FromHours(1.1));

        Assert.True(_reminders.Tick());
    }

    [Fact]
    public void Resuming_early_ends_a_quiet_time()
    {
        _reminders.PauseUntilTomorrow();

        _reminders.Resume();

        Assert.False(_reminders.Status().IsPaused);
    }

    [Fact]
    public void The_status_names_the_day_when_the_quiet_time_is_not_today()
    {
        _reminders.PauseUntil(new TimeOnly(15, 0));
        Assert.Equal("Paused until 15:00", ReminderStatusText.Brief(_reminders.Status(), _core.Settings.Current, _core.Time));

        _reminders.PauseUntilTomorrow();
        Assert.Equal("Paused until tomorrow 10:00", ReminderStatusText.Brief(_reminders.Status(), _core.Settings.Current, _core.Time));
        Assert.Equal("Reminders paused until tomorrow 10:00", ReminderStatusText.Full(_reminders.Status(), _core.Settings.Current, _core.Time));
    }

    // ---- after the goal

    [Fact]
    public void Once_the_goal_is_met_the_next_reminder_is_three_times_further_away()
    {
        _reminders.Resolve(ReminderOutcome.Drank);
        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45), _scheduler.NextDueAt);

        _core.Hydration.Log(2000);
        _reminders.Resolve(ReminderOutcome.Drank); // what the service does when a drink is logged while it is running

        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45 * 3), _scheduler.NextDueAt);
    }

    [Fact]
    public void The_setting_can_switch_that_off()
    {
        _core.Settings.Update(s => s with { RemindLessAfterGoal = false });

        _core.Hydration.Log(2000);
        _reminders.Resolve(ReminderOutcome.Drank);

        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45), _scheduler.NextDueAt);
    }

    [Fact]
    public void Ignored_reminders_do_not_escalate_after_the_goal()
    {
        _core.Hydration.Log(2000);
        _reminders.Resolve(ReminderOutcome.Missed);
        _reminders.Resolve(ReminderOutcome.Missed);

        Assert.Equal(EscalationLevel.Calm, _scheduler.Level(_core.Settings.Current, goalReached: true));
        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45 * 3), _scheduler.NextDueAt);
    }

    [Fact]
    public void Before_the_goal_ignored_reminders_still_escalate()
    {
        _reminders.Resolve(ReminderOutcome.Missed);
        _reminders.Resolve(ReminderOutcome.Missed);

        Assert.Equal(EscalationLevel.Insistent, _scheduler.Level(_core.Settings.Current));
    }

    [Fact]
    public void A_reminder_that_fires_after_the_goal_schedules_the_next_one_further_out_and_is_calm()
    {
        _core.Hydration.Log(2000);
        _core.Time.Advance(TimeSpan.FromHours(3)); // 15:00, overdue
        Reminder? shown = null;
        _reminders.Due += reminder => shown = reminder;

        Assert.True(_reminders.Tick());

        Assert.Equal(EscalationLevel.Calm, shown!.Level);
        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45 * 3), _scheduler.NextDueAt);
    }

    [Fact]
    public void The_goal_of_yesterday_does_not_slow_todays_reminders()
    {
        _core.Hydration.Log(2000);
        _core.Time.Advance(TimeSpan.FromHours(24));

        _reminders.Resolve(ReminderOutcome.Drank);

        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45), _scheduler.NextDueAt);
    }
}
