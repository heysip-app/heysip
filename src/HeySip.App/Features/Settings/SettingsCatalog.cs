namespace HeySip.App.Features.Settings;

/// <summary>One searchable setting. <see cref="Key"/> matches the <c>Show{Key}</c> property on the settings view model.</summary>
public sealed record SettingEntry(string Key, string Title, string Description, string Keywords);

/// <summary>
/// Every setting in one list, so the Settings page search and the command palette find the same things by the same words.
/// Matching is plain "contains", so results are predictable: every word typed must appear in the title, description or keywords.
/// </summary>
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingEntry> All { get; } =
    [
        new("Name", "Your name", "Used in greetings", "call me greeting hello"),
        new("Goal", "Daily goal", "How much water to drink each day", "target ml litres liters amount"),
        new("Glass", "Glass size", "How much one glass is", "cup amount ml portion"),
        new("Interval", "Reminder interval", "Minutes between reminders", "every frequency how often"),
        new("Snooze", "Snooze length", "Minutes before a snoozed reminder returns", "delay later postpone"),
        new("Hours", "Active hours", "When reminders may appear", "start end from until time schedule quiet night morning"),
        new("Presets", "Schedule presets", "One-tap hours such as office, early bird, night owl or weekends off", "office work early bird night owl weekends schedule hours days quick"),
        new("Days", "Active days", "Which days of the week reminders appear, and which days count as your weekend", "weekdays weekend weekends days off monday tuesday wednesday thursday friday saturday sunday"),
        new("Zone", "Time zone", "Which clock to follow", "timezone travel local utc"),
        new("Escalate", "Escalate ignored reminders", "Ignored reminders come back sooner and more insistently", "insistent nag worried persistent"),
        new("AfterGoal", "Remind less after goal", "Reminders come less often once today's goal is met", "quieter fewer goal reached done calm relax"),
        new("Away", "Pause when away", "Hold reminders while idle, presenting or full-screen", "idle fullscreen presentation meeting busy lock"),
        new("Test", "Show a test reminder", "Preview what a reminder looks like", "preview try demo"),
        new("Character", "Character", "Choose your companion", "mascot drip minty sunny blossom axolotl duckling look appearance install pack download file add new"),
        new("Online", "Check online for updates and new characters", "Let HeySip check the internet for updates, notices, and new characters and accessories", "internet network download store offline privacy connect update updates notices news browse look find new characters online"),
        new("Outfit", "Outfit", "Hats, glasses, scarves and things your companion can wear or hold", "accessories accessory hat crown beanie glasses sunglasses scarf bow tie balloon flower wear dress up costume"),
        new("Sounds", "Sounds", "A chime when a reminder appears and a gulp when you drink, with a volume", "audio volume mute unmute chime gulp quiet noise loud sound beep"),
        new("Messages", "Message style", "How the companion talks to you", "tone friendly coach zen personality voice"),
        new("Monitor", "Monitor", "Which screen the companion appears on", "screen display where multiple"),
        new("Theme", "Theme", "Light or dark appearance", "dark light system colour color mode"),
        new("Tray", "Close to tray", "Whether closing the window keeps HeySip running", "quit exit minimize background"),
        new("Startup", "Start with Windows", "Start HeySip when you sign in", "login boot autostart startup sign in"),
        new("Help", "Diagnostics", "Version, a report to copy for bug reports, and the data folder", "copy report bug logs data folder version about"),
        new("Data", "Delete drink history", "Remove everything that has been logged", "clear reset erase remove drinks privacy"),
    ];

    private static readonly Dictionary<string, SettingEntry> ByKey = All.ToDictionary(entry => entry.Key);

    /// <summary>True when every word of <paramref name="query"/> appears in the setting's title, description or keywords.</summary>
    public static bool Matches(SettingEntry entry, string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var haystack = $"{entry.Title} {entry.Description} {entry.Keywords}";
        return words.All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Matches(string key, string? query) => Matches(ByKey[key], query);
}
