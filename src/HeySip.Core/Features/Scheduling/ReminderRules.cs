namespace HeySip.Core.Features.Scheduling;

/// <summary>The fixed numbers behind when reminders come, named so the scheduler reads as a set of rules.</summary>
public static class ReminderRules
{
    /// <summary>How many times longer the interval gets once the goal is met, if the user chose to be reminded less then.</summary>
    public const int AfterGoalFactor = 3;

    /// <summary>An ignored reminder comes back after this fraction of the interval (one third)...</summary>
    public const int EscalatedRetryDivisor = 3;

    /// <summary>...but never sooner than this many minutes...</summary>
    public const int EscalatedRetryMinMinutes = 5;

    /// <summary>...and never later than this many minutes.</summary>
    public const int EscalatedRetryMaxMinutes = 15;

    /// <summary>A reminder that has waited at least this long (or one interval, if longer) counts as a fresh start.</summary>
    public static readonly TimeSpan MinimumStaleAfter = TimeSpan.FromHours(1);

    /// <summary>How far ahead "when is the next reminder" looks. A week plus a day covers any weekly schedule.</summary>
    public const int LookAheadDays = 8;

    /// <summary>The granularity of that search.</summary>
    public static readonly TimeSpan LookAheadStep = TimeSpan.FromMinutes(1);

    /// <summary>How often the service checks whether a reminder is due.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    /// <summary>Under this long to go, the status says "any moment now" instead of a number of minutes.</summary>
    public static readonly TimeSpan ImminentWithin = TimeSpan.FromMinutes(1);
}
