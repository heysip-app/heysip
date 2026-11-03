namespace HeySip.Core.Features.Settings;

/// <summary>
/// A ready-made schedule: when reminders may appear and on which days. A preset that <paramref name="SkipsWeekend"/> runs
/// on every day except the user's own weekend days, so it fits any week, not just Saturday and Sunday.
/// </summary>
public sealed record WorkHoursPreset(string Id, string Name, string Description, TimeOnly Start, TimeOnly End, DayOfWeek[] Days, bool SkipsWeekend = false)
{
    /// <summary>The days this preset turns reminders on for, given the user's weekend.</summary>
    public DayOfWeek[] DaysFor(AppSettings settings) =>
        SkipsWeekend ? [.. Enum.GetValues<DayOfWeek>().Except(settings.WeekendDays)] : Days;

    /// <summary>The preset in words for the user's own weekend, such as "9:00 to 18:00, Sunday to Thursday".</summary>
    public string Describe(AppSettings settings) =>
        SkipsWeekend ? $"{Start.ToString("H':'mm")} to {End.ToString("H':'mm")}, {DayListText.Describe(DaysFor(settings))}" : Description;

    public AppSettings Apply(AppSettings settings) => settings with { ActiveStart = Start, ActiveEnd = End, ActiveDays = [.. DaysFor(settings)] };

    public bool Matches(AppSettings settings) =>
        settings.ActiveStart == Start && settings.ActiveEnd == End && new HashSet<DayOfWeek>(settings.ActiveDays).SetEquals(DaysFor(settings));
}

public static class WorkHoursPresets
{
    private static readonly DayOfWeek[] Everyday = Enum.GetValues<DayOfWeek>();

    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static IReadOnlyList<WorkHoursPreset> All { get; } =
    [
        new("everyday", "Every day", "10:00 to 23:00, all week", new(10, 0), new(23, 0), Everyday),
        new("office", "Office", "9:00 to 18:00, Monday to Friday", new(9, 0), new(18, 0), Weekdays, SkipsWeekend: true),
        new("early", "Early bird", "6:00 to 20:00, all week", new(6, 0), new(20, 0), Everyday),
        new("nightowl", "Night owl", "14:00 to 02:00, all week", new(14, 0), new(2, 0), Everyday),
        new("weekends-off", "Weekends off", "10:00 to 23:00, Monday to Friday", new(10, 0), new(23, 0), Weekdays, SkipsWeekend: true),
    ];

    /// <summary>The preset the current schedule matches exactly, or null when it has been customised.</summary>
    public static WorkHoursPreset? Matching(AppSettings settings) => All.FirstOrDefault(preset => preset.Matches(settings));
}
