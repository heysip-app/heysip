using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HeySip.App.Shell;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

/// <summary>Renders the main window in both themes so the design can be reviewed by eye. Set HEYSIP_SHOTS to choose the folder.</summary>
public sealed class DesignScreenshotTests
{
    private static readonly string ShotDirectory = Environment.GetEnvironmentVariable("HEYSIP_SHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "heysip-shots");

    private static void Capture(UiHarness ui, string name, ThemeVariant theme)
    {
        Application.Current!.RequestedThemeVariant = theme;
        ui.Show();
        ui.Screenshot(Path.Combine(ShotDirectory, $"{name}-{theme.Key.ToString()!.ToLowerInvariant()}.png"));
    }

    [AvaloniaFact]
    public void Tidepool_in_both_themes()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var ui = UiHarness.Create(services => services.AddSingleton<TimeProvider>(clock));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true, Name = "Alex", Character = "heysip.drip" });
        ui.Get<ReminderService>().Restart();

        var hydration = ui.Get<HydrationService>();
        foreach (var ml in new[] { 2100, 1400, 2200, 2000, 2300 }) // Mar 5..9: a streak after the dip
        {
            hydration.Log(ml);
            clock.Advance(TimeSpan.FromHours(24));
        }

        // Now Mar 10 at 09:00: a partly drunk day.
        hydration.Log(250);
        clock.Advance(TimeSpan.FromHours(3));
        hydration.Log(500);
        hydration.Log(500);
        ui.Shell.Tick();

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            ui.Shell.Navigate(ShellPage.Today);
            Capture(ui, "today", theme);
            ui.Shell.Navigate(ShellPage.Stats);
            Capture(ui, "stats", theme);
            ui.Shell.Navigate(ShellPage.History);
            Capture(ui, "history", theme);
            ui.Shell.Navigate(ShellPage.Settings);
            Capture(ui, "settings", theme);
        }

        // Phase 2: day tabs in the rail.
        ui.Shell.OpenDay(new DateOnly(2026, 3, 8));
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.OpenDay(new DateOnly(2026, 3, 9));
        Capture(ui, "daytab", ThemeVariant.Dark);
        Capture(ui, "daytab", ThemeVariant.Light);

        // Phase 3: the command palette (empty, then filtered) and settings search.
        ui.Shell.Navigate(ShellPage.Today);
        ui.Shell.Palette.Open();
        Capture(ui, "palette", ThemeVariant.Dark);
        Capture(ui, "palette", ThemeVariant.Light);
        ui.Shell.Palette.Query = "log";
        Capture(ui, "palette-log", ThemeVariant.Dark);
        ui.Shell.Palette.Query = "interval";
        Capture(ui, "palette-interval", ThemeVariant.Dark);
        ui.Shell.Palette.Query = "qqqq";
        Capture(ui, "palette-none", ThemeVariant.Dark);
        ui.Shell.Palette.Close();

        ui.Shell.OpenSettings("theme");
        Capture(ui, "search-theme", ThemeVariant.Dark);
        ui.Shell.OpenSettings("snooze");
        Capture(ui, "search-snooze", ThemeVariant.Light);
        ui.Shell.OpenSettings("qqqq");
        Capture(ui, "search-none", ThemeVariant.Dark);
        ui.Shell.OpenSettings(null);

        // Phase 3: the command palette (empty, then filtered), and settings search.
        ui.Shell.Navigate(ShellPage.Today);
        ui.Shell.Palette.Open();
        Capture(ui, "palette", ThemeVariant.Dark);
        ui.Shell.Palette.Query = "log";
        Capture(ui, "palette-log", ThemeVariant.Dark);
        ui.Shell.Palette.Query = "interval";
        Capture(ui, "palette-setting", ThemeVariant.Light);
        ui.Shell.Palette.Close();

        ui.Shell.OpenSettings("reminder");
        Capture(ui, "settings-search", ThemeVariant.Dark);
        ui.Shell.OpenSettings("zzzz");
        Capture(ui, "settings-none", ThemeVariant.Dark);
        ui.Shell.OpenSettings(null);

        // The pause state shows up in the status bar.
        ui.Shell.StatusBar.TogglePauseCommand.Execute(null);
        ui.Shell.Navigate(ShellPage.Today);
        Capture(ui, "today-paused", ThemeVariant.Dark);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }
}
