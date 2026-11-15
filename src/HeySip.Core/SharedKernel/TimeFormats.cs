namespace HeySip.Core.SharedKernel;

/// <summary>The format strings the app shows times and days with, so every screen writes them the same way.</summary>
public static class TimeFormats
{
    /// <summary>A time of day on a 24-hour clock, such as "09:05".</summary>
    public const string Clock = "HH:mm";

    /// <summary>A time of day with the hour unpadded, such as "9:05".</summary>
    public const string ClockShortHour = "H':'mm";

    /// <summary>The full name of a weekday, such as "Friday".</summary>
    public const string DayName = "dddd";

    /// <summary>The short name of a weekday, such as "Fri".</summary>
    public const string ShortDayName = "ddd";

    /// <summary>A short weekday with the date, such as "Fri 6 Mar".</summary>
    public const string ShortDayAndDate = "ddd d MMM";
}
