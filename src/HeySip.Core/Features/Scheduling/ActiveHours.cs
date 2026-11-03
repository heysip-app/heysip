using HeySip.Core.Features.Settings;

namespace HeySip.Core.Features.Scheduling;

public static class ActiveHours
{
    /// <summary>
    /// Whether reminders are allowed at <paramref name="localNow"/>: an enabled weekday and inside the start/end window.
    /// A window whose end is before its start (e.g. 22:00-02:00) spans midnight and belongs to the day it starts on.
    /// </summary>
    public static bool Contains(AppSettings settings, DateTime localNow)
    {
        var time = TimeOnly.FromDateTime(localNow);
        var start = settings.ActiveStart;
        var end = settings.ActiveEnd;

        if (start == end)
        {
            return settings.ActiveDays.Contains(localNow.DayOfWeek);
        }

        if (start < end)
        {
            return settings.ActiveDays.Contains(localNow.DayOfWeek) && time >= start && time < end;
        }

        // Overnight window: the evening part belongs to today, the early-morning part to yesterday's window.
        return time >= start
            ? settings.ActiveDays.Contains(localNow.DayOfWeek)
            : time < end && settings.ActiveDays.Contains(localNow.AddDays(-1).DayOfWeek);
    }
}
