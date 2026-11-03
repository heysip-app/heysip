using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.Core.Features.Diagnostics;

/// <summary>
/// Builds the plain-text report a user can paste into a bug report. It deliberately leaves out the user's name and any
/// drink data; it only says whether a name is set.
/// </summary>
public sealed class DiagnosticsService(
    AppDataLocation location,
    SettingsService settings,
    ReminderService reminders,
    DiagnosticLog log,
    TimeProvider time)
{
    public static string AppVersion { get; } = ReadVersion();

    public string DataFolder => location.RootDirectory;

    public string BuildReport()
    {
        var current = settings.Current;
        var zone = ZoneResolver.Resolve(current.TimeZoneId, time);
        var status = reminders.Status();
        var report = new StringBuilder();

        report.AppendLine("HeySip diagnostics");
        report.AppendLine($"Version: {AppVersion}");
        report.AppendLine($"Generated: {time.GetLocalNow():yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        report.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"Time zone in use: {zone.Id}{(current.TimeZoneId is null ? " (automatic)" : "")}");
        report.AppendLine($"Data folder: {location.RootDirectory}");
        report.AppendLine($"Database: {DescribeFile(location.DatabasePath)}");
        report.AppendLine($"Settings file: {DescribeFile(location.SettingsPath)}");
        report.AppendLine($"Last settings save error: {(settings.LastSaveError is { } saveError ? $"{saveError.GetType().Name}: {saveError.Message}" : "none")}");
        report.AppendLine();
        report.AppendLine("Settings (name left out)");
        report.AppendLine($"  Name set: {(string.IsNullOrWhiteSpace(current.Name) ? "no" : "yes")}");
        report.AppendLine($"  Remind every {current.IntervalMinutes} min, snooze {current.SnoozeMinutes} min");
        report.AppendLine($"  Active {current.ActiveStart:HH\\:mm}-{current.ActiveEnd:HH\\:mm} on {string.Join(", ", current.ActiveDays.Select(day => day.ToString()[..3]))}");
        report.AppendLine($"  Goal {current.DailyGoalMilliliters} ml, glass {current.GlassMilliliters} ml");
        report.AppendLine($"  Escalate {current.Escalate}, pause when away {current.PauseWhenAway}");
        report.AppendLine($"  Theme {current.Theme}, character {current.Character}, messages {current.Messages}, monitor {current.Monitor}");
        report.AppendLine($"  Close to tray {current.CloseToTray}, start with OS {current.StartWithOs}");
        report.AppendLine();
        report.AppendLine("Reminders");
        report.AppendLine($"  Paused: {(status.IsPaused ? "yes" : "no")}");
        report.AppendLine($"  Outside active hours now: {(status.OutsideActiveHours ? "yes" : "no")}");
        report.AppendLine($"  Next reminder: {(status.NextAt is { } next ? TimeZoneInfo.ConvertTime(next, zone).ToString("yyyy-MM-dd HH:mm") : "none scheduled")}");
        report.AppendLine($"  Last polling error: {(reminders.LastError is { } error ? $"{error.GetType().Name}: {error.Message}" : "none")}");
        report.AppendLine();
        report.AppendLine("Recent log");
        var tail = log.Tail();
        if (tail.Count == 0)
        {
            report.AppendLine("  (empty)");
        }

        foreach (var line in tail)
        {
            report.AppendLine(line);
        }

        return report.ToString();
    }

    private static string DescribeFile(string path) =>
        File.Exists(path) ? $"{new FileInfo(path).Length / 1024.0:0.#} KB" : "not created yet";

    private static string ReadVersion()
    {
        var informational = typeof(DiagnosticsService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? typeof(DiagnosticsService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
