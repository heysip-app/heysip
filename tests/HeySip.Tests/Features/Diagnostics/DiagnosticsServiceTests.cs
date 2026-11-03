using HeySip.Core.Features.Diagnostics;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.SharedKernel.Diagnostics;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Diagnostics;

public sealed class DiagnosticsServiceTests : IDisposable
{
    private readonly CoreHarness _core = new();
    private readonly DiagnosticLog _log;
    private readonly DiagnosticsService _service;

    public DiagnosticsServiceTests()
    {
        _log = new DiagnosticLog(_core.Time, _core.Data.Location.LogDirectory);
        var reminders = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), new AlwaysAvailable(), _core.Time, _log);
        _service = new DiagnosticsService(_core.Data.Location, _core.Settings, reminders, _log, _core.Time);
    }

    public void Dispose() => _core.Dispose();

    [Fact]
    public void The_report_describes_the_app_and_machine()
    {
        var report = _service.BuildReport();

        Assert.StartsWith("HeySip diagnostics", report);
        Assert.Contains($"Version: {DiagnosticsService.AppVersion}", report);
        Assert.Contains("OS:", report);
        Assert.Contains(".NET:", report);
        Assert.Contains(_core.Data.Root, report);
        Assert.Contains("Remind every 45 min", report);
    }

    [Fact]
    public void The_report_never_contains_the_name_or_any_drink_data()
    {
        _core.Settings.Update(s => s with { Name = "Dipjyoti-Secret" });
        _core.Hydration.Log(777);

        var report = _service.BuildReport();

        Assert.DoesNotContain("Dipjyoti-Secret", report);
        // The report names the (randomly named) data folder, whose hex digits can contain "777" by chance, so look for how an amount is written.
        Assert.DoesNotContain("777 ml", report);
        Assert.DoesNotContain("777ml", report);
        Assert.DoesNotContain("Amount", report);
        Assert.Contains("Name set: yes", report);
    }

    [Fact]
    public void The_report_includes_the_recent_log()
    {
        _log.Error("UI", "something odd happened", new InvalidOperationException("detail"));

        var report = _service.BuildReport();

        Assert.Contains("something odd happened", report);
        Assert.Contains("InvalidOperationException: detail", report);
    }

    [Fact]
    public void The_report_says_so_when_reminders_are_paused_or_quiet()
    {
        var reminders = new ReminderService(_core.Settings, new ReminderScheduler(_core.Time), new AlwaysAvailable(), _core.Time, _log);
        reminders.Pause(TimeSpan.FromHours(1));
        var service = new DiagnosticsService(_core.Data.Location, _core.Settings, reminders, _log, _core.Time);

        Assert.Contains("Paused: yes", service.BuildReport());
    }

    [Fact]
    public void The_version_has_no_build_metadata_suffix() =>
        Assert.DoesNotContain('+', DiagnosticsService.AppVersion);
}
