using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Scheduling;

/// <summary>Plain-language descriptions of when the next reminder will happen, shared by every place that shows it.</summary>
public static class ReminderStatusText
{
    private const string Today = "today";
    private const string Tomorrow = "tomorrow";

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
            return $"Outside your active hours. Next nudge {DayOf(next, settings, time).Word} at {ClockOf(next, settings, time)}";
        }

        var left = status.TimeUntilNext ?? TimeSpan.Zero;
        return left < ReminderRules.ImminentWithin ? "Next nudge: any moment now" : $"Next nudge in about {Math.Round(left.TotalMinutes)} min";
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
            return $"Quiet until {UntilText(next, settings, time)}";
        }

        var left = status.TimeUntilNext ?? TimeSpan.Zero;
        return left < ReminderRules.ImminentWithin ? "Next nudge now" : $"Next nudge in {Math.Max(1, (int)Math.Round(left.TotalMinutes))} min";
    }

    /// <summary>"15:00" when it is today, otherwise "tomorrow 10:00" or "Friday 10:00".</summary>
    private static string UntilText(DateTimeOffset moment, AppSettings settings, TimeProvider time)
    {
        var day = DayOf(moment, settings, time);
        var clock = ClockOf(moment, settings, time);
        return day.IsToday ? clock : $"{day.Word} {clock}";
    }

    private static string ClockOf(DateTimeOffset moment, AppSettings settings, TimeProvider time) =>
        TimeZoneInfo.ConvertTime(moment, ZoneResolver.Resolve(settings.TimeZoneId, time)).ToString(TimeFormats.Clock);

    /// <summary>How a moment's calendar day is said: "today", "tomorrow", or the weekday's name.</summary>
    private readonly record struct DayWord(bool IsToday, string Word);

    private static DayWord DayOf(DateTimeOffset moment, AppSettings settings, TimeProvider time)
    {
        var zone = ZoneResolver.Resolve(settings.TimeZoneId, time);
        var today = ZoneResolver.LocalDate(time.GetUtcNow(), zone);
        var day = ZoneResolver.LocalDate(moment, zone);

        if (day == today)
        {
            return new DayWord(IsToday: true, Today);
        }

        return new DayWord(IsToday: false, day == today.AddDays(1) ? Tomorrow : TimeZoneInfo.ConvertTime(moment, zone).ToString(TimeFormats.DayName));
    }
}
