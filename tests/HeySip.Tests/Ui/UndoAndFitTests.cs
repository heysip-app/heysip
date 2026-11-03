using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

/// <summary>The "Removed ... Undo" bar, and opening at a size that fits the screen.</summary>
public sealed class UndoAndFitTests
{
    private static UiHarness Harness()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var ui = UiHarness.Create(services => services.AddSingleton<TimeProvider>(clock));
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        // Tests decide when the bar would time out, instead of waiting for it.
        ui.Shell.RemovalBanner.Schedule = (hide, _) => Pending.Add(hide);
        return ui;
    }

    [ThreadStatic]
    private static List<Action>? _pending;

    private static List<Action> Pending => _pending ??= [];

    // ---- undo

    [AvaloniaFact]
    public void Removing_a_drink_offers_to_put_it_back()
    {
        Pending.Clear();
        using var ui = Harness();
        var entry = ui.Get<HydrationService>().Log(330);

        ui.Get<HydrationService>().Remove(entry.Id);

        var banner = ui.Shell.RemovalBanner;
        Assert.True(banner.IsVisible);
        Assert.Equal("Removed 330 ml from 12:00", banner.Text);
        Assert.Equal(0, ui.Get<HydrationService>().TodayMilliliters());
    }

    [AvaloniaFact]
    public void Undo_brings_the_drink_back_at_its_original_time_and_closes_the_bar()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        hydration.LogAt(new DateOnly(2026, 3, 9), new TimeOnly(8, 15), 400);
        var yesterday = hydration.Day(new DateOnly(2026, 3, 9)).Entries.Single();
        hydration.Remove(yesterday.Id);

        ui.Shell.RemovalBanner.UndoCommand.Execute(null);

        var restored = hydration.Day(new DateOnly(2026, 3, 9)).Entries.Single();
        Assert.Equal(yesterday.At, restored.At);
        Assert.Equal(400, restored.Milliliters);
        Assert.False(ui.Shell.RemovalBanner.IsVisible);
    }

    [AvaloniaFact]
    public void Undo_works_from_the_history_page_and_the_list_there_updates()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        hydration.LogAt(new DateOnly(2026, 3, 8), new TimeOnly(9, 0), 250);
        ui.Shell.Navigate(ShellPage.History);
        var history = ui.Shell.History;
        Assert.Single(history.Days);

        history.Days[0].Entries[0].RemoveCommand.Execute(null);
        Assert.Empty(history.Days);

        ui.Shell.RemovalBanner.UndoCommand.Execute(null);
        Assert.Single(history.Days);
        Assert.Equal("250 ml", history.Days[0].TotalText);
    }

    [AvaloniaFact]
    public void Only_the_latest_removal_can_be_undone()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        var first = hydration.Log(100);
        var second = hydration.Log(200);
        hydration.Remove(first.Id);
        hydration.Remove(second.Id);

        Assert.Equal("Removed 200 ml from 12:00", ui.Shell.RemovalBanner.Text);
        ui.Shell.RemovalBanner.UndoCommand.Execute(null);

        Assert.Equal(200, hydration.TodayMilliliters());
    }

    [AvaloniaFact]
    public void Dismissing_or_timing_out_hides_the_bar_and_a_stale_timer_cannot_hide_a_newer_one()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        var banner = ui.Shell.RemovalBanner;
        hydration.Remove(hydration.Log(100).Id);
        var firstTimer = Pending[^1];
        hydration.Remove(hydration.Log(200).Id); // a second removal restarts the countdown
        var secondTimer = Pending[^1];

        firstTimer();
        Assert.True(banner.IsVisible, "The first removal's timer must not close the newer bar.");

        secondTimer();
        Assert.False(banner.IsVisible);

        hydration.Remove(hydration.Log(300).Id);
        banner.DismissCommand.Execute(null);
        Assert.False(banner.IsVisible);
        banner.UndoCommand.Execute(null); // nothing left to undo: harmless
        Assert.Equal(0, hydration.TodayMilliliters());
    }

    [AvaloniaFact]
    public void Clearing_all_history_is_not_something_the_bar_offers_to_undo()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        hydration.Log(100);

        hydration.ClearHistory();

        Assert.False(ui.Shell.RemovalBanner.IsVisible);
    }

    [AvaloniaFact]
    public void Putting_a_drink_back_does_not_count_as_answering_a_reminder()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        var logged = 0;
        hydration.DrinkLogged += (_, _) => logged++;
        hydration.Remove(hydration.Log(100).Id);
        Assert.Equal(1, logged);

        ui.Shell.RemovalBanner.UndoCommand.Execute(null);

        Assert.Equal(1, logged);
    }

    [AvaloniaFact]
    public void The_bar_is_in_the_window_and_only_visible_after_a_removal()
    {
        Pending.Clear();
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        ui.Show();
        var bar = ui.Window.GetVisualDescendants().OfType<RemovalBar>().Single();
        Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible);

        hydration.Remove(hydration.Log(250).Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(bar.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Undo") && button.IsEffectivelyVisible);
    }

    // ---- fitting the screen

    [Theory]
    [InlineData(1920, 1040, 1.0, 1180, 780)]      // a roomy desktop keeps the preferred size
    [InlineData(1366, 720, 1.5, 837.81, 441.6)]   // a 1366x768 laptop at 150%: inside the usable area
    [InlineData(1920, 1000, 2.0, 883.2, 460)]     // a 1080p laptop at 200%
    [InlineData(1280, 680, 2.0, 700, 440)]        // a tiny screen: never below the minimum
    public void The_window_opens_no_bigger_than_the_usable_screen(int areaW, int areaH, double scale, double wantW, double wantH)
    {
        var size = MainWindow.FitSize(new Size(1180, 780), new Size(700, 440), new PixelRect(0, 0, areaW, areaH), scale);

        Assert.Equal(wantW, Math.Round(size.Width, 2));
        Assert.Equal(wantH, Math.Round(size.Height, 2));
    }

    [Fact]
    public void An_unknown_screen_leaves_the_preferred_size_alone()
    {
        var size = MainWindow.FitSize(new Size(1180, 780), new Size(700, 440), new PixelRect(0, 0, 0, 0), 1);

        Assert.Equal(new Size(1180, 780), size);
    }

    [AvaloniaFact]
    public void The_window_can_be_made_small_enough_for_a_scaled_laptop_screen()
    {
        using var ui = Harness();

        Assert.True(ui.Window.MinWidth <= 700);
        Assert.True(ui.Window.MinHeight <= 440);
    }
}
