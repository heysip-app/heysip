using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Scheduling;

public enum EscalationLevel
{
    Calm = 0,
    Nudge = 1,
    Insistent = 2,
}

/// <summary>
/// Decides when the next reminder is due. It only ever compares the current time with a stored due time (never a long
/// countdown), so it stays correct across sleep and wake. Not thread-safe on its own; <see cref="ReminderService"/> locks.
/// </summary>
public sealed class ReminderScheduler(TimeProvider time)
{
    private DateTimeOffset? _pausedUntil;

    public DateTimeOffset NextDueAt { get; private set; } = time.GetUtcNow();

    /// <summary>How many reminders in a row went unanswered.</summary>
    public int Ignored { get; private set; }

    public bool IsPaused => _pausedUntil is { } until && until > time.GetUtcNow();

    public DateTimeOffset? PausedUntil => IsPaused ? _pausedUntil : null;

    /// <summary>How many times longer the interval gets once the goal is met, if the user chose to be reminded less then.</summary>
    public const int AfterGoalFactor = 3;

    private static bool Relaxed(AppSettings settings, bool goalReached) => goalReached && settings.RemindLessAfterGoal;

    private static int Interval(AppSettings settings, bool goalReached) =>
        settings.IntervalMinutes * (Relaxed(settings, goalReached) ? AfterGoalFactor : 1);

    /// <summary>Once the goal is met (and the user asked for fewer reminders) reminders stay calm however many are ignored.</summary>
    public EscalationLevel Level(AppSettings settings, bool goalReached = false) =>
        !settings.Escalate || Relaxed(settings, goalReached)
            ? EscalationLevel.Calm
            : (EscalationLevel)Math.Min(Ignored, (int)EscalationLevel.Insistent);

    /// <summary>Start a fresh countdown of one interval from now.</summary>
    public void Restart(AppSettings settings, bool goalReached = false)
    {
        Ignored = 0;
        NextDueAt = time.GetUtcNow().AddMinutes(Interval(settings, goalReached));
    }

    public void Acknowledged(AppSettings settings, bool goalReached = false) => Restart(settings, goalReached);

    public void Snoozed(AppSettings settings) => NextDueAt = time.GetUtcNow().AddMinutes(settings.SnoozeMinutes);

    /// <summary>The reminder went unanswered. With escalation on, the next one comes back sooner.</summary>
    public void Missed(AppSettings settings, bool goalReached = false)
    {
        Ignored++;
        var minutes = settings.Escalate && !Relaxed(settings, goalReached)
            ? Math.Min(settings.IntervalMinutes, Math.Clamp(settings.IntervalMinutes / 3, 5, 15))
            : Interval(settings, goalReached);
        NextDueAt = time.GetUtcNow().AddMinutes(minutes);
    }

    private static TimeSpan StaleAfter(AppSettings settings) => TimeSpan.FromMinutes(Math.Max(settings.IntervalMinutes, 60));

    public void DueIn(TimeSpan delay) => NextDueAt = time.GetUtcNow().Add(delay);

    public void PauseFor(TimeSpan duration) => _pausedUntil = time.GetUtcNow().Add(duration);

    /// <summary>Quiet until a moment in time. A moment that has already passed ends any pause.</summary>
    public void PauseUntil(DateTimeOffset until) => _pausedUntil = until > time.GetUtcNow() ? until : null;

    public void PauseIndefinitely() => _pausedUntil = DateTimeOffset.MaxValue;

    /// <summary>
    /// Ends a pause the user ended on purpose. The countdown starts again from now: someone who just pressed Resume
    /// doesn't want a reminder within seconds. (A pause that simply runs out leaves the overdue reminder to fire.)
    /// </summary>
    public void Resume(AppSettings settings, bool goalReached = false)
    {
        _pausedUntil = null;
        Restart(settings, goalReached);
    }

    /// <summary>
    /// True once when a reminder should appear now. When it fires, the next one is provisionally scheduled one interval
    /// ahead so an answered, snoozed or missed outcome can refine it.
    /// </summary>
    public bool TryTakeDue(AppSettings settings, bool userIsBusy, bool goalReached = false)
    {
        var now = time.GetUtcNow();
        if (IsPaused || now < NextDueAt || userIsBusy)
        {
            return false;
        }

        var zone = ZoneResolver.Resolve(settings.TimeZoneId, time);
        if (!ActiveHours.Contains(settings, ZoneResolver.LocalNow(zone, time)))
        {
            return false;
        }

        // A due time that has been waiting for hours (overnight, a long sleep) means a fresh start, not a continuation of
        // last night's ignored reminders, so the first nudge of the day isn't already insistent.
        if (now - NextDueAt > StaleAfter(settings))
        {
            Ignored = 0;
        }

        NextDueAt = now.AddMinutes(Interval(settings, goalReached));
        return true;
    }

    /// <summary>
    /// When the next reminder will really appear: the due time, pushed forward to the next moment reminders are allowed
    /// (active hours and days). Null while paused, or if no active moment exists in the next eight days.
    /// </summary>
    public DateTimeOffset? NextReminderAt(AppSettings settings)
    {
        if (IsPaused)
        {
            return null;
        }

        var zone = ZoneResolver.Resolve(settings.TimeZoneId, time);
        var candidate = NextDueAt > time.GetUtcNow() ? NextDueAt : time.GetUtcNow();
        for (var step = 0; step <= 8 * 24 * 60; step++, candidate = candidate.AddMinutes(1))
        {
            if (ActiveHours.Contains(settings, TimeZoneInfo.ConvertTime(candidate, zone).DateTime))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>True when reminders are not allowed right now because it is outside the active hours or days.</summary>
    public bool IsOutsideActiveHours(AppSettings settings)
    {
        var zone = ZoneResolver.Resolve(settings.TimeZoneId, time);
        return !ActiveHours.Contains(settings, ZoneResolver.LocalNow(zone, time));
    }
}
