using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Scheduling;

/// <summary>Plain-language descriptions of when the next reminder will happen, shared by every place that shows it.</summary>
public static class ReminderStatusText
{
    /// <summary>A full sentence for the Today page.</summary>
    public static string Full(ReminderStatus status, AppSettings settings, TimeProvider time)
    {
        if (status.IsPaused)
        {
            return status.PausedUntil is { } until && until != DateTimeOffset.MaxValue
                ? $"Reminders paused until {UntilText(until, settings, time)}"
                : "Reminders paused";
        }

        if (status.NextAt is not { } next)
        {
            return "No reminders are scheduled. Check your active days in Settings.";
        }

        if (status.OutsideActiveHours)
        {
            return $"Outside your active hours. Next nudge {DayWord(next, settings, time)} at {LocalTime(next, settings, time):HH:mm}";
        }

        var left = status.TimeUntilNext ?? TimeSpan.Zero;
        return left.TotalMinutes < 1 ? "Next nudge: any moment now" : $"Next nudge in about {Math.Round(left.TotalMinutes)} min";
    }

    /// <summary>A few words for the status bar and tray tooltip.</summary>
    public static string Brief(ReminderStatus status, AppSettings settings, TimeProvider time)
    {
        if (status.IsPaused)
        {
            return status.PausedUntil is { } until && until != DateTimeOffset.MaxValue
                ? $"Paused until {UntilText(until, settings, time)}"
                : "Paused";
        }

        if (status.NextAt is not { } next)
        {
            return "No reminders scheduled";
        }

        if (status.OutsideActiveHours)
        {
            var day = DayWord(next, settings, time);
            return day == "today"
                ? $"Quiet until {LocalTime(next, settings, time):HH:mm}"
                : $"Quiet until {day} {LocalTime(next, settings, time):HH:mm}";
        }

        var left = status.TimeUntilNext ?? TimeSpan.Zero;
        return left.TotalMinutes < 1 ? "Next nudge now" : $"Next nudge in {Math.Max(1, (int)Math.Round(left.TotalMinutes))} min";
    }

    /// <summary>"15:00" when it is today, otherwise "tomorrow 10:00" or "Friday 10:00".</summary>
    private static string UntilText(DateTimeOffset moment, AppSettings settings, TimeProvider time)
    {
        var day = DayWord(moment, settings, time);
        var clock = LocalTime(moment, settings, time).ToString("HH:mm");
        return day == "today" ? clock : $"{day} {clock}";
    }

    private static DateTime LocalTime(DateTimeOffset moment, AppSettings settings, TimeProvider time) =>
        TimeZoneInfo.ConvertTime(moment, ZoneResolver.Resolve(settings.TimeZoneId, time)).DateTime;

    private static string DayWord(DateTimeOffset moment, AppSettings settings, TimeProvider time)
    {
        var zone = ZoneResolver.Resolve(settings.TimeZoneId, time);
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var day = ZoneResolver.LocalDate(moment, zone);
        return day == today ? "today" : day == today.AddDays(1) ? "tomorrow" : LocalTime(moment, settings, time).ToString("dddd");
    }
}
