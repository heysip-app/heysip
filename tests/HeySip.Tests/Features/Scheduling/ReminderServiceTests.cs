using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Scheduling;

public sealed class ReminderServiceTests : IDisposable
{
    private sealed class FakePresence : IPresenceProbe
    {
        public bool Away { get; set; }

        public bool IsAwayOrBusy() => Away;
    }

    private readonly CoreHarness _core = new();
    private readonly FakePresence _presence = new();
    private readonly ReminderService _service;
    private readonly List<Reminder> _raised = [];

    public ReminderServiceTests()
    {
        _service = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), _presence, _core.Time);
        _service.Due += _raised.Add;
        _service.Restart();
    }

    public void Dispose() => _core.Dispose();

    [Fact]
    public void Raises_Due_once_the_interval_passes()
    {
        _core.Time.Advance(TimeSpan.FromMinutes(46));

        Assert.True(_service.Tick());
        Assert.Single(_raised);
        Assert.False(_raised[0].Manual);
    }

    [Fact]
    public void Does_not_stack_reminders_while_one_is_visible()
    {
        _service.ReminderVisible = true;
        _core.Time.Advance(TimeSpan.FromMinutes(46));

        Assert.False(_service.Tick());
    }

    [Fact]
    public void Respects_the_pause_when_away_setting()
    {
        _presence.Away = true;
        _core.Time.Advance(TimeSpan.FromMinutes(46));
        Assert.False(_service.Tick());

        _core.Settings.Update(s => s with { PauseWhenAway = false });
        Assert.True(_service.Tick());
    }

    [Fact]
    public void Missing_a_reminder_makes_the_next_one_insistent_and_earlier()
    {
        _core.Time.Advance(TimeSpan.FromMinutes(46));
        _service.Tick();
        _service.Resolve(ReminderOutcome.Missed);

        _core.Time.Advance(TimeSpan.FromMinutes(15));
        _service.Tick();

        Assert.Equal(EscalationLevel.Nudge, _raised[^1].Level);
    }

    [Fact]
    public void Trigger_shows_a_manual_reminder_immediately()
    {
        _service.Trigger();

        Assert.True(_raised.Single().Manual);
    }

    [Fact]
    public void Pausing_is_reflected_in_the_status()
    {
        _service.Pause(TimeSpan.FromHours(1));

        var status = _service.Status();
        Assert.True(status.IsPaused);
        Assert.Null(status.TimeUntilNext);

        _service.Resume();
        Assert.False(_service.Status().IsPaused);
    }
}
