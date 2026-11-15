namespace HeySip.App.Features.Companion;

/// <summary>How long each beat of the companion's visit lasts. Everything the character does goes through these, in one place.</summary>
public static class CompanionTimings
{
    /// <summary>How long a reminder waits for an answer before it counts as missed.</summary>
    public static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long a hello (a newly chosen character or outfit) stays on screen.</summary>
    public static readonly TimeSpan Greeting = TimeSpan.FromMilliseconds(3200);

    /// <summary>Walking on or off the screen.</summary>
    public static readonly TimeSpan Walk = TimeSpan.FromMilliseconds(1150);

    /// <summary>A moment offstage before walking in, so the character starts from outside the window.</summary>
    public static readonly TimeSpan OffstageSettle = TimeSpan.FromMilliseconds(60);

    /// <summary>The line shown after a snooze, before leaving.</summary>
    public static readonly TimeSpan SnoozedFarewell = TimeSpan.FromMilliseconds(1400);

    /// <summary>The line shown after a missed reminder, before leaving.</summary>
    public static readonly TimeSpan MissedFarewell = TimeSpan.FromMilliseconds(1200);

    /// <summary>Celebration: the sip, then the confetti, then goodbye.</summary>
    public static readonly TimeSpan Sip = TimeSpan.FromMilliseconds(950);

    public static readonly TimeSpan Confetti = TimeSpan.FromMilliseconds(1100);

    public static readonly TimeSpan Goodbye = TimeSpan.FromMilliseconds(700);
}
