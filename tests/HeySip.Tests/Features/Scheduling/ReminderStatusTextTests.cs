using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Features.Scheduling;

public sealed class ReminderStatusTextTests
{
    private readonly AppSettings _settings = new AppSettings { TimeZoneId = "UTC" }.Normalize();

    /// <summary>A clock set to the given moment. (A fake clock can't go backwards, so each scenario gets its own.)</summary>
    private static FakeTimeProvider At(int year, int month, int day, int hour, int minute)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    private static ReminderStatus Status(DateTimeOffset? next, bool outside = false, bool paused = false, DateTimeOffset? pausedUntil = null, TimeSpan? left = null) =>
        new(paused, pausedUntil, next, left, outside);

    [Fact]
    public void Outside_hours_the_brief_text_names_tomorrow_when_the_next_nudge_is_not_today()
    {
        var time = At(2026, 3, 10, 23, 30); // Tuesday night
        var next = new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("Quiet until tomorrow 10:00", ReminderStatusText.Brief(Status(next, outside: true), _settings, time));
        Assert.Equal("Outside your active hours. Next nudge tomorrow at 10:00", ReminderStatusText.Full(Status(next, outside: true), _settings, time));
    }

    [Fact]
    public void Outside_hours_earlier_the_same_day_just_gives_the_time()
    {
        var time = At(2026, 3, 10, 2, 0);
        var next = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("Quiet until 10:00", ReminderStatusText.Brief(Status(next, outside: true), _settings, time));
    }

    [Fact]
    public void With_weekends_off_a_nudge_days_away_is_named_by_weekday()
    {
        var time = At(2026, 3, 13, 23, 30); // Friday night
        var monday = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal("Quiet until Monday 10:00", ReminderStatusText.Brief(Status(monday, outside: true), _settings, time));
    }

    [Fact]
    public void A_countdown_inside_active_hours_is_in_minutes()
    {
        var time = At(2026, 3, 10, 12, 0);
        var status = Status(time.GetUtcNow().AddMinutes(12), left: TimeSpan.FromMinutes(12));

        Assert.Equal("Next nudge in 12 min", ReminderStatusText.Brief(status, _settings, time));
        Assert.Equal("Next nudge in about 12 min", ReminderStatusText.Full(status, _settings, time));
    }

    [Fact]
    public void Paused_states_are_described_in_the_configured_zone()
    {
        var until = new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal("Paused until 15:00", ReminderStatusText.Brief(Status(null, paused: true, pausedUntil: until), _settings, At(2026, 3, 10, 14, 0)));
        Assert.Equal("Reminders paused until 15:00", ReminderStatusText.Full(Status(null, paused: true, pausedUntil: until), _settings, At(2026, 3, 10, 14, 0)));
        Assert.Equal("Paused", ReminderStatusText.Brief(Status(null, paused: true, pausedUntil: DateTimeOffset.MaxValue), _settings, At(2026, 3, 10, 14, 0)));
    }

    [Fact]
    public void No_scheduled_reminder_is_explained()
    {
        Assert.Equal("No reminders scheduled", ReminderStatusText.Brief(Status(null), _settings, At(2026, 3, 10, 14, 0)));
        Assert.Contains("active days", ReminderStatusText.Full(Status(null), _settings, At(2026, 3, 10, 14, 0)));
    }
}
