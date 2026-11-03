using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using HeySip.App.Features.Settings;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

/// <summary>Quiet time, schedule presets, remind-less-after-goal and the waving and sleeping poses, as the user meets them.</summary>
public sealed class QuietPresetsAndPosesTests
{
    private static FakeTimeProvider Clock(int hour = 12)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, hour, 0, 0, TimeSpan.Zero)); // a Tuesday
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

    // ---- poses

    [AvaloniaFact]
    public void Drip_waves_before_the_first_sip_and_is_idle_once_the_day_has_started()
    {
        using var ui = Harness(Clock());

        Assert.Equal(DripPose.Waving, ui.Shell.Today.Pose);

        ui.Get<HydrationService>().Log(250);
        Assert.Equal(DripPose.Idle, ui.Shell.Today.Pose);
    }

    [AvaloniaFact]
    public void Drip_is_happy_after_the_goal_even_at_night()
    {
        using var ui = Harness(Clock(hour: 3));
        ui.Get<HydrationService>().Log(2000);

        Assert.Equal(DripPose.Happy, ui.Shell.Today.Pose);
    }

    [AvaloniaFact]
    public void Drip_sleeps_when_it_is_outside_the_active_hours()
    {
        var clock = Clock(hour: 3);
        using var ui = Harness(clock);
        Assert.Equal(DripPose.Sleeping, ui.Shell.Today.Pose);

        clock.Advance(TimeSpan.FromHours(5)); // 08:00, still before the active hours: waking up, not asleep
        ui.Shell.Tick();
        Assert.Equal(DripPose.Sleepy, ui.Shell.Today.Pose);

        clock.Advance(TimeSpan.FromHours(3)); // 11:00, active again
        ui.Shell.Tick();
        Assert.Equal(DripPose.Waving, ui.Shell.Today.Pose);
    }

    [AvaloniaFact]
    public void The_waving_pose_really_draws_something_different_from_standing()
    {
        static byte[] Draw(DripPose pose)
        {
            var drip = new DripControl { Width = 100, Height = 140, Pose = pose, IsAnimated = false };
            drip.Measure(new Size(100, 140));
            drip.Arrange(new Rect(0, 0, 100, 140));
            using var bitmap = new RenderTargetBitmap(new PixelSize(100, 140));
            bitmap.Render(drip);
            using var stream = new MemoryStream();
#pragma warning disable CS0618
            bitmap.Save(stream);
#pragma warning restore CS0618
            return stream.ToArray();
        }

        var waving = Draw(DripPose.Waving);

        Assert.NotEqual(Draw(DripPose.Idle), waving);
        Assert.NotEqual(Draw(DripPose.Happy), waving);
        Assert.Equal(waving, Draw(DripPose.Waving)); // still, when not animated: no flicker in thumbnails
    }

    // ---- quiet time (the card on the Today page)

    [AvaloniaFact]
    public void The_quiet_card_pauses_and_resumes_and_says_until_when()
    {
        using var ui = Harness(Clock());
        var today = ui.Shell.Today;
        Assert.False(today.IsPaused);

        today.QuietUntilTomorrowCommand.Execute(null);
        Assert.True(today.IsPaused);
        Assert.Equal("Reminders paused until tomorrow 10:00", today.NextText);

        today.ResumeRemindersCommand.Execute(null);
        Assert.False(today.IsPaused);
    }

    [AvaloniaFact]
    public void Quiet_until_a_chosen_time_uses_that_time()
    {
        using var ui = Harness(Clock());
        ui.Shell.Today.QuietTime = new TimeSpan(17, 30, 0);

        ui.Shell.Today.QuietUntilTimeCommand.Execute(null);

        Assert.Equal("Reminders paused until 17:30", ui.Shell.Today.NextText);
    }

    [AvaloniaFact]
    public void An_hour_of_quiet_ends_in_an_hour()
    {
        var clock = Clock();
        using var ui = Harness(clock);

        ui.Shell.Today.PauseHourCommand.Execute(null);

        Assert.Equal("Reminders paused until 13:00", ui.Shell.Today.NextText);
    }

    [AvaloniaFact]
    public void Pausing_from_anywhere_updates_the_quiet_card_and_the_status_bar()
    {
        using var ui = Harness(Clock());

        ui.Get<ReminderService>().Pause(TimeSpan.FromMinutes(30)); // as the tray menu or the status bar does

        Assert.True(ui.Shell.Today.IsPaused);
        Assert.True(ui.Shell.StatusBar.IsPaused);
    }

    [AvaloniaFact]
    public void The_quiet_card_shows_the_right_controls_for_each_state()
    {
        using var ui = Harness(Clock());
        ui.Show();

        bool Visible(string content) => ui.Window.GetVisualDescendants().OfType<Button>()
            .Any(button => Equals(button.Content, content) && button.IsEffectivelyVisible);

        Assert.True(Visible("Until tomorrow"));
        Assert.False(Visible("Resume reminders"));

        ui.Shell.Today.PauseHourCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(Visible("Until tomorrow"));
        Assert.True(Visible("Resume reminders"));
    }

    // ---- presets

    [AvaloniaFact]
    public void Choosing_a_preset_sets_the_schedule_and_highlights_it()
    {
        using var ui = Harness(Clock());
        var settings = ui.Shell.Settings;
        Assert.Equal("Every day", settings.Presets.Single(p => p.IsSelected).Name);

        settings.Presets.Single(p => p.Preset.Id == "office").SelectCommand.Execute(null);

        var saved = ui.Get<SettingsService>().Current;
        Assert.Equal(new TimeOnly(9, 0), saved.ActiveStart);
        Assert.Equal(new TimeOnly(18, 0), saved.ActiveEnd);
        Assert.Equal(5, saved.ActiveDays.Length);
        Assert.Equal("Office", settings.Presets.Single(p => p.IsSelected).Name);
        Assert.Equal(TimeSpan.FromHours(9), settings.ActiveStart);
        Assert.All(settings.Days.Where(day => day.Day is DayOfWeek.Saturday or DayOfWeek.Sunday), day => Assert.False(day.IsOn));
    }

    [AvaloniaFact]
    public void Customising_the_schedule_drops_the_highlight()
    {
        using var ui = Harness(Clock());
        var settings = ui.Shell.Settings;
        settings.Presets.Single(p => p.Preset.Id == "office").SelectCommand.Execute(null);

        settings.Days.Single(day => day.Day == DayOfWeek.Saturday).IsOn = true;

        Assert.DoesNotContain(settings.Presets, preset => preset.IsSelected);
    }

    [AvaloniaFact]
    public void A_preset_changes_only_the_schedule()
    {
        using var ui = Harness(Clock());
        ui.Get<SettingsService>().Update(s => s with { Name = "Kai", IntervalMinutes = 25 });

        ui.Shell.Settings.Presets.Single(p => p.Preset.Id == "nightowl").SelectCommand.Execute(null);

        var saved = ui.Get<SettingsService>().Current;
        Assert.Equal("Kai", saved.Name);
        Assert.Equal(25, saved.IntervalMinutes);
        Assert.Equal(new TimeOnly(2, 0), saved.ActiveEnd);
    }

    [AvaloniaFact]
    public void Presets_and_the_after_goal_switch_are_found_by_search()
    {
        using var ui = Harness(Clock());
        var settings = ui.Shell.Settings;

        settings.SearchText = "night owl";
        Assert.True(settings.ShowPresets);
        Assert.True(settings.ShowRemindersCard);
        Assert.False(settings.ShowInterval);

        settings.SearchText = "quieter";
        Assert.True(settings.ShowAfterGoal);
        Assert.False(settings.ShowPresets);
    }

    [AvaloniaFact]
    public void The_after_goal_switch_is_saved()
    {
        using var ui = Harness(Clock());
        Assert.True(ui.Shell.Settings.RemindLessAfterGoal);

        ui.Shell.Settings.RemindLessAfterGoal = false;

        Assert.False(ui.Get<SettingsService>().Current.RemindLessAfterGoal);
    }

    // ---- the command palette

    private static IReadOnlyList<string> Titles(UiHarness ui, string query)
    {
        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = query;
        return ui.Shell.Palette.Results.Select(result => result.Title).ToList();
    }

    [AvaloniaFact]
    public void The_palette_offers_every_preset_except_the_one_in_use()
    {
        using var ui = Harness(Clock());

        var titles = Titles(ui, "Schedule:");

        Assert.Contains("Schedule: Office", titles);
        Assert.Contains("Schedule: Night owl", titles);
        Assert.DoesNotContain("Schedule: Every day", titles);
    }

    [AvaloniaFact]
    public void A_preset_chosen_in_the_palette_is_applied()
    {
        using var ui = Harness(Clock());
        Titles(ui, "Schedule: Weekends off");

        ui.Shell.Palette.Results.First().RunCommand.Execute(null);

        Assert.Equal("weekends-off", WorkHoursPresets.Matching(ui.Get<SettingsService>().Current)?.Id);
    }

    [AvaloniaFact]
    public void The_palette_offers_quiet_times_that_are_still_ahead_and_only_while_not_paused()
    {
        using var ui = Harness(Clock(hour: 16));

        var titles = Titles(ui, "Quiet until").Where(title => title.StartsWith("Quiet until", StringComparison.Ordinal)).ToList();
        Assert.Contains("Quiet until tomorrow", titles);
        Assert.Contains("Quiet until 18:00", titles);
        Assert.Contains("Quiet until 20:00", titles);
        Assert.DoesNotContain("Quiet until 13:00", titles);
        Assert.DoesNotContain("Quiet until 15:00", titles);

        ui.Get<ReminderService>().Pause(null);
        Assert.DoesNotContain(Titles(ui, "Quiet until"), title => title.StartsWith("Quiet until", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_quiet_time_chosen_in_the_palette_pauses_until_then()
    {
        using var ui = Harness(Clock());
        Titles(ui, "Quiet until 18:00");

        ui.Shell.Palette.Results.First().RunCommand.Execute(null);

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 18, 0, 0, TimeSpan.Zero), ui.Get<ReminderService>().Status().PausedUntil);
    }
}
