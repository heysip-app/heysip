using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Features.Scheduling;

public sealed class ReminderSchedulerTests
{
    // 12:00 UTC on a Tuesday, inside the default 10:00-23:00 window.
    private readonly FakeTimeProvider _time = FakeAt(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly AppSettings _settings = new AppSettings().Normalize();

    private static FakeTimeProvider FakeAt(DateTimeOffset start)
    {
        var time = new FakeTimeProvider(start);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    [Fact]
    public void Is_not_due_until_the_interval_has_passed()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);

        _time.Advance(TimeSpan.FromMinutes(44));
        Assert.False(scheduler.TryTakeDue(_settings, userIsBusy: false));

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(scheduler.TryTakeDue(_settings, userIsBusy: false));
    }

    [Fact]
    public void Firing_schedules_the_next_reminder_one_interval_ahead()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);
        _time.Advance(TimeSpan.FromMinutes(45));
        Assert.True(scheduler.TryTakeDue(_settings, false));

        Assert.False(scheduler.TryTakeDue(_settings, false));
        _time.Advance(TimeSpan.FromMinutes(45));
        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Stays_correct_after_a_long_sleep()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);

        _time.Advance(TimeSpan.FromHours(6)); // laptop lid closed, still inside active hours at 18:00

        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Holds_back_outside_active_hours_and_fires_when_they_start()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);
        _time.Advance(TimeSpan.FromHours(12)); // midnight
        Assert.False(scheduler.TryTakeDue(_settings, false));

        _time.Advance(TimeSpan.FromHours(10)); // 10:00 next day
        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Holds_back_on_disabled_days()
    {
        var weekdaysOnly = _settings with { ActiveDays = [DayOfWeek.Monday, DayOfWeek.Wednesday] }; // today is Tuesday
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(weekdaysOnly);
        _time.Advance(TimeSpan.FromMinutes(60));

        Assert.False(scheduler.TryTakeDue(weekdaysOnly, false));
    }

    [Fact]
    public void Holds_back_while_the_user_is_busy()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);
        _time.Advance(TimeSpan.FromMinutes(60));

        Assert.False(scheduler.TryTakeDue(_settings, userIsBusy: true));
        Assert.True(scheduler.TryTakeDue(_settings, userIsBusy: false));
    }

    [Fact]
    public void Snooze_brings_the_reminder_back_after_the_snooze_time()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Snoozed(_settings);

        _time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(scheduler.TryTakeDue(_settings, false));
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Missed_reminders_escalate_and_return_sooner()
    {
        var scheduler = new ReminderScheduler(_time);
        Assert.Equal(EscalationLevel.Calm, scheduler.Level(_settings));

        scheduler.Missed(_settings);
        Assert.Equal(EscalationLevel.Nudge, scheduler.Level(_settings));
        _time.Advance(TimeSpan.FromMinutes(15)); // interval 45 / 3 = 15
        Assert.True(scheduler.TryTakeDue(_settings, false));

        scheduler.Missed(_settings);
        Assert.Equal(EscalationLevel.Insistent, scheduler.Level(_settings));
        scheduler.Missed(_settings);
        Assert.Equal(EscalationLevel.Insistent, scheduler.Level(_settings));
    }

    [Fact]
    public void Escalation_can_be_turned_off()
    {
        var calm = _settings with { Escalate = false };
        var scheduler = new ReminderScheduler(_time);

        scheduler.Missed(calm);

        Assert.Equal(EscalationLevel.Calm, scheduler.Level(calm));
        _time.Advance(TimeSpan.FromMinutes(44));
        Assert.False(scheduler.TryTakeDue(calm, false));
    }

    [Fact]
    public void Answering_resets_escalation()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Missed(_settings);
        scheduler.Missed(_settings);

        scheduler.Acknowledged(_settings);

        Assert.Equal(EscalationLevel.Calm, scheduler.Level(_settings));
    }

    [Fact]
    public void Pausing_blocks_reminders_until_it_expires_or_is_resumed()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);
        scheduler.PauseFor(TimeSpan.FromHours(2));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.True(scheduler.IsPaused);
        Assert.False(scheduler.TryTakeDue(_settings, false));

        _time.Advance(TimeSpan.FromHours(1.5));
        Assert.False(scheduler.IsPaused);
        Assert.True(scheduler.TryTakeDue(_settings, false));

        scheduler.PauseIndefinitely();
        _time.Advance(TimeSpan.FromDays(3));
        Assert.True(scheduler.IsPaused);
        scheduler.Resume(_settings);
        Assert.False(scheduler.IsPaused);
        Assert.False(scheduler.TryTakeDue(_settings, false)); // restarted, not firing at once
        _time.Advance(TimeSpan.FromMinutes(45));
        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Active_hours_follow_the_configured_time_zone()
    {
        // 12:00 UTC is 17:30 in India (inside 10-23) but 04:00 in New York (outside).
        var india = _settings with { TimeZoneId = "India Standard Time" };
        var newYork = _settings with { TimeZoneId = "Eastern Standard Time" };
        if (!OperatingSystem.IsWindows())
        {
            india = _settings with { TimeZoneId = "Asia/Kolkata" };
            newYork = _settings with { TimeZoneId = "America/New_York" };
        }

        var inIndia = new ReminderScheduler(_time);
        inIndia.Restart(india);
        var inNewYork = new ReminderScheduler(_time);
        inNewYork.Restart(newYork);
        _time.Advance(TimeSpan.FromMinutes(60));

        Assert.True(inIndia.TryTakeDue(india, false));
        Assert.False(inNewYork.TryTakeDue(newYork, false));
    }

    [Fact]
    public void A_snooze_at_night_resumes_when_active_hours_start_not_ten_minutes_later()
    {
        var scheduler = new ReminderScheduler(_time);
        _time.Advance(TimeSpan.FromHours(14.5)); // 02:30 next day, outside 10:00-23:00
        scheduler.Snoozed(_settings);

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(scheduler.IsOutsideActiveHours(_settings));
        Assert.False(scheduler.TryTakeDue(_settings, false));

        var next = scheduler.NextReminderAt(_settings)!.Value;
        Assert.Equal(new DateTime(2026, 3, 11, 10, 0, 0), new DateTime(next.Year, next.Month, next.Day, next.Hour, next.Minute, 0));

        _time.SetUtcNow(next);
        Assert.True(scheduler.TryTakeDue(_settings, false));
    }

    [Fact]
    public void Next_reminder_is_the_due_time_when_it_falls_inside_active_hours()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(_settings);

        Assert.Equal(_time.GetUtcNow().AddMinutes(45), scheduler.NextReminderAt(_settings));
        Assert.False(scheduler.IsOutsideActiveHours(_settings));
    }

    [Fact]
    public void There_is_no_next_reminder_while_paused()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.PauseFor(TimeSpan.FromHours(1));

        Assert.Null(scheduler.NextReminderAt(_settings));
    }

    [Fact]
    public void With_no_day_enabled_nothing_is_ever_scheduled_and_nothing_fires()
    {
        var noDays = _settings with { ActiveDays = [] };
        var scheduler = new ReminderScheduler(_time);
        scheduler.Restart(noDays);
        _time.Advance(TimeSpan.FromDays(3));

        Assert.Null(scheduler.NextReminderAt(noDays));
        Assert.False(scheduler.TryTakeDue(noDays, false));
    }

    [Fact]
    public void The_first_reminder_after_a_long_quiet_period_starts_calm_again()
    {
        var scheduler = new ReminderScheduler(_time);
        scheduler.Missed(_settings);
        scheduler.Missed(_settings);
        Assert.Equal(EscalationLevel.Insistent, scheduler.Level(_settings));

        _time.Advance(TimeSpan.FromHours(22)); // 10:00 next morning; the due time has been stale for hours
        Assert.True(scheduler.TryTakeDue(_settings, false));

        Assert.Equal(EscalationLevel.Calm, scheduler.Level(_settings));
    }

    [Fact]
    public void A_short_interval_is_never_made_longer_by_escalation()
    {
        var tiny = _settings with { IntervalMinutes = 2 };
        var scheduler = new ReminderScheduler(_time);

        scheduler.Missed(tiny);

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(scheduler.TryTakeDue(tiny, false));
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(1, 30, true)]
    [InlineData(2, 0, false)]
    [InlineData(12, 0, false)]
    public void Overnight_windows_span_midnight(int hour, int minute, bool expected)
    {
        var night = _settings with { ActiveStart = new TimeOnly(22, 0), ActiveEnd = new TimeOnly(2, 0) };

        Assert.Equal(expected, ActiveHours.Contains(night, new DateTime(2026, 3, 10, hour, minute, 0)));
    }
}
