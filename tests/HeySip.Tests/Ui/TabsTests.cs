using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeySip.App.Features.Day;
using HeySip.App.Features.Settings;
using HeySip.App.Shared.Components;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

public sealed class TabsTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static FakeTimeProvider NoonClock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)); // Tuesday noon
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    private static UiHarness Harness(FakeTimeProvider clock)
    {
        var ui = UiHarness.Create(services => services.AddSingleton<TimeProvider>(clock));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    private static string[] TabTitles(UiHarness ui) => ui.Shell.Tabs.Select(tab => tab.Title).ToArray();

    // ---- tabs

    [AvaloniaFact]
    public void The_window_starts_with_one_tab_that_cannot_be_closed()
    {
        using var ui = Harness(NoonClock());

        Assert.Equal(["Today"], TabTitles(ui));
        Assert.True(ui.Shell.SelectedTab.IsSelected);
        Assert.False(ui.Shell.SelectedTab.IsClosable);

        ui.Shell.CloseCurrentTabCommand.Execute(null);
        Assert.Equal(["Today"], TabTitles(ui));
    }

    [AvaloniaFact]
    public void Opening_pages_adds_tabs_and_opening_one_again_just_focuses_it()
    {
        using var ui = Harness(NoonClock());

        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);
        ui.Shell.ShowStatsCommand.Execute(null);

        Assert.Equal(["Today", "Stats", "Settings"], TabTitles(ui));
        Assert.IsType<StatsViewModel>(ui.Shell.Current);
        Assert.True(ui.Shell.IsStats);
        Assert.Single(ui.Shell.Tabs, tab => tab.IsSelected);

        ui.Shell.ShowTodayCommand.Execute(null);
        Assert.IsType<TodayViewModel>(ui.Shell.Current);
        Assert.Equal(3, ui.Shell.Tabs.Count);
    }

    [AvaloniaFact]
    public void Closing_the_selected_tab_selects_its_neighbour()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);
        ui.Shell.OpenDay(Today.AddDays(-1));
        // Tabs: Today, Stats, Settings, Yesterday (selected)

        ui.Shell.SelectedTab = ui.Shell.Tabs[1];                // select Stats, then close it
        ui.Shell.CloseCurrentTabCommand.Execute(null);
        Assert.Equal(["Today", "Settings", "Yesterday"], TabTitles(ui));
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);     // Settings slid into Stats' place

        ui.Shell.SelectedTab = ui.Shell.Tabs[^1];               // the last tab: closing it falls back to the one before
        ui.Shell.CloseCurrentTabCommand.Execute(null);
        Assert.Equal(["Today", "Settings"], TabTitles(ui));
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Closing_a_tab_you_are_not_on_leaves_the_selection_alone()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);

        ui.Shell.Tabs[1].CloseCommand.Execute(null); // close Stats while on Settings

        Assert.Equal(["Today", "Settings"], TabTitles(ui));
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Tabs_can_be_cycled_with_wraparound()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);

        ui.Shell.NextTabCommand.Execute(null);
        Assert.IsType<TodayViewModel>(ui.Shell.Current);

        ui.Shell.PreviousTabCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Open_days_are_tabs_in_the_left_rail_and_there_is_no_tab_strip_across_the_top()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.OpenDay(new DateOnly(2026, 3, 9));
        ui.Shell.OpenDay(new DateOnly(2026, 3, 8));
        ui.Show();

        var rail = ui.Window.GetVisualDescendants().OfType<RailTab>().ToList();

        Assert.Equal(new[] { "9", "8" }, rail.Select(tab => ((TabViewModel)tab.DataContext!).Glyph));
        Assert.DoesNotContain(ui.Window.GetVisualDescendants().OfType<Grid>(), grid => grid.Classes.Contains("tab"));

        ui.Shell.CloseCurrentTab();
        ui.Show();
        Assert.Single(ui.Window.GetVisualDescendants().OfType<RailTab>());
        Assert.True(ui.Shell.HasDayTabs);

        ui.Shell.CloseCurrentTab();
        ui.Show();
        Assert.Empty(ui.Window.GetVisualDescendants().OfType<RailTab>());
        Assert.False(ui.Shell.HasDayTabs);
    }

    [AvaloniaFact]
    public void Keyboard_shortcuts_close_and_cycle_tabs()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Shell.ShowSettingsCommand.Execute(null);
        ui.Show();

        ui.Window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Today", "Stats"], TabTitles(ui));

        ui.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TodayViewModel>(ui.Shell.Current);
    }

    // ---- day tabs

    [AvaloniaFact]
    public void A_day_opens_in_its_own_tab_and_reopening_it_focuses_the_same_tab()
    {
        using var ui = Harness(NoonClock());

        ui.Shell.OpenDay(Today.AddDays(-1));
        ui.Shell.OpenDay(Today.AddDays(-3));
        ui.Shell.OpenDay(Today.AddDays(-1));

        Assert.Equal(["Today", "Yesterday", "Sat 7 Mar"], TabTitles(ui));
        Assert.Equal("Yesterday", ui.Shell.SelectedTab.Title);
        Assert.IsType<DayViewModel>(ui.Shell.Current);
        Assert.Equal("Monday 9 March", ((DayViewModel)ui.Shell.Current).Heading);
    }

    [AvaloniaFact]
    public void Todays_own_log_tab_is_named_so_it_isnt_confused_with_the_Today_page()
    {
        using var ui = Harness(NoonClock());

        ui.Shell.OpenDay(Today);

        Assert.Equal(["Today", "Today's log"], TabTitles(ui));
    }

    [AvaloniaFact]
    public void A_closed_day_tab_stops_listening_for_changes()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.OpenDay(Today);
        var day = (DayViewModel)ui.Shell.Current;
        Assert.Equal("0 / 2000 ml", day.Summary);

        ui.Shell.CloseCurrentTabCommand.Execute(null);
        ui.Get<HydrationService>().Log(500);

        Assert.Equal("0 / 2000 ml", day.Summary); // disposed: no longer updated, no leaked subscription
    }

    [AvaloniaFact]
    public void A_forgotten_drink_can_be_added_to_an_earlier_day_from_its_tab()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.OpenDay(Today.AddDays(-1));
        var day = (DayViewModel)ui.Shell.Current;

        day.AddAmount = 400;
        day.AddTime = new TimeSpan(21, 30, 0);
        day.AddDrinkCommand.Execute(null);

        Assert.Equal("400 / 2000 ml", day.Summary);
        Assert.Single(day.Entries);
        Assert.Equal("21:30", day.Entries[0].Time);
        Assert.Contains("Added 400 ml", day.Message);
        Assert.Equal(0, ui.Get<HydrationService>().TodayMilliliters()); // it went on yesterday, not today
    }

    [AvaloniaFact]
    public void A_drink_cannot_be_added_in_the_future_or_with_no_amount()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.OpenDay(Today);
        var day = (DayViewModel)ui.Shell.Current;

        day.AddAmount = 250;
        day.AddTime = new TimeSpan(18, 0, 0); // it is only noon
        day.AddDrinkCommand.Execute(null);
        Assert.Equal("That time hasn't happened yet.", day.Message);
        Assert.Empty(day.Entries);

        day.AddTime = new TimeSpan(9, 0, 0);
        day.AddAmount = 0;
        day.AddDrinkCommand.Execute(null);
        Assert.Equal("Enter an amount above zero.", day.Message);
        Assert.Empty(day.Entries);
    }

    [AvaloniaFact]
    public void A_day_tab_starts_at_the_current_time_for_today_and_midday_for_earlier_days()
    {
        using var ui = Harness(NoonClock());

        ui.Shell.OpenDay(Today);
        Assert.Equal(new TimeSpan(12, 0, 0), ((DayViewModel)ui.Shell.Current).AddTime);

        ui.Shell.OpenDay(Today.AddDays(-2));
        Assert.Equal(new TimeSpan(12, 0, 0), ((DayViewModel)ui.Shell.Current).AddTime);
        Assert.Equal(250, ((DayViewModel)ui.Shell.Current).AddAmount);
    }

    [AvaloniaFact]
    public void Removing_a_drink_from_a_day_tab_updates_it()
    {
        using var ui = Harness(NoonClock());
        ui.Get<HydrationService>().Log(300);
        ui.Shell.OpenDay(Today);
        var day = (DayViewModel)ui.Shell.Current;

        day.Entries[0].RemoveCommand.Execute(null);

        Assert.Equal("0 / 2000 ml", day.Summary);
        Assert.False(day.HasEntries);
    }

    // ---- opening days from the stats chart

    [AvaloniaFact]
    public void Clicking_a_bar_on_the_stats_chart_opens_that_day_in_a_tab()
    {
        using var ui = Harness(NoonClock());
        ui.Get<HydrationService>().Log(250);
        ui.Shell.ShowStatsCommand.Execute(null);

        var yesterday = ui.Shell.Stats.Bars.Single(bar => bar.Day == Today.AddDays(-1));
        yesterday.OpenCommand.Execute(null);

        Assert.Equal("Yesterday", ui.Shell.SelectedTab.Title);
        Assert.IsType<DayViewModel>(ui.Shell.Current);
        Assert.Equal(["Yesterday"], ui.Shell.DayTabs.Select(tab => tab.Title));
    }

    [AvaloniaFact]
    public void Today_always_shows_its_own_log()
    {
        using var ui = Harness(NoonClock());
        ui.Get<HydrationService>().Log(250);
        ui.Show();

        Assert.Contains(ui.Window.GetVisualDescendants().OfType<EntryRow>(), row => row.IsEffectivelyVisible);
    }

    // ---- the rail must never hide a tab

    [AvaloniaFact]
    public void Every_open_day_is_in_the_rail_and_the_fixed_buttons_stay_reachable_however_many_are_open()
    {
        using var ui = Harness(NoonClock());
        for (var i = 0; i < 40; i++)
        {
            ui.Shell.OpenDay(Today.AddDays(-i));
        }

        ui.Show();
        Dispatcher.UIThread.RunJobs();

        var all = ui.Window.GetVisualDescendants().OfType<RailTab>().ToList();
        Assert.Equal(40, all.Count);

        var scroll = ui.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "DayTabsScroll");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "The list should overflow into a scroll, not off the window.");

        foreach (var name in new[] { "Today", "Stats", "Settings" })
        {
            var button = ui.Window.GetVisualDescendants().OfType<RailButton>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
            var bottom = button.TranslatePoint(new Avalonia.Point(0, button.Bounds.Height), ui.Window)!.Value.Y;
            Assert.True(button.IsEffectivelyVisible && bottom <= ui.Window.Bounds.Height, $"{name} is pushed out of the window.");
        }
    }

    [AvaloniaFact]
    public void The_selected_day_tab_is_scrolled_into_view()
    {
        using var ui = Harness(NoonClock());
        for (var i = 0; i < 40; i++)
        {
            ui.Shell.OpenDay(Today.AddDays(-i));
        }

        ui.Show();
        Dispatcher.UIThread.RunJobs();
        var scroll = ui.Window.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "DayTabsScroll");
        var selected = ui.Window.GetVisualDescendants().OfType<RailTab>().Single(tab => ((TabViewModel)tab.DataContext!).IsSelected);

        var top = selected.TranslatePoint(default, scroll)!.Value.Y;

        Assert.InRange(top, 0, scroll.Viewport.Height);
    }

    [AvaloniaFact]
    public void Day_tabs_with_the_same_date_number_are_told_apart_by_their_month()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.OpenDay(new DateOnly(2026, 3, 8));
        ui.Shell.OpenDay(new DateOnly(2026, 2, 8));

        var captions = ui.Shell.DayTabs.Select(tab => (tab.Glyph, tab.Caption)).ToList();

        Assert.Equal(2, captions.Distinct().Count());
        Assert.All(ui.Shell.DayTabs, tab => Assert.Equal("8", tab.Glyph));
    }

    [AvaloniaFact]
    public void A_day_tab_is_renamed_when_midnight_passes()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        ui.Shell.OpenDay(Today);
        ui.Shell.OpenDay(Today.AddDays(-1));
        Assert.Equal(["Today's log", "Yesterday"], ui.Shell.DayTabs.Select(tab => tab.Title));

        clock.Advance(TimeSpan.FromHours(24));
        ui.Shell.Tick();

        Assert.Equal(["Yesterday", "Mon 9 Mar"], ui.Shell.DayTabs.Select(tab => tab.Title));
    }

    // ---- the recent days list on the stats page

    [AvaloniaFact]
    public void Recent_days_lists_two_weeks_newest_first_with_totals_and_goal_marks()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        var hydration = ui.Get<HydrationService>();
        hydration.Log(2100);                  // Mar 10
        clock.Advance(TimeSpan.FromDays(1));  // Mar 11 = today
        hydration.Log(500);

        var recent = ui.Shell.Stats.RecentDays;

        Assert.Equal(14, recent.Count);
        Assert.Equal(["Today", "Yesterday", "Mon 9 Mar"], recent.Take(3).Select(day => day.Label));
        Assert.Equal(["500 ml", "2100 ml", "none yet"], recent.Take(3).Select(day => day.TotalText));
        Assert.Equal([false, true, false], recent.Take(3).Select(day => day.GoalMet));
        Assert.Equal(25, recent[0].Percent);
        Assert.Equal(100, recent[1].Percent);
    }

    [AvaloniaFact]
    public void A_recent_day_opens_its_tab()
    {
        using var ui = Harness(NoonClock());

        ui.Shell.Stats.RecentDays[1].OpenCommand.Execute(null); // yesterday

        Assert.Equal("Yesterday", ui.Shell.SelectedTab.Title);
        Assert.IsType<DayViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Recent_days_follow_a_new_drink_and_the_turn_of_the_day()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        ui.Shell.Stats.Refresh();
        Assert.Equal("none yet", ui.Shell.Stats.RecentDays[0].TotalText);

        ui.Get<HydrationService>().Log(400);
        Assert.Equal("400 ml", ui.Shell.Stats.RecentDays[0].TotalText);

        clock.Advance(TimeSpan.FromHours(24));
        ui.Shell.Tick();
        Assert.Equal(["Today", "Yesterday"], ui.Shell.Stats.RecentDays.Take(2).Select(day => day.Label));
        Assert.Equal("400 ml", ui.Shell.Stats.RecentDays[1].TotalText);
    }

    [AvaloniaFact]
    public void The_stats_page_renders_a_row_for_every_recent_day()
    {
        using var ui = Harness(NoonClock());
        ui.Shell.ShowStatsCommand.Execute(null);
        ui.Show();

        Assert.Equal(14, ui.Window.GetVisualDescendants().OfType<HeySip.App.Features.Stats.RecentDayRow>().Count());
    }
}
