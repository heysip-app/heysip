using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeySip.App.Features.Day;
using HeySip.App.Features.Palette;
using HeySip.App.Features.Settings;
using HeySip.App.Features.Stats;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Ui;

public sealed class CommandPaletteTests
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

    // ---- the palette itself, with simple made-up commands

    private static (CommandPaletteViewModel Palette, List<string> Ran) Simple(bool resumeAvailable = false)
    {
        var ran = new List<string>();
        PaletteCommand Make(string id, string category, string title, Func<bool>? available = null, string keywords = "") =>
            new(id, category, title, () => ran.Add(id), "", keywords, available);

        var palette = new CommandPaletteViewModel(() =>
        [
            Make("glass", "Log", "Log a glass"),
            Make("today", "Go to", "Today"),
            Make("pause", "Reminders", "Pause for 1 hour", keywords: "snooze mute"),
            Make("resume", "Reminders", "Resume reminders", () => resumeAvailable),
        ]);
        return (palette, ran);
    }

    [Fact]
    public void Opening_lists_every_available_command_in_order_and_hides_the_rest()
    {
        var (palette, _) = Simple(resumeAvailable: false);

        palette.Open();

        Assert.True(palette.IsOpen);
        Assert.Equal(["Log a glass", "Today", "Pause for 1 hour"], palette.Results.Select(item => item.Title));
        Assert.Equal(0, palette.SelectedIndex);
        Assert.True(palette.Results[0].IsSelected);
        Assert.False(palette.HasNoResults);
    }

    [Fact]
    public void Availability_is_checked_again_every_time_it_opens()
    {
        var available = false;
        var palette = new CommandPaletteViewModel(() => [new PaletteCommand("r", "Reminders", "Resume", () => { }, IsAvailable: () => available)]);

        palette.Open();
        Assert.Empty(palette.Results);

        palette.Close();
        available = true;
        palette.Open();
        Assert.Single(palette.Results);
    }

    [Fact]
    public void Typing_filters_and_ranks_the_best_match_first()
    {
        var (palette, _) = Simple();
        palette.Open();

        palette.Query = "gl";

        Assert.Equal(["Log a glass"], palette.Results.Select(item => item.Title));

        palette.Query = "snooze"; // found through its keywords, not its title
        Assert.Equal(["Pause for 1 hour"], palette.Results.Select(item => item.Title));
    }

    [Fact]
    public void A_query_that_matches_nothing_says_so_and_running_does_nothing()
    {
        var (palette, ran) = Simple();
        palette.Open();

        palette.Query = "zzzzz";
        palette.RunSelected();

        Assert.True(palette.HasNoResults);
        Assert.Empty(palette.Results);
        Assert.Empty(ran);
        Assert.True(palette.IsOpen);
    }

    [Fact]
    public void The_arrow_keys_wrap_around_the_list()
    {
        var (palette, _) = Simple();
        palette.Open();

        palette.MovePrevious();
        Assert.Equal(2, palette.SelectedIndex);
        Assert.True(palette.Results[2].IsSelected);
        Assert.False(palette.Results[0].IsSelected);

        palette.MoveNext();
        Assert.Equal(0, palette.SelectedIndex);
    }

    [Fact]
    public void Changing_the_query_puts_the_highlight_back_on_the_first_result()
    {
        var (palette, _) = Simple();
        palette.Open();
        palette.MoveNext();
        palette.MoveNext();

        palette.Query = "o";

        Assert.Equal(0, palette.SelectedIndex);
        Assert.Single(palette.Results, item => item.IsSelected);
        Assert.True(palette.Results[0].IsSelected);
    }

    [Fact]
    public void Running_the_selected_command_closes_the_palette_first_and_then_runs_it()
    {
        var wasOpenWhenRun = true;
        CommandPaletteViewModel? palette = null;
        palette = new CommandPaletteViewModel(() => [new PaletteCommand("x", "Test", "Do it", () => wasOpenWhenRun = palette!.IsOpen)]);
        palette.Open();

        palette.RunSelected();

        Assert.False(wasOpenWhenRun); // closed before running, so a command may open something else
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void Moving_to_a_row_and_running_runs_that_row()
    {
        var (palette, ran) = Simple();
        palette.Open();
        palette.MoveNext(); // "Today"

        palette.RunSelected();

        Assert.Equal(["today"], ran);
    }

    [Fact]
    public void A_clicked_row_runs_its_own_command()
    {
        var (palette, ran) = Simple();
        palette.Open();

        palette.Results[2].RunCommand.Execute(null);

        Assert.Equal(["pause"], ran);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void Reopening_starts_with_an_empty_search()
    {
        var (palette, _) = Simple();
        palette.Open();
        palette.Query = "gl";
        palette.Close();

        palette.Open();

        Assert.Equal("", palette.Query);
        Assert.Equal(3, palette.Results.Count);
    }

    // ---- the real commands

    private static UiHarness Harness(FakeDesktop? desktop = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var ui = UiHarness.Create(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            if (desktop is not null)
            {
                services.AddSingleton<IClipboardService>(desktop);
                services.AddSingleton<IShellLauncher>(desktop);
            }
        });
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<ReminderService>().Restart();
        return ui;
    }

    private static IReadOnlyList<PaletteCommand> Commands(UiHarness ui) => ui.Get<PaletteCommands>().Build(ui.Shell);

    private static PaletteCommand Find(UiHarness ui, string id) => Commands(ui).Single(command => command.Id == id);

    [AvaloniaFact]
    public void Every_command_has_a_unique_id_a_category_and_a_title()
    {
        using var ui = Harness();
        var commands = Commands(ui);

        Assert.Equal(commands.Count, commands.Select(command => command.Id).Distinct().Count());
        Assert.All(commands, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Category));
            Assert.False(string.IsNullOrWhiteSpace(command.Title));
        });
    }

    [AvaloniaFact]
    public void Logging_commands_add_drinks()
    {
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();

        Find(ui, "log.glass").Run();
        Find(ui, "log.500").Run();
        Find(ui, "log.150").Run();

        Assert.Equal(250 + 500 + 150, hydration.TodayMilliliters());
        Assert.Equal("Log a glass (250 ml)", Find(ui, "log.glass").Title);
    }

    [AvaloniaFact]
    public void The_glass_command_follows_the_glass_size_setting()
    {
        using var ui = Harness();
        ui.Get<SettingsService>().Update(s => s with { GlassMilliliters = 330 });

        Assert.Equal("Log a glass (330 ml)", Find(ui, "log.glass").Title);
        Find(ui, "log.glass").Run();
        Assert.Equal(330, ui.Get<HydrationService>().TodayMilliliters());
    }

    [AvaloniaFact]
    public void Undo_is_only_offered_when_there_is_something_to_undo_and_removes_the_latest_drink()
    {
        using var ui = Harness();
        var hydration = ui.Get<HydrationService>();
        Assert.False(Find(ui, "log.undo").Available);

        hydration.Log(100);
        hydration.Log(400);
        Assert.True(Find(ui, "log.undo").Available);

        Find(ui, "log.undo").Run();

        Assert.Equal(100, hydration.TodayMilliliters());
    }

    [AvaloniaFact]
    public void Pause_and_resume_are_each_offered_only_when_they_make_sense()
    {
        using var ui = Harness();
        var reminders = ui.Get<ReminderService>();
        Assert.True(Find(ui, "pause.Pause for 1 hour").Available);
        Assert.False(Find(ui, "remind.resume").Available);

        Find(ui, "pause.Pause for 1 hour").Run();

        Assert.True(reminders.Status().IsPaused);
        Assert.False(Find(ui, "pause.Pause for 1 hour").Available);
        Assert.True(Find(ui, "remind.resume").Available);

        Find(ui, "remind.resume").Run();
        Assert.False(reminders.Status().IsPaused);
    }

    [AvaloniaFact]
    public void The_pause_until_resumed_command_has_no_end_time()
    {
        using var ui = Harness();

        Find(ui, "pause.Pause until I resume").Run();

        Assert.True(ui.Get<ReminderService>().Status().IsPaused);
        Assert.Equal(DateTimeOffset.MaxValue, ui.Get<ReminderService>().Status().PausedUntil);
    }

    [AvaloniaFact]
    public void Show_a_reminder_now_asks_for_a_manual_reminder()
    {
        using var ui = Harness();
        Reminder? shown = null;
        ui.Get<ReminderService>().Due += reminder => shown = reminder;

        Find(ui, "remind.now").Run();

        Assert.NotNull(shown);
        Assert.True(shown.Manual);
    }

    [AvaloniaFact]
    public void The_go_to_commands_open_the_right_tabs()
    {
        using var ui = Harness();

        Find(ui, "go.stats").Run();
        Assert.IsType<StatsViewModel>(ui.Shell.Current);

        Find(ui, "go.todaylog").Run();
        Assert.Equal("Today's log", ui.Shell.SelectedTab.Title);
        Assert.IsType<DayViewModel>(ui.Shell.Current);

        Find(ui, "go.yesterday").Run();
        Assert.Equal("Yesterday", ui.Shell.SelectedTab.Title);

        Find(ui, "go.settings").Run();
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);

        Find(ui, "go.today").Run();
        Assert.False(ui.Shell.SelectedTab.IsClosable);
    }

    [AvaloniaFact]
    public void The_navigation_commands_show_the_shortcuts_that_do_the_same_thing()
    {
        using var ui = Harness();

        Assert.Equal("Ctrl+1", Find(ui, "go.today").Shortcut);
        Assert.Equal("Ctrl+2", Find(ui, "go.stats").Shortcut);
        Assert.Equal("Ctrl+3", Find(ui, "go.history").Shortcut);
        Assert.Equal("Ctrl+4", Find(ui, "go.settings").Shortcut);
        Assert.Equal("Ctrl+W", Find(ui, "view.close").Shortcut);
    }

    [AvaloniaFact]
    public void The_view_commands_cycle_and_close_tabs()
    {
        using var ui = Harness();
        ui.Shell.ShowStatsCommand.Execute(null);

        Find(ui, "view.next").Run();
        Assert.False(ui.Shell.SelectedTab.IsClosable); // wrapped round to Today

        Find(ui, "view.previous").Run();
        Assert.Equal("Stats", ui.Shell.SelectedTab.Title);

        Find(ui, "view.close").Run();
        Assert.Single(ui.Shell.Tabs);
    }

    [AvaloniaFact]
    public void Appearance_commands_change_the_setting_and_leave_out_the_current_choice()
    {
        using var ui = Harness();
        Assert.DoesNotContain(Commands(ui), command => command.Id == "theme.System");   // already System
        Assert.DoesNotContain(Commands(ui), command => command.Id == "character.heysip.drip"); // already Drip
        Assert.DoesNotContain(Commands(ui), command => command.Id == "style.Friendly"); // already Friendly

        Find(ui, "theme.Dark").Run();
        Find(ui, "character.heysip.minty").Run();
        Find(ui, "style.Coach").Run();

        var settings = ui.Get<SettingsService>().Current;
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal("heysip.minty", settings.Character);
        Assert.Equal(MessageStyle.Coach, settings.Messages);

        // Now Dark is the current choice, so it is no longer offered, and System is.
        Assert.DoesNotContain(Commands(ui), command => command.Id == "theme.Dark");
        Assert.Contains(Commands(ui), command => command.Id == "theme.System");
    }

    [AvaloniaFact]
    public void Every_setting_can_be_reached_by_name_and_opens_settings_filtered_to_it()
    {
        using var ui = Harness();
        var commands = Commands(ui);
        Assert.All(SettingsCatalog.All, entry => Assert.Contains(commands, command => command.Id == $"setting.{entry.Key}"));

        Find(ui, "setting.Interval").Run();

        Assert.IsType<SettingsViewModel>(ui.Shell.Current);
        Assert.Equal("Reminder interval", ui.Shell.Settings.SearchText);
        Assert.True(ui.Shell.Settings.ShowInterval);
        Assert.False(ui.Shell.Settings.ShowName);
    }

    [AvaloniaFact]
    public void Quit_calls_back_to_the_app_and_does_nothing_if_nobody_is_listening()
    {
        using var ui = Harness();
        var palette = ui.Get<PaletteCommands>();

        Assert.Null(Record.Exception(() => Find(ui, "app.quit").Run()));

        var quits = 0;
        palette.Quit = () => quits++;
        Find(ui, "app.quit").Run();
        Assert.Equal(1, quits);
    }

    [AvaloniaFact]
    public void The_help_commands_copy_diagnostics_and_open_the_data_folder()
    {
        var desktop = new FakeDesktop();
        using var ui = Harness(desktop);
        ui.Get<SettingsService>().Update(s => s with { Name = "Private-Name" });

        Find(ui, "help.diagnostics").Run();
        Find(ui, "help.folder").Run();

        Assert.StartsWith("HeySip diagnostics", desktop.Copied);
        Assert.DoesNotContain("Private-Name", desktop.Copied);
        Assert.Equal(ui.Data.Root, desktop.OpenedFolder);
    }

    // ---- searching through the shell

    [AvaloniaFact]
    public void Typing_a_few_letters_finds_the_expected_command_first()
    {
        using var ui = Harness();
        var palette = ui.Shell.Palette;
        palette.Open();

        palette.Query = "interval";
        Assert.Equal("Reminder interval", palette.Results[0].Title);

        palette.Query = "dark";
        Assert.Equal("Theme: Dark", palette.Results[0].Title);

        palette.Query = "pause";
        Assert.All(palette.Results.Take(4), item => Assert.StartsWith("Pause", item.Title));

        palette.Query = "500";
        Assert.Equal("Log 500 ml", palette.Results[0].Title);
    }

    [AvaloniaFact]
    public void Resume_only_turns_up_in_the_palette_while_reminders_are_paused()
    {
        using var ui = Harness();
        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "resume";
        Assert.DoesNotContain(ui.Shell.Palette.Results, item => item.Title == "Resume reminders");

        ui.Get<ReminderService>().Pause(TimeSpan.FromHours(1));
        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "resume";
        Assert.Contains(ui.Shell.Palette.Results, item => item.Title == "Resume reminders");
    }

    [AvaloniaFact]
    public void Choosing_a_command_by_typing_and_pressing_Enter_works_end_to_end()
    {
        using var ui = Harness();
        ui.Shell.Palette.Open();

        ui.Shell.Palette.Query = "log 500";
        ui.Shell.Palette.RunSelected();

        Assert.False(ui.Shell.Palette.IsOpen);
        Assert.Equal(500, ui.Get<HydrationService>().TodayMilliliters());
    }

    // ---- the ways in

    [AvaloniaFact]
    public void Ctrl_Shift_P_and_F1_open_the_palette_and_Escape_closes_it()
    {
        using var ui = Harness();
        ui.Show();

        ui.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ui.Shell.Palette.IsOpen);

        ui.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ui.Shell.Palette.IsOpen);

        ui.Window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ui.Shell.Palette.IsOpen);
    }

    [AvaloniaFact]
    public void The_status_bar_has_a_Commands_button_for_people_who_do_not_know_the_shortcut()
    {
        using var ui = Harness();

        ui.Shell.StatusBar.ShowPaletteCommand.Execute(null);

        Assert.True(ui.Shell.Palette.IsOpen);
    }

    [AvaloniaFact]
    public void The_palette_is_only_visible_while_it_is_open()
    {
        using var ui = Harness();
        ui.Show();
        var view = ui.Window.GetVisualDescendants().OfType<PaletteView>().Single();
        Assert.False(view.IsEffectivelyVisible);

        ui.Shell.Palette.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.IsEffectivelyVisible);

        ui.Shell.Palette.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(view.IsEffectivelyVisible);
    }
}
