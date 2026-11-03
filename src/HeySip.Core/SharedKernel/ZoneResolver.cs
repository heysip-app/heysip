namespace HeySip.Core.SharedKernel;

public static class ZoneResolver
{
    /// <summary>The configured time zone, or the machine's local zone when none is set or the id is unknown.</summary>
    public static TimeZoneInfo Resolve(string? timeZoneId, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return time.LocalTimeZone;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return time.LocalTimeZone;
        }
    }

    public static DateTime LocalNow(TimeZoneInfo zone, TimeProvider time) =>
        TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;

    public static DateOnly LocalDate(DateTimeOffset moment, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);
}
