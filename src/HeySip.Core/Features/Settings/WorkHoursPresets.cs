using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Settings;

/// <summary>
/// A ready-made schedule: when reminders may appear and on which days. A preset that <paramref name="SkipsWeekend"/> runs
/// on every day except the user's own weekend days, so it fits any week, not just Saturday and Sunday.
/// </summary>
public sealed record WorkHoursPreset(string Id, string Name, TimeOnly Start, TimeOnly End, IReadOnlyList<DayOfWeek> Days, bool SkipsWeekend = false)
{
    /// <summary>The days this preset turns reminders on for, given the user's weekend.</summary>
    public DayOfWeek[] DaysFor(AppSettings settings) =>
        SkipsWeekend ? [.. WeekDays.All.Except(settings.WeekendDays)] : [.. Days];

    /// <summary>The preset in words for the user's own weekend, such as "9:00 to 18:00, Sunday to Thursday".</summary>
    public string Describe(AppSettings settings) =>
        $"{Start.ToString(TimeFormats.ClockShortHour)} to {End.ToString(TimeFormats.ClockShortHour)}, {DayListText.Describe(DaysFor(settings))}";

    public AppSettings Apply(AppSettings settings) => settings with { ActiveStart = Start, ActiveEnd = End, ActiveDays = DaysFor(settings) };

    public bool Matches(AppSettings settings) =>
        settings.ActiveStart == Start && settings.ActiveEnd == End && new HashSet<DayOfWeek>(settings.ActiveDays).SetEquals(DaysFor(settings));
}

public static class WorkHoursPresets
{
    public const string EverydayId = "everyday";
    public const string OfficeId = "office";
    public const string EarlyBirdId = "early";
    public const string NightOwlId = "nightowl";
    public const string WeekendsOffId = "weekends-off";

    public static IReadOnlyList<WorkHoursPreset> All { get; } =
    [
        new(EverydayId, "Every day", AppSettings.DefaultActiveStart, AppSettings.DefaultActiveEnd, WeekDays.All),
        new(OfficeId, "Office", new(9, 0), new(18, 0), WeekDays.MondayToFriday, SkipsWeekend: true),
        new(EarlyBirdId, "Early bird", new(6, 0), new(20, 0), WeekDays.All),
        new(NightOwlId, "Night owl", new(14, 0), new(2, 0), WeekDays.All),
        new(WeekendsOffId, "Weekends off", AppSettings.DefaultActiveStart, AppSettings.DefaultActiveEnd, WeekDays.MondayToFriday, SkipsWeekend: true),
    ];

    /// <summary>The preset with this id. Throws for an id that doesn't exist, which is a mistake in the code, not in user input.</summary>
    public static WorkHoursPreset ById(string id) => All.Single(preset => preset.Id == id);

    /// <summary>The preset the current schedule matches exactly, or null when it has been customised.</summary>
    public static WorkHoursPreset? Matching(AppSettings settings) => All.FirstOrDefault(preset => preset.Matches(settings));
}
