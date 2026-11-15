namespace HeySip.Core.Features.Scheduling;

/// <summary>One way to pause reminders for a set time, such as "30 minutes".</summary>
public sealed record PauseOption(string Label, TimeSpan Duration);

/// <summary>The pauses the tray, the command palette and the status bar all offer, defined once.</summary>
public static class PauseOptions
{
    public static readonly PauseOption HalfHour = new("30 minutes", TimeSpan.FromMinutes(30));
    public static readonly PauseOption OneHour = new("1 hour", TimeSpan.FromHours(1));
    public static readonly PauseOption TwoHours = new("2 hours", TimeSpan.FromHours(2));

    public static IReadOnlyList<PauseOption> Timed { get; } = [HalfHour, OneHour, TwoHours];

    /// <summary>What the one-click "Pause 1h" buttons use.</summary>
    public static readonly PauseOption Quick = OneHour;

    /// <summary>The "quiet until" times of day the command palette suggests, while they are still ahead.</summary>
    public static IReadOnlyList<TimeOnly> QuietUntilSuggestions { get; } = [new(13, 0), new(15, 0), new(18, 0), new(20, 0)];

    /// <summary>What the Today page's "Quiet until" box starts at.</summary>
    public static readonly TimeOnly DefaultQuietUntil = new(18, 0);
}
