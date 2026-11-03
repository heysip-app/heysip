using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using HeySip.App.Features.Companion;
using HeySip.App.Features.Settings;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shell;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace HeySip.Tests.Ui;

public sealed class MainWindowTests
{
    private static readonly string ShotDirectory = Environment.GetEnvironmentVariable("HEYSIP_SHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "heysip-shots");

    [AvaloniaFact]
    public void The_window_opens_on_Today_and_shows_the_progress()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true, Name = "Alex" });
        ui.Get<HydrationService>().Log(750);
        ui.Show();

        Assert.IsType<TodayViewModel>(ui.Shell.Current);
        Assert.Equal("750 / 2000 ml", ui.Shell.Today.Summary);
        Assert.Contains("Alex", ui.Shell.Today.Greeting);
        Assert.NotNull(ui.Window.GetVisualDescendants().OfType<TodayView>().FirstOrDefault());
        ui.Screenshot(Path.Combine(ShotDirectory, "today.png"));
    }

    [AvaloniaFact]
    public void Navigation_swaps_the_page_and_Stats_reflects_logged_drinks()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<HydrationService>().Log(2200);
        ui.Show();

        ui.Shell.Navigate(ShellPage.Stats);
        ui.Show();
        Assert.IsType<StatsViewModel>(ui.Shell.Current);
        Assert.Equal(7, ui.Shell.Stats.Bars.Count);
        Assert.Equal("1 day", ui.Shell.Stats.Streak);
        ui.Screenshot(Path.Combine(ShotDirectory, "stats.png"));

        ui.Shell.Navigate(ShellPage.Settings);
        ui.Show();
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);
        ui.Screenshot(Path.Combine(ShotDirectory, "settings.png"));
    }

    [AvaloniaFact]
    public void Editing_a_setting_saves_it_immediately()
    {
        using var ui = UiHarness.Create();
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { Onboarded = true });

        ui.Shell.Settings.Name = "Robin";
        ui.Shell.Settings.IntervalMinutes = 20;
        ui.Shell.Settings.Escalate = false;
        ui.Shell.Settings.Days.First(day => day.Day == DayOfWeek.Sunday).IsOn = false;

        Assert.Equal("Robin", settings.Current.Name);
        Assert.Equal(20, settings.Current.IntervalMinutes);
        Assert.False(settings.Current.Escalate);
        Assert.DoesNotContain(DayOfWeek.Sunday, settings.Current.ActiveDays);
    }

    [AvaloniaFact]
    public void Choosing_a_character_in_settings_updates_the_saved_settings()
    {
        using var ui = UiHarness.Create();
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with { Onboarded = true });

        ui.Shell.Settings.Characters.First(c => c.Id == "heysip.blossom").SelectCommand.Execute(null);

        Assert.Equal("heysip.blossom", settings.Current.Character);
        Assert.True(ui.Shell.Settings.Characters.First(c => c.Id == "heysip.blossom").IsSelected);
        Assert.Equal("heysip.blossom", ui.Shell.Today.Character);
    }

    [AvaloniaFact]
    public void First_run_shows_the_welcome_and_finishing_it_saves_the_choices()
    {
        using var ui = UiHarness.Create();
        ui.Shell.Onboarding.ShowIfFirstRun();
        ui.Show();
        Assert.True(ui.Shell.Onboarding.IsVisible);
        ui.Screenshot(Path.Combine(ShotDirectory, "onboarding.png"));

        var completed = false;
        ui.Shell.Onboarding.Completed += () => completed = true;
        ui.Shell.Onboarding.Name = "Kai";
        ui.Shell.Onboarding.PickIntervalCommand.Execute(60);
        ui.Shell.Onboarding.NextCommand.Execute(null);
        ui.Shell.Onboarding.NextCommand.Execute(null);
        ui.Shell.Onboarding.Characters.First(c => c.Id == "heysip.minty").SelectCommand.Execute(null);
        ui.Shell.Onboarding.NextCommand.Execute(null);

        var saved = ui.Get<SettingsService>().Current;
        Assert.True(completed);
        Assert.False(ui.Shell.Onboarding.IsVisible);
        Assert.True(saved.Onboarded);
        Assert.Equal("Kai", saved.Name);
        Assert.Equal(60, saved.IntervalMinutes);
        Assert.Equal("heysip.minty", saved.Character);
    }

    [AvaloniaFact]
    public void The_companion_window_renders_each_pose()
    {
        using var ui = UiHarness.Create();
        var viewModel = ui.Get<CompanionViewModel>();
        var window = new CompanionWindow { DataContext = viewModel };
        window.Show();

        // The headless platform has no animation clock, so the slide-in would never finish; snap it instead.
        foreach (var panel in window.GetVisualDescendants().OfType<Panel>().Where(p => p.Classes.Contains("pet")))
        {
            panel.Transitions = null;
        }

        viewModel.IsOffstage = false;
        viewModel.BubbleText = "Alex, time for a sip of water!";
        viewModel.IsBubbleVisible = true;
        viewModel.AreButtonsVisible = true;
        foreach (var pose in Enum.GetValues<HeySip.App.Shared.Controls.DripPose>())
        {
            viewModel.Pose = pose;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Directory.CreateDirectory(ShotDirectory);
            UiHarness.SavePng(frame, Path.Combine(ShotDirectory, $"companion-{pose}.png"));
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Quick_logging_from_the_Today_page_updates_the_totals()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });

        ui.Shell.Today.LogGlassCommand.Execute(null);
        ui.Shell.Today.QuickAmounts[2].AddCommand.Execute(null);

        Assert.Equal("750 / 2000 ml", ui.Shell.Today.Summary);
        Assert.Equal(2, ui.Shell.Today.Entries.Count);

        ui.Shell.Today.UndoCommand.Execute(null);
        Assert.Equal("250 / 2000 ml", ui.Shell.Today.Summary);
    }
}
