using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.Core.Features.Scheduling;

/// <summary>Whether the user is away, presenting, or otherwise shouldn't be interrupted right now.</summary>
public interface IPresenceProbe
{
    bool IsAwayOrBusy();
}

public sealed class AlwaysAvailable : IPresenceProbe
{
    public bool IsAwayOrBusy() => false;
}

/// <param name="Greeting">A hello from a newly chosen character: no question, no buttons, and it never touches the schedule or the log.</param>
/// <param name="NewLook">With <paramref name="Greeting"/>: the same character in a different outfit, so it asks how it looks instead of saying hi.</param>
public sealed record Reminder(EscalationLevel Level, bool Manual, bool Greeting = false, bool NewLook = false);

public enum ReminderOutcome
{
    Drank,
    Snoozed,
    Missed,

    /// <summary>The user logged a drink somewhere else (main window, tray) while the reminder was showing.</summary>
    DrankElsewhere,
}

/// <param name="NextAt">When the next reminder will really appear, accounting for active hours; null while paused.</param>
/// <param name="OutsideActiveHours">True when reminders are being held because it is outside the active hours or days.</param>
public sealed record ReminderStatus(bool IsPaused, DateTimeOffset? PausedUntil, DateTimeOffset? NextAt, TimeSpan? TimeUntilNext, bool OutsideActiveHours);

/// <summary>Polls the scheduler on a short timer and raises <see cref="Due"/> when a reminder should appear.</summary>
public sealed class ReminderService(
    SettingsService settings,
    ReminderScheduler scheduler,
    IPresenceProbe presence,
    TimeProvider time,
    DiagnosticLog? log = null,
    HydrationService? hydration = null)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();
    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private bool _listening;
    private volatile bool _greetingPending;

    /// <summary>Raised on a background thread; subscribers marshal to the UI themselves.</summary>
    public event Action<Reminder>? Due;

    /// <summary>Raised after a pause, resume, restart or answered reminder changes what "next nudge" means. On the caller's thread.</summary>
    public event Action? StatusChanged;

    /// <summary>True while a reminder is on screen, so reminders never stack.</summary>
    public bool ReminderVisible
    {
        get;
        set
        {
            field = value;
            _greetingPending = false; // whatever was waiting to appear has now appeared (or finished)
        }
    }

    /// <summary>The most recent error swallowed by the polling loop, for diagnostics.</summary>
    public Exception? LastError { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            if (hydration is not null && !_listening)
            {
                // However a drink gets logged (reminder button, main window, tray), it answers the reminder.
                hydration.DrinkLogged += (_, _) => Resolve(ReminderOutcome.Drank);
                _listening = true;
            }

            scheduler.Restart(settings.Current, GoalReached());
            _stopping = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_stopping.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        lock (_gate)
        {
            _stopping?.Cancel();
            loop = _loop;
            _loop = null;
        }

        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }
    }

    /// <summary>One poll. Public so tests can drive it with a fake clock.</summary>
    public bool Tick()
    {
        var current = settings.Current;
        var goal = GoalReached();
        Reminder? reminder = null;
        lock (_gate)
        {
            if (!ReminderVisible)
            {
                var busy = current.PauseWhenAway && presence.IsAwayOrBusy();
                if (scheduler.TryTakeDue(current, busy, goal))
                {
                    reminder = new Reminder(scheduler.Level(current, goal), Manual: false);
                }
            }
        }

        if (reminder is null)
        {
            return false;
        }

        Due?.Invoke(reminder);
        return true;
    }

    /// <summary>Records how the user answered the last reminder and schedules the next one accordingly.</summary>
    public void Resolve(ReminderOutcome outcome)
    {
        var current = settings.Current;
        var goal = GoalReached();
        lock (_gate)
        {
            switch (outcome)
            {
                case ReminderOutcome.Drank:
                case ReminderOutcome.DrankElsewhere:
                    scheduler.Acknowledged(current, goal);
                    break;
                case ReminderOutcome.Snoozed:
                    scheduler.Snoozed(current);
                    break;
                default:
                    scheduler.Missed(current, goal);
                    break;
            }
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Pause for a while, or until resumed when <paramref name="duration"/> is null.</summary>
    public void Pause(TimeSpan? duration)
    {
        lock (_gate)
        {
            if (duration is { } span)
            {
                scheduler.PauseFor(span);
            }
            else
            {
                scheduler.PauseIndefinitely();
            }
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Stay quiet until a time of day (today if it is still ahead, otherwise tomorrow), in the configured time zone.</summary>
    public void PauseUntil(TimeOnly clock)
    {
        var zone = ZoneResolver.Resolve(settings.Current.TimeZoneId, time);
        var now = ZoneResolver.LocalNow(zone, time);
        var target = now.Date + clock.ToTimeSpan();
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        PauseAt(target, zone);
    }

    /// <summary>Stay quiet until tomorrow, when the active hours begin again.</summary>
    public void PauseUntilTomorrow()
    {
        var zone = ZoneResolver.Resolve(settings.Current.TimeZoneId, time);
        var tomorrow = ZoneResolver.LocalNow(zone, time).Date.AddDays(1) + settings.Current.ActiveStart.ToTimeSpan();
        PauseAt(tomorrow, zone);
    }

    private void PauseAt(DateTime local, TimeZoneInfo zone)
    {
        var until = new DateTimeOffset(local, zone.GetUtcOffset(local));
        lock (_gate)
        {
            scheduler.PauseUntil(until);
        }

        StatusChanged?.Invoke();
    }

    public void Resume()
    {
        var current = settings.Current;
        var goal = GoalReached();
        lock (_gate)
        {
            scheduler.Resume(current, goal);
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Re-arm the countdown after the interval setting changes.</summary>
    public void Restart()
    {
        var current = settings.Current;
        var goal = GoalReached();
        lock (_gate)
        {
            scheduler.Restart(current, goal);
        }

        StatusChanged?.Invoke();
    }

    public ReminderStatus Status()
    {
        var current = settings.Current;
        lock (_gate)
        {
            var next = scheduler.NextReminderAt(current);
            return new ReminderStatus(
                scheduler.IsPaused,
                scheduler.PausedUntil,
                next,
                next is { } at ? at - time.GetUtcNow() : null,
                scheduler.IsOutsideActiveHours(current));
        }
    }

    /// <summary>Show a reminder right now (tray "Drink now", settings "Test reminder").</summary>
    public void Trigger()
    {
        if (!ReminderVisible)
        {
            Due?.Invoke(new Reminder(EscalationLevel.Calm, Manual: true));
        }
    }

    /// <summary>The character in use has just changed: let it visit and say hi, so the user sees it without asking for a test.</summary>
    public void Greet(bool newLook = false)
    {
        // One already on its way to the screen counts too, so quick changes make one visit and one chime.
        if (!ReminderVisible && !_greetingPending)
        {
            _greetingPending = true;
            Due?.Invoke(new Reminder(EscalationLevel.Calm, Manual: true, Greeting: true, NewLook: newLook));
        }
    }

    /// <summary>Whether today's goal is met. Only asked of the database when the setting that cares about it is on.</summary>
    private bool GoalReached() => settings.Current.RemindLessAfterGoal && hydration?.GoalReachedToday() == true;

    private async Task RunAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false))
            {
                try
                {
                    Tick();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One bad poll (a failing presence probe, a subscriber that threw) must not end the reminders for good.
                    LastError = exception;
                    log?.Error("Reminders", "A reminder check failed; reminders keep running.", exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
