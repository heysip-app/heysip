using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Scheduling;

/// <summary>Any logged drink answers the reminder, wherever it was logged.</summary>
public sealed class ReminderAnsweringTests : IDisposable
{
    private readonly CoreHarness _core = new();
    private readonly ReminderScheduler _scheduler;
    private readonly ReminderService _service;

    public ReminderAnsweringTests()
    {
        _scheduler = new ReminderScheduler(_core.Time);
        _service = new ReminderService(_core.Settings, _scheduler, new AlwaysAvailable(), _core.Time, log: null, hydration: _core.Hydration);
        _service.Start();
    }

    public void Dispose()
    {
        _service.StopAsync().GetAwaiter().GetResult();
        _core.Dispose();
    }

    [Fact]
    public void Logging_a_drink_anywhere_resets_the_countdown_and_the_escalation()
    {
        _service.Resolve(ReminderOutcome.Missed);
        Assert.Equal(EscalationLevel.Nudge, _scheduler.Level(_core.Settings.Current));

        _core.Hydration.Log(500); // e.g. from the main window or the tray

        Assert.Equal(EscalationLevel.Calm, _scheduler.Level(_core.Settings.Current));
        Assert.Equal(_core.Time.GetUtcNow().AddMinutes(45), _scheduler.NextDueAt);
    }

    [Fact]
    public void Removing_a_drink_does_not_count_as_answering()
    {
        var entry = _core.Hydration.Log(250);
        _service.Resolve(ReminderOutcome.Missed);

        _core.Hydration.Remove(entry.Id);

        Assert.Equal(EscalationLevel.Nudge, _scheduler.Level(_core.Settings.Current));
    }

    [Fact]
    public void A_drink_logged_elsewhere_is_treated_like_answering_with_the_button()
    {
        _service.Resolve(ReminderOutcome.Missed);

        _service.Resolve(ReminderOutcome.DrankElsewhere);

        Assert.Equal(EscalationLevel.Calm, _scheduler.Level(_core.Settings.Current));
    }

    [Fact]
    public void Status_changes_are_announced_so_the_status_bar_and_tray_stay_current()
    {
        var announced = 0;
        _service.StatusChanged += () => announced++;

        _service.Pause(TimeSpan.FromHours(1));
        _service.Resume();
        _service.Resolve(ReminderOutcome.Snoozed);
        _service.Restart();

        Assert.Equal(4, announced);
    }

    [Fact]
    public void The_status_says_when_the_next_reminder_really_is_even_outside_active_hours()
    {
        _core.Time.Advance(TimeSpan.FromHours(14)); // 02:00 the next day, outside 10:00-23:00

        var status = _service.Status();

        Assert.True(status.OutsideActiveHours);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero).Date, status.NextAt!.Value.Date);
        Assert.Equal(10, status.NextAt.Value.Hour);
    }
}
