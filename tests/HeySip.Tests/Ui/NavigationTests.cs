using Avalonia.Headless.XUnit;
using HeySip.App.Features.Day;
using HeySip.App.Features.History;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shell;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

public sealed class NavigationTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static UiHarness Harness(bool onboarded = true)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var ui = UiHarness.Create(services => services.AddSingleton<TimeProvider>(clock));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = onboarded });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    [AvaloniaFact]
    public void The_Settings_shortcut_clears_a_filter_left_by_a_named_setting()
    {
        using var ui = Harness();
        ui.Shell.OpenSettings("theme");
        ui.Shell.ShowTodayCommand.Execute(null);

        ui.Shell.ShowSettingsCommand.Execute(null);

        Assert.Equal("", ui.Shell.Settings.SearchText);
    }

    [AvaloniaFact]
    public void Shortcuts_cannot_switch_or_close_tabs_behind_the_welcome()
    {
        using var ui = Harness(onboarded: false);
        ui.Shell.Onboarding.ShowIfFirstRun();

        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);
        ui.Shell.OpenPaletteCommand.Execute(null);
        ui.Shell.NextTabCommand.Execute(null);

        Assert.IsType<TodayViewModel>(ui.Shell.Current);
        Assert.Single(ui.Shell.Tabs);
        Assert.False(ui.Shell.Palette.IsOpen);
    }

    [AvaloniaFact]
    public void Escape_skips_the_welcome_and_otherwise_closes_the_palette()
    {
        using var ui = Harness(onboarded: false);
        ui.Shell.Onboarding.ShowIfFirstRun();

        ui.Shell.DismissCommand.Execute(null);

        Assert.False(ui.Shell.Onboarding.IsVisible);
        Assert.True(ui.Get<SettingsService>().Current.Onboarded);

        ui.Shell.Palette.Open();
        ui.Shell.DismissCommand.Execute(null);
        Assert.False(ui.Shell.Palette.IsOpen);
    }

    [AvaloniaFact]
    public void The_welcome_can_be_shown_again_from_the_first_step()
    {
        using var ui = Harness();
        ui.Shell.Onboarding.ShowIfFirstRun();
        Assert.False(ui.Shell.Onboarding.IsVisible);

        ui.Shell.ShowWelcome();

        Assert.True(ui.Shell.Onboarding.IsVisible);
        Assert.Equal(0, ui.Shell.Onboarding.Step);
    }

    [AvaloniaFact]
    public void Empty_Stats_and_History_lead_back_to_Today()
    {
        using var ui = Harness();

        ui.Shell.ShowStatsCommand.Execute(null);
        Assert.True(ui.Shell.Stats.IsEmpty);
        ui.Shell.Stats.LogDrinkCommand.Execute(null);
        Assert.IsType<TodayViewModel>(ui.Shell.Current);

        ui.Shell.ShowHistoryCommand.Execute(null);
        Assert.True(ui.Shell.History.IsEmpty);
        ui.Shell.History.LogDrinkCommand.Execute(null);
        Assert.IsType<TodayViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Today_links_to_the_schedule_and_to_Stats()
    {
        using var ui = Harness();

        ui.Shell.Today.ChangeScheduleCommand.Execute(null);
        Assert.True(ui.Shell.IsSettings);
        Assert.Equal("schedule", ui.Shell.Settings.SearchText);

        ui.Shell.Today.SeeStatsCommand.Execute(null);
        Assert.IsType<StatsViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void A_day_steps_to_its_neighbours_but_never_into_the_future()
    {
        using var ui = Harness();
        ui.Shell.OpenDay(Today.AddDays(-1));
        var yesterday = (DayViewModel)ui.Shell.Current;
        Assert.True(yesterday.HasNextDay);

        yesterday.NextDayCommand.Execute(null);
        var today = (DayViewModel)ui.Shell.Current;
        Assert.Equal(Today, today.Day);
        Assert.False(today.HasNextDay);

        today.NextDayCommand.Execute(null);
        Assert.Same(today, ui.Shell.Current);

        today.PreviousDayCommand.Execute(null);
        Assert.Equal(Today.AddDays(-1), ((DayViewModel)ui.Shell.Current).Day);
    }

    [AvaloniaFact]
    public void A_day_leads_back_to_History()
    {
        using var ui = Harness();
        ui.Shell.OpenDay(Today.AddDays(-2));

        ((DayViewModel)ui.Shell.Current).BackToHistoryCommand.Execute(null);

        Assert.IsType<HistoryViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void The_status_bar_reaches_Stats_and_History_even_without_a_streak()
    {
        using var ui = Harness();
        Assert.False(ui.Shell.StatusBar.HasStreak);

        ui.Shell.StatusBar.ShowStatsCommand.Execute(null);
        Assert.IsType<StatsViewModel>(ui.Shell.Current);

        ui.Shell.StatusBar.ShowHistoryCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Typing_a_date_in_the_palette_offers_that_day_and_ignores_future_dates()
    {
        using var ui = Harness();
        ui.Shell.Palette.Open();

        ui.Shell.Palette.Query = "2026-03-08";
        Assert.StartsWith("go.date.2026-03-08", ui.Shell.Palette.Results[0].Command.Id);
        ui.Shell.Palette.RunSelectedCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 3, 8), ((DayViewModel)ui.Shell.Current).Day);

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "2026-04-01";
        Assert.DoesNotContain(ui.Shell.Palette.Results, item => item.Command.Id.StartsWith("go.date."));
    }

    [AvaloniaFact]
    public void The_new_page_takes_keyboard_focus_after_a_tab_change()
    {
        using var ui = Harness();
        ui.Show();

        ui.Shell.ShowStatsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var focused = ui.Window.FocusManager?.GetFocusedElement();
        Assert.Equal("PageHost", (focused as Avalonia.Controls.Control)?.Name);
    }

    [AvaloniaFact]
    public void Clicking_the_buddy_asks_for_the_main_window()
    {
        using var ui = Harness();
        var viewModel = ui.Get<HeySip.App.Features.Companion.CompanionViewModel>();
        var opened = 0;
        viewModel.OpenRequested += () => opened++;

        viewModel.RequestOpen();

        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void The_weekend_can_be_any_days_and_the_presets_follow_it()
    {
        using var ui = Harness();
        var settings = ui.Get<SettingsService>();
        var page = ui.Shell.Settings;

        foreach (var toggle in page.WeekendDays)
        {
            toggle.IsOn = toggle.Day is DayOfWeek.Friday;
        }

        Assert.Equal([DayOfWeek.Friday], settings.Current.WeekendDays);

        page.Presets.Single(p => p.Preset.Id == "weekends-off").SelectCommand.Execute(null);

        Assert.DoesNotContain(DayOfWeek.Friday, settings.Current.ActiveDays);
        Assert.Equal(6, settings.Current.ActiveDays.Length);
        Assert.Contains("Saturday to Thursday", page.Presets.Single(p => p.Preset.Id == "weekends-off").Description);
    }
}
