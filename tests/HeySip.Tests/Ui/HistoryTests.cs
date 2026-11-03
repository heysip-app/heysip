using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeySip.App.Features.Day;
using HeySip.App.Features.History;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

public sealed class HistoryTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static FakeTimeProvider NoonClock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
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

    private static void Drink(UiHarness ui, int daysAgo, int ml = 250, int hour = 9) =>
        ui.Get<HydrationService>().LogAt(Today.AddDays(-daysAgo), new TimeOnly(hour, 0), ml);

    /// <summary>Puts one drink on each of the last <paramref name="days"/> days straight into the store, then lets the page read it.</summary>
    private static void Seed(UiHarness ui, int days)
    {
        var store = ui.Get<IntakeStore>();
        for (var i = 1; i <= days; i++)
        {
            store.Add(new DateTimeOffset(Today.AddDays(-i).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), 100 + i);
        }

        ui.Shell.History.Refresh();
    }

    [AvaloniaFact]
    public void History_is_a_page_in_the_rail_with_its_own_shortcut_and_palette_command()
    {
        using var ui = Harness(NoonClock());
        ui.Show();

        Assert.Contains(
            ui.Window.GetVisualDescendants().OfType<RailButton>(),
            button => Avalonia.Automation.AutomationProperties.GetName(button) == "History");

        ui.Shell.ShowHistoryCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(ui.Shell.Current);
        Assert.True(ui.Shell.IsHistory);

        ui.Shell.ShowTodayCommand.Execute(null);
        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "history";
        var item = ui.Shell.Palette.Results.First(result => result.Title == "History");
        Assert.Equal("Ctrl+3", item.Shortcut);
        item.RunCommand.Execute(null);
        Assert.True(ui.Shell.IsHistory);
    }

    [AvaloniaFact]
    public void Days_are_listed_newest_first_with_totals_and_a_year_only_when_it_differs()
    {
        using var ui = Harness(NoonClock());
        Drink(ui, 0, 500);
        Drink(ui, 1, 2100);
        Drink(ui, 4, 300);
        ui.Get<HydrationService>().LogAt(new DateOnly(2025, 12, 30), new TimeOnly(9, 0), 400);

        var days = ui.Shell.History.Days;

        Assert.Equal(["Today", "Yesterday", "Fri 6 Mar", "Tue 30 Dec 2025"], days.Select(day => day.Label));
        Assert.Equal(["500 ml", "2100 ml", "300 ml", "400 ml"], days.Select(day => day.TotalText));
        Assert.Equal([false, true, false, false], days.Select(day => day.GoalMet));
    }

    [AvaloniaFact]
    public void Older_days_load_a_page_at_a_time_without_repeats()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 40);
        var history = ui.Shell.History;

        Assert.Equal(14, history.Days.Count);
        Assert.True(history.HasMore);

        history.LoadMoreCommand.Execute(null);
        Assert.Equal(28, history.Days.Count);

        history.LoadMoreCommand.Execute(null);
        Assert.Equal(40, history.Days.Count);
        Assert.False(history.HasMore);
        Assert.Equal(40, history.Days.Select(day => day.Date).Distinct().Count());
        Assert.Equal(history.Days.OrderByDescending(day => day.Date).Select(day => day.Date), history.Days.Select(day => day.Date));

        history.LoadMoreCommand.Execute(null); // nothing left: harmless
        Assert.Equal(40, history.Days.Count);
    }

    [AvaloniaFact]
    public void An_empty_history_says_so()
    {
        using var ui = Harness(NoonClock());

        Assert.True(ui.Shell.History.IsEmpty);
        Assert.False(ui.Shell.History.HasMore);

        Drink(ui, 2);
        Assert.False(ui.Shell.History.IsEmpty);
    }

    [AvaloniaFact]
    public void Removing_a_drink_updates_its_day_in_place_and_keeps_everything_loaded()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 40);
        ui.Get<HydrationService>().LogAt(Today.AddDays(-3), new TimeOnly(15, 0), 50); // a second drink on day 3
        var history = ui.Shell.History;
        history.LoadMoreCommand.Execute(null);
        var before = history.Days.ToList();

        history.Days.Single(day => day.Date == Today.AddDays(-3)).Entries[1].RemoveCommand.Execute(null);

        Assert.Equal(28, history.Days.Count);
        Assert.Equal("103 ml", history.Days.Single(day => day.Date == Today.AddDays(-3)).TotalText);
        Assert.All(history.Days, day => Assert.Contains(day, before)); // the same objects: nothing was rebuilt
    }

    [AvaloniaFact]
    public void Removing_the_last_drink_of_a_day_removes_the_day()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 5);
        var history = ui.Shell.History;

        history.Days.Single(day => day.Date == Today.AddDays(-2)).Entries[0].RemoveCommand.Execute(null);

        Assert.Equal(4, history.Days.Count);
        Assert.DoesNotContain(history.Days, day => day.Date == Today.AddDays(-2));
    }

    [AvaloniaFact]
    public void A_new_drink_appears_at_the_top_and_pages_already_loaded_stay_loaded()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 40);
        var history = ui.Shell.History;
        history.LoadMoreCommand.Execute(null);
        Assert.Equal(28, history.Days.Count);

        ui.Get<HydrationService>().Log(250);

        Assert.Equal(29, history.Days.Count);
        Assert.Equal("Today", history.Days[0].Label);
        Assert.Equal(["250 ml"], history.Days[0].Entries.Select(entry => entry.Amount));
    }

    [AvaloniaFact]
    public void Labels_move_on_when_midnight_passes()
    {
        var clock = NoonClock();
        using var ui = Harness(clock);
        Drink(ui, 0, 300);
        Assert.Equal("Today", ui.Shell.History.Days[0].Label);

        clock.Advance(TimeSpan.FromHours(24));
        ui.Shell.Tick();

        Assert.Equal("Yesterday", ui.Shell.History.Days[0].Label);
    }

    [AvaloniaFact]
    public void A_day_opens_in_its_own_tab()
    {
        using var ui = Harness(NoonClock());
        Drink(ui, 1);

        ui.Shell.History.Days[0].OpenCommand.Execute(null);

        Assert.Equal("Yesterday", ui.Shell.SelectedTab.Title);
        Assert.IsType<DayViewModel>(ui.Shell.Current);
    }

    [AvaloniaFact]
    public void Scrolling_to_the_bottom_loads_older_days_by_itself()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 60);
        ui.Shell.ShowHistoryCommand.Execute(null);
        ui.Show();
        Dispatcher.UIThread.RunJobs();

        var history = ui.Shell.History;
        var layout = ui.Window.GetVisualDescendants().OfType<HeySip.App.Shared.Components.PageLayout>().Single(page => page.IsEffectivelyVisible);
        var scroller = layout.Scroller!;
        Assert.True(history.Days.Count < 60);
        var loaded = history.Days.Count;

        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.True(history.Days.Count > loaded, "Reaching the end of the list should load more days.");
    }

    [AvaloniaFact]
    public void A_window_taller_than_the_first_page_keeps_loading_until_it_is_full()
    {
        using var ui = Harness(NoonClock());
        Seed(ui, 60);
        ui.Window.Height = 4000; // far taller than 14 days of cards
        ui.Shell.ShowHistoryCommand.Execute(null);
        ui.Show();
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(ui.Shell.History.Days.Count > 14);
    }
}
