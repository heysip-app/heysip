using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HeySip.Tests.Ui;

public sealed class DiagnosticsAndPerformanceTests
{
    private sealed class FakeDesktop : IClipboardService, IShellLauncher
    {
        public string? Copied { get; private set; }

        public string? OpenedFolder { get; private set; }

        public Task<bool> SetTextAsync(string text)
        {
            Copied = text;
            return Task.FromResult(true);
        }

        public bool OpenUrl(string url) => true;

        public bool OpenFolder(string path)
        {
            OpenedFolder = path;
            return true;
        }
    }

    private static DripControl MainWindowDrip(UiHarness ui) =>
        ui.Window.GetVisualDescendants().OfType<DripControl>().First(drip => drip.IsAnimated);

    [AvaloniaFact]
    public void The_mascot_animates_only_while_its_window_is_visible()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Show();
        var drip = MainWindowDrip(ui);
        Assert.True(drip.IsTimerRunning, "Should animate while the window is showing.");

        ui.Window.Hide(); // what closing to the tray does
        Dispatcher.UIThread.RunJobs();
        Assert.False(drip.IsTimerRunning, "Should stop animating while hidden in the tray.");

        ui.Window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(drip.IsTimerRunning, "Should resume when the window comes back.");
    }

    [AvaloniaFact]
    public void The_mascot_stops_when_its_page_is_navigated_away()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Show();
        var drip = MainWindowDrip(ui);
        Assert.True(drip.IsTimerRunning);

        ui.Shell.Navigate(ShellPage.Stats);
        ui.Show();

        Assert.False(drip.IsTimerRunning, "The Today page's mascot is detached, so it must not keep ticking.");
    }

    [AvaloniaFact]
    public void Icons_and_thumbnails_never_start_a_timer()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Shell.Navigate(ShellPage.Settings); // the character picker shows non-animated thumbnails
        ui.Show();

        var still = ui.Window.GetVisualDescendants().OfType<DripControl>().Where(drip => !drip.IsAnimated).ToList();

        Assert.NotEmpty(still);
        Assert.All(still, drip => Assert.False(drip.IsTimerRunning));
    }

    [AvaloniaFact]
    public async Task Copy_diagnostics_puts_a_safe_report_on_the_clipboard()
    {
        var desktop = new FakeDesktop();
        using var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<IClipboardService>(desktop);
            services.AddSingleton<IShellLauncher>(desktop);
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true, Name = "Hidden-Name" });
        ui.Get<HydrationService>().Log(1234);

        await ui.Shell.Settings.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.NotNull(desktop.Copied);
        Assert.StartsWith("HeySip diagnostics", desktop.Copied);
        Assert.DoesNotContain("Hidden-Name", desktop.Copied);
        Assert.DoesNotContain("1234", desktop.Copied);
        Assert.Contains("Copied", ui.Shell.Settings.DiagnosticsStatus);
    }

    [AvaloniaFact]
    public void Open_data_folder_opens_the_apps_data_folder()
    {
        var desktop = new FakeDesktop();
        using var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<IClipboardService>(desktop);
            services.AddSingleton<IShellLauncher>(desktop);
        });

        ui.Shell.Settings.OpenDataFolderCommand.Execute(null);

        Assert.Equal(ui.Data.Root, desktop.OpenedFolder);
    }

    [AvaloniaFact]
    public void The_startup_error_window_explains_the_problem_without_alarming_the_user()
    {
        var window = new StartupErrorWindow("database is locked", @"C:\Data\HeySip", "full exception text");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var text = string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.Contains("couldn't open its data", text);
        Assert.Contains("Nothing has been deleted", text);
        Assert.Contains("database is locked", text);
        Assert.Equal(3, window.GetVisualDescendants().OfType<Button>().Count(button => button.IsVisible));
        window.Close();
    }

    [AvaloniaFact]
    public void The_startup_error_window_offers_to_try_again_only_when_it_can()
    {
        var retried = 0;
        var window = new StartupErrorWindow("database is locked", @"C:\Data\HeySip", "details", () => retried++);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tryAgain = window.GetVisualDescendants().OfType<Button>().Single(button => (string?)button.Content == "Try again");
        Assert.True(tryAgain.IsVisible);
        tryAgain.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, retried);
        window.Close();
    }

    [Fact]
    public void An_unopenable_database_makes_startup_fail_with_a_clear_error_instead_of_crashing_silently()
    {
        using var data = new TempData();
        Directory.CreateDirectory(data.Location.DatabasePath); // a folder where the database file should be
        using var host = HeySip.App.Composition.AppHost.Build(data.Location);

        var failure = Record.Exception(() => host.Start());

        Assert.NotNull(failure);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }
}
