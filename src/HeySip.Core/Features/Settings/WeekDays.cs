namespace HeySip.Core.Features.Settings;

/// <summary>The sets of weekdays the schedule is built from, named once instead of spelled out wherever they are needed.</summary>
public static class WeekDays
{
    /// <summary>The first day of the week as it is written on screen.</summary>
    public const DayOfWeek First = DayOfWeek.Monday;

    public static readonly int DaysInWeek = Enum.GetValues<DayOfWeek>().Length;

    public static IReadOnlyList<DayOfWeek> All { get; } = Enum.GetValues<DayOfWeek>();

    /// <summary>All seven days, Monday first.</summary>
    public static IReadOnlyList<DayOfWeek> MondayFirst { get; } = [.. All.OrderBy(PositionInWeek)];

    public static IReadOnlyList<DayOfWeek> MondayToFriday { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static IReadOnlyList<DayOfWeek> SaturdayAndSunday { get; } = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>0 for Monday up to 6 for Sunday.</summary>
    public static int PositionInWeek(DayOfWeek day) => ((int)day - (int)First + DaysInWeek) % DaysInWeek;

    /// <summary>A fresh array each time, because settings hold their days in arrays that callers may keep.</summary>
    public static DayOfWeek[] AllDays() => [.. All];

    public static DayOfWeek[] DefaultWeekend() => [.. SaturdayAndSunday];
}
