using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HeySip.App.Features.Settings;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

public sealed class ShellBehaviourTests
{
    private sealed class DeniedStartupRegistration : IStartupRegistration
    {
        public bool IsSupported => true;

        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled) => throw new UnauthorizedAccessException("blocked by policy");
    }

    private static FakeTimeProvider NoonClock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)); // Tuesday, inside active hours
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    private static UiHarness Harness(FakeTimeProvider clock, Action<IServiceCollection>? more = null)
    {
        var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            more?.Invoke(services);
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    // ---- status bar

    [AvaloniaFact]
    public void The_status_bar_shows_progress_and_the_next_nudge()
    {
        using var ui = Harness(NoonClock());

        var bar = ui.Shell.StatusBar;
        Assert.Equal("0 / 2000 ml", bar.ProgressText);
        Assert.Equal("Next nudge in 45 min", bar.NextText);
        Assert.False(bar.HasStreak);

        ui.Get<HydrationService>().Log(500);

        Assert.Equal("500 / 2000 ml", bar.ProgressText);
        Assert.Equal(25, bar.ProgressPercent);
        Assert.False(bar.GoalReached);
    }

    [AvaloniaFact]
    public void Reaching_the_goal_starts_a_streak_in_the_status_bar()
    {
        using var ui = Harness(NoonClock());

        ui.Get<HydrationService>().Log(2000);

        Assert.True(ui.Shell.StatusBar.GoalReached);
        Assert.True(ui.Shell.StatusBar.HasStreak);
        Assert.Equal("1 day streak", ui.Shell.StatusBar.StreakText);
    }

    [AvaloniaFact]
    public void The_pause_button_toggles_reminders_and_the_text_follows()
    {
        using var ui = Harness(NoonClock());
        var bar = ui.Shell.StatusBar;

        bar.TogglePauseCommand.Execute(null);
        Assert.True(bar.IsPaused);
        Assert.Equal("Resume", bar.PauseLabel);
        Assert.Equal("Paused until 13:00", bar.NextText);

        bar.TogglePauseCommand.Execute(null);
        Assert.False(bar.IsPaused);
        Assert.Equal("Pause 1h", bar.PauseLabel);
        Assert.Equal("Next nudge in 45 min", bar.NextText);
    }

    [AvaloniaFact]
    public void Status_bar_items_take_you_to_the_matching_page()
    {
        using var ui = Harness(NoonClock());
        ui.Show();

        ui.Shell.StatusBar.ShowStatsCommand.Execute(null);
        Assert.IsType<StatsViewModel>(ui.Shell.Current);

        ui.Shell.StatusBar.ShowTodayCommand.Execute(null);
        Assert.IsType<TodayViewModel>(ui.Shell.Current);
    }

    // ---- days that roll over while the window stays open

    [AvaloniaFact]
    public void Everything_refreshes_when_midnight_passes_while_the_window_is_open()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        ui.Get<HydrationService>().Log(750);
        Assert.Equal("750 / 2000 ml", ui.Shell.Today.Summary);

        clock.Advance(TimeSpan.FromHours(24)); // the next day, nothing has been clicked
        ui.Shell.Tick();                       // what the app's timer does

        Assert.Equal("0 / 2000 ml", ui.Shell.Today.Summary);
        Assert.Empty(ui.Shell.Today.Entries);
        Assert.Equal("0 / 2000 ml", ui.Shell.StatusBar.ProgressText);
        Assert.Equal("Wed", ui.Shell.Stats.Bars[^1].Label);   // the chart now ends on the new day
        Assert.Equal(2, ui.Shell.Stats.Bars[^1].Height);        // and that day's bar is empty (the minimum height)
    }

    [AvaloniaFact]
    public void Undo_after_midnight_does_nothing_instead_of_throwing()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        ui.Get<HydrationService>().Log(250);
        clock.Advance(TimeSpan.FromHours(24));

        var failure = Record.Exception(() => ui.Shell.Today.UndoCommand.Execute(null));

        Assert.Null(failure);
    }

    // ---- keyboard

    [AvaloniaFact]
    public void Ctrl_1_2_3_4_switch_pages()
    {
        using var ui = Harness(NoonClock());
        ui.Show();

        ui.Window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<StatsViewModel>(ui.Shell.Current);

        ui.Window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<HeySip.App.Features.History.HistoryViewModel>(ui.Shell.Current);

        ui.Window.KeyPressQwerty(PhysicalKey.Digit4, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);

        ui.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TodayViewModel>(ui.Shell.Current);
    }

    // ---- closing the window

    [AvaloniaFact]
    public void Closing_with_close_to_tray_hides_the_window_and_it_can_be_shown_again()
    {
        using var ui = Harness(NoonClock());
        ui.Show();
        ui.Window.HideOnClose = true;

        ui.Window.Close();
        Assert.False(ui.Window.IsVisible);

        var failure = Record.Exception(() => ui.Window.Show());
        Assert.Null(failure);
        Assert.True(ui.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Closing_without_close_to_tray_asks_the_app_to_quit_instead_of_leaving_a_windowless_process()
    {
        using var ui = Harness(NoonClock());
        ui.Show();
        var quitRequests = 0;
        ui.Window.HideOnClose = false;
        ui.Window.QuitRequested = () => quitRequests++;

        ui.Window.Close();

        Assert.Equal(1, quitRequests);
    }

    // ---- clipboard / diagnostics wiring

    [AvaloniaFact]
    public async Task Without_an_owner_window_the_clipboard_reports_failure_and_with_one_it_works()
    {
        using var ui = Harness(NoonClock());
        ui.Show();

        Assert.False(await new DesktopServices().SetTextAsync("x"));

        var withOwner = new DesktopServices { Owner = ui.Window };
        Assert.True(await withOwner.SetTextAsync("copied text"));
    }

    // ---- start with Windows

    [AvaloniaFact]
    public void A_start_with_windows_change_that_is_refused_is_not_saved_and_the_user_is_told()
    {
        using var ui = Harness(NoonClock(), services => services.AddSingleton<IStartupRegistration>(new DeniedStartupRegistration()));
        var settings = ui.Get<SettingsService>();

        ui.Shell.Settings.StartWithOs = true;

        Assert.False(settings.Current.StartWithOs);
        Assert.False(ui.Shell.Settings.StartWithOs);
        Assert.Contains("didn't allow", ui.Shell.Settings.StartupMessage);
    }

    // ---- second launch

    [Fact]
    public void A_second_launch_signals_the_running_copy_to_show_itself()
    {
        var name = "HeySipTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.Acquire(name);
        using var second = SingleInstance.Acquire(name);
        using var shown = new ManualResetEventSlim();
        first.StartListening(shown.Set);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        second.SignalPrimary();

        Assert.True(shown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "The running copy was never told to show itself.");
    }
}
