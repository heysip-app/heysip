namespace HeySip.Core.SharedKernel;

public static class ZoneResolver
{
    private static readonly TimeSpan LocalZoneRecheck = TimeSpan.FromSeconds(30);
    private static long _lastRecheck = Environment.TickCount64;

    /// <summary>
    /// .NET caches the machine's zone for the life of the process, so a zone change while the app runs (travel, a changed
    /// system setting) would otherwise go unnoticed until restart. Drop the cache now and then so "automatic" stays automatic.
    /// </summary>
    private static TimeZoneInfo MachineZone(TimeProvider time)
    {
        if (ReferenceEquals(time, TimeProvider.System))
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastRecheck);
            if (now - last >= LocalZoneRecheck.TotalMilliseconds && Interlocked.CompareExchange(ref _lastRecheck, now, last) == last)
            {
                TimeZoneInfo.ClearCachedData();
            }
        }

        return time.LocalTimeZone;
    }

    /// <summary>The configured time zone, or the machine's local zone when none is set or the id is unknown.</summary>
    public static TimeZoneInfo Resolve(string? timeZoneId, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return MachineZone(time);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return MachineZone(time);
        }
    }

    public static DateTime LocalNow(TimeZoneInfo zone, TimeProvider time) =>
        TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime;

    public static DateOnly LocalDate(DateTimeOffset moment, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);
}
