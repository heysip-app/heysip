namespace HeySip.App.Shared;

/// <summary>How a calendar day is named in lists and tabs.</summary>
public static class DayLabel
{
    /// <summary>"Today", "Yesterday", or a short date such as "Mon 9 Mar".</summary>
    public static string Short(DateOnly day, DateOnly today) =>
        day == today ? "Today"
        : day == today.AddDays(-1) ? "Yesterday"
        : day.ToString("ddd d MMM");

    /// <summary>A full heading such as "Monday 9 March".</summary>
    public static string Long(DateOnly day) => day.ToString("dddd d MMMM");
}
