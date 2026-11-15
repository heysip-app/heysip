using System.Globalization;
using System.Text.RegularExpressions;

namespace HeySip.Core.SharedKernel;

/// <summary>
/// Reads a time of day the way people type it ("9", "9:30", "9pm", "930", "21:00", "noon") and writes one the way the
/// computer's language shows it.
/// </summary>
public static partial class TimeInput
{
    private const int NoonHour = 12;
    private const int HoursInHalfDay = 12;
    private const int MaxMinute = 59;
    private const int MaxHour = 23;

    [GeneratedRegex(@"^(?<hour>\d{1,2})(?:[:.](?<minute>\d{2}))?$")]
    private static partial Regex HourAndMinute();

    [GeneratedRegex(@"^(?<hour>\d{1,2})(?<minute>\d{2})$")]
    private static partial Regex Compact();

    [GeneratedRegex(@"\s*(?<half>a|p)\.?m?\.?$")]
    private static partial Regex HalfOfDay();

    /// <summary>The time as the computer's language writes it, such as "9:30 PM" or "21:30".</summary>
    public static string Format(TimeOnly time, IFormatProvider? culture = null) =>
        time.ToString("t", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Reads <paramref name="text"/> as a time. With "am" or "pm" the hour is 1 to 12; without, hours 13 to 23 and 0 are on a
    /// 24-hour clock, and an hour from 1 to 11 written without a leading zero keeps the half of the day <paramref name="previous"/> was in (so
    /// changing "10:00 PM" by typing "9" gives 9 PM, while "09" is always the morning). Twelve is noon. With no <paramref name="previous"/> a bare hour is read on a 24-hour clock.
    /// </summary>
    public static bool TryParse(string? text, TimeOnly? previous, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var body = text.Trim().ToLowerInvariant();
        switch (body)
        {
            case "noon":
                time = new TimeOnly(NoonHour, 0);
                return true;
            case "midnight":
                time = new TimeOnly(0, 0);
                return true;
        }

        char? half = null;
        if (HalfOfDay().Match(body) is { Success: true } suffix)
        {
            half = suffix.Groups["half"].Value[0];
            body = body[..suffix.Index].Trim();
        }

        var match = HourAndMinute().Match(body);
        if (!match.Success)
        {
            match = Compact().Match(body);
            if (!match.Success)
            {
                return false;
            }
        }

        var hourText = match.Groups["hour"].Value;
        var hour = int.Parse(hourText, CultureInfo.InvariantCulture);
        var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
        if (minute > MaxMinute)
        {
            return false;
        }

        if (half is { } marker)
        {
            if (hour is < 1 or > HoursInHalfDay)
            {
                return false;
            }

            hour = marker == 'p' ? hour % HoursInHalfDay + HoursInHalfDay : hour % HoursInHalfDay;
        }
        else if (hour is >= 1 and < NoonHour && !hourText.StartsWith('0') && previous is { Hour: >= NoonHour })
        {
            // "9" with an afternoon time already showing means 9 in the evening. A leading zero ("09") always means the morning.
            hour += HoursInHalfDay;
        }

        if (hour > MaxHour)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
