using HeySip.Core.Features.Scheduling;
using HeySip.Core.SharedKernel.Diagnostics;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Scheduling;

public sealed class ReminderLoopTests : IDisposable
{
    private sealed class FlakyPresence : IPresenceProbe
    {
        public int Calls;

        public bool IsAwayOrBusy()
        {
            if (Interlocked.Increment(ref Calls) == 1)
            {
                throw new InvalidOperationException("presence probe exploded");
            }

            return false;
        }
    }

    private readonly CoreHarness _core = new();

    public void Dispose() => _core.Dispose();

    [Fact]
    public async Task One_failed_check_does_not_end_reminders_for_good()
    {
        var presence = new FlakyPresence();
        var log = new DiagnosticLog(_core.Time, _core.Data.Location.LogDirectory);
        var service = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), presence, _core.Time, log);
        _core.Time.Advance(TimeSpan.FromMinutes(1));
        service.Start();
        await Task.Delay(50, TestContext.Current.CancellationToken); // let the loop reach its first wait

        // The first check throws. Keep ticking until a later one happens (or give up after a generous wait), instead of
        // sleeping a fixed time, so a slow machine can't make this flaky.
        for (var i = 0; i < 100 && Volatile.Read(ref presence.Calls) < 2; i++)
        {
            _core.Time.Advance(ReminderService.PollInterval);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await service.StopAsync();

        Assert.True(presence.Calls >= 2, $"The loop died after the first failure ({presence.Calls} check).");
        Assert.NotNull(service.LastError);
        Assert.Contains(log.Tail(), line => line.Contains("A reminder check failed"));
    }
}
