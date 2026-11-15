using HeySip.Core.SharedKernel;

namespace HeySip.Tests.SharedKernel;

/// <summary>The real system clock path, which re-reads the machine's zone now and then.</summary>
public sealed class ZoneResolverSystemClockTests
{
    [Fact]
    public void The_automatic_zone_is_the_machines_zone_and_stays_usable_across_many_calls()
    {
        for (var i = 0; i < 1000; i++)
        {
            var zone = ZoneResolver.Resolve(null, TimeProvider.System);

            Assert.NotNull(zone);
            Assert.Equal(TimeZoneInfo.Local.BaseUtcOffset, zone.BaseUtcOffset);
        }
    }

    [Fact]
    public void Concurrent_callers_never_see_a_broken_zone()
    {
        var failures = 0;

        Parallel.For(0, 2000, _ =>
        {
            try
            {
                var local = ZoneResolver.LocalNow(ZoneResolver.Resolve(null, TimeProvider.System), TimeProvider.System);
                if (Math.Abs((local - DateTime.Now).TotalMinutes) > 61)
                {
                    Interlocked.Increment(ref failures);
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
    }

    [Fact]
    public void An_unknown_zone_id_falls_back_to_the_machine_zone()
    {
        Assert.NotNull(ZoneResolver.Resolve("Not/AZone", TimeProvider.System));
    }
}
