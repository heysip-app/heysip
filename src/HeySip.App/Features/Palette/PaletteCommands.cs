using HeySip.App.Features.Settings;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Diagnostics;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Palette;

/// <summary>Builds the list of things the command palette can do. Everything here already exists elsewhere in the app.</summary>
public sealed class PaletteCommands(
    HydrationService hydration,
    SettingsService settings,
    ReminderService reminders,
    DiagnosticsService diagnostics,
    IClipboardService clipboard,
    IShellLauncher launcher,
    TimeProvider time)
{
    /// <summary>Set by the app: shuts HeySip down completely.</summary>
    public Action? Quit { get; set; }

    public IReadOnlyList<PaletteCommand> Build(IShellNavigator shell)
    {
        var commands = new List<PaletteCommand>();
        var current = settings.Current;

        // Logging drinks
        commands.Add(new("log.glass", "Log", $"Log a glass ({current.GlassMilliliters} ml)", () => hydration.LogGlass(), "", "drink water sip add"));
        foreach (var amount in new[] { 150, 330, 500 })
        {
            var ml = amount;
            commands.Add(new($"log.{ml}", "Log", $"Log {ml} ml", () => hydration.Log(ml), "", "drink water add amount"));
        }

        commands.Add(new("log.undo", "Log", "Undo last drink", UndoLast, "", "remove delete mistake",
            () => hydration.Today().Count > 0));

        // Reminders
        commands.Add(new("remind.now", "Reminders", "Show a reminder now", reminders.Trigger, "", "test preview nudge"));
        foreach (var (title, span) in new (string, TimeSpan?)[]
        {
            ("Pause for 30 minutes", TimeSpan.FromMinutes(30)),
            ("Pause for 1 hour", TimeSpan.FromHours(1)),
            ("Pause for 2 hours", TimeSpan.FromHours(2)),
            ("Pause until I resume", null),
        })
        {
            var duration = span;
            commands.Add(new($"pause.{title}", "Reminders", title, () => reminders.Pause(duration), "", "snooze mute quiet stop",
                () => !reminders.Status().IsPaused));
        }

        commands.Add(new("quiet.tomorrow", "Reminders", "Quiet until tomorrow", reminders.PauseUntilTomorrow, "", "pause mute silence rest of day night peace",
            () => !reminders.Status().IsPaused));
        foreach (var hour in new[] { 13, 15, 18, 20 }.Where(hour => LocalNow().Hour < hour))
        {
            var clock = new TimeOnly(hour, 0);
            commands.Add(new($"quiet.{hour}", "Reminders", $"Quiet until {clock:HH:mm}", () => reminders.PauseUntil(clock), "", "pause mute silence peace",
                () => !reminders.Status().IsPaused));
        }

        commands.Add(new("remind.resume", "Reminders", "Resume reminders", reminders.Resume, "", "unpause continue start",
            () => reminders.Status().IsPaused));

        // Ready-made schedules (the one in use is left out, since choosing it would do nothing)
        foreach (var preset in WorkHoursPresets.All.Where(preset => !preset.Matches(current)))
        {
            var choice = preset;
            commands.Add(new($"schedule.{choice.Id}", "Schedule", $"Schedule: {choice.Name}", () => settings.Update(choice.Apply), "",
                $"{choice.Describe(current)} hours days"));
        }

        // Moving around
        commands.Add(new("go.today", "Go to", "Today", () => shell.Navigate(ShellPage.Today), "Ctrl+1", "home page"));
        commands.Add(new("go.stats", "Go to", "Stats", () => shell.Navigate(ShellPage.Stats), "Ctrl+2", "chart streak average"));
        commands.Add(new("go.history", "Go to", "History", () => shell.Navigate(ShellPage.History), "Ctrl+3", "log list past days drinks older earlier date"));
        commands.Add(new("go.settings", "Go to", "Settings", () => shell.OpenSettings(null), "Ctrl+4", "preferences options"));
        var today = Today();
        commands.Add(new("go.todaylog", "Go to", "Today's log", () => shell.OpenDay(today), "", "day drinks backfill add forgotten"));
        commands.Add(new("go.yesterday", "Go to", "Yesterday's log", () => shell.OpenDay(today.AddDays(-1)), "", "day drinks backfill add forgotten"));

        commands.Add(new("go.welcome", "Go to", "Show welcome again", shell.ShowWelcome, "", "onboarding intro first run tour name"));

        // The window
        commands.Add(new("view.close", "View", "Close tab", shell.CloseCurrentTab, "Ctrl+W", ""));
        commands.Add(new("view.next", "View", "Next tab", shell.NextTab, "Ctrl+Tab", ""));
        commands.Add(new("view.previous", "View", "Previous tab", shell.PreviousTab, "Ctrl+Shift+Tab", ""));

        // Appearance: the current value is left out, since choosing it would do nothing.
        foreach (var theme in Enum.GetValues<ThemePreference>().Where(theme => theme != current.Theme))
        {
            var choice = theme;
            commands.Add(new($"theme.{choice}", "Appearance", $"Theme: {choice}", () => settings.Update(s => s with { Theme = choice }), "", "colour color mode appearance"));
        }

        foreach (var character in CharacterCatalog.All.Where(character => character.Id != current.Character))
        {
            var choice = character;
            commands.Add(new($"character.{choice.Id}", "Appearance", $"Character: {choice.Name}", () => settings.Update(s => s with { Character = choice.Id }), "", "mascot companion look"));
        }

        commands.Add(new("sound.mute", "Appearance", "Mute sounds", () => settings.Update(s => s with { SoundEnabled = false }), "", "silence quiet volume off audio chime gulp",
            () => settings.Current.SoundEnabled));
        commands.Add(new("sound.unmute", "Appearance", "Turn sounds on", () => settings.Update(s => s with { SoundEnabled = true }), "", "unmute volume audio chime gulp",
            () => !settings.Current.SoundEnabled));
        commands.Add(new("browse.characters", "Appearance", "Browse characters and accessories", () => shell.Navigate(ShellPage.Browse), "Ctrl+5", "store shop find get new download more characters accessories packs"));
        commands.Add(new("install.file", "Appearance", "Install a character or accessory from a file...", shell.InstallFromFile, "", "pack heysippack download add new open import"));

        // Outfit: put on what isn't worn yet, and take off what is.
        foreach (var accessory in CharacterCatalog.Accessories.Where(accessory => current.Outfit.GetValueOrDefault(OutfitSlots.KeyOf(accessory.Slot)) != accessory.Id))
        {
            var choice = accessory;
            commands.Add(new($"wear.{choice.Id}", "Appearance", $"Wear: {choice.Name}", () => settings.Update(s => s.WithAccessory(choice.Slot, choice.Id)), "",
                $"outfit accessory dress up {OutfitSlots.TitleOf(choice.Slot)} {choice.Slot}"));
        }

        foreach (var slot in OutfitSlots.All)
        {
            if (CharacterCatalog.FindAccessory(current.Outfit.GetValueOrDefault(OutfitSlots.KeyOf(slot))) is { } worn && worn.Slot == slot)
            {
                var removing = slot;
                commands.Add(new($"wear.off.{OutfitSlots.KeyOf(slot)}", "Appearance", $"Take off: {worn.Name}", () => settings.Update(s => s.WithAccessory(removing, null)), "",
                    $"outfit accessory remove undress {OutfitSlots.TitleOf(slot)}"));
            }
        }

        foreach (var style in Enum.GetValues<MessageStyle>().Where(style => style != current.Messages))
        {
            var choice = style;
            commands.Add(new($"style.{choice}", "Appearance", $"Message style: {choice}", () => settings.Update(s => s with { Messages = choice }), "", "tone talk personality"));
        }

        // Every setting is reachable by name: choosing one opens Settings, filtered to it.
        foreach (var entry in SettingsCatalog.All)
        {
            var setting = entry;
            commands.Add(new($"setting.{setting.Key}", "Settings", setting.Title, () => shell.OpenSettings(setting.Title), "", $"{setting.Description} {setting.Keywords}"));
        }

        // Help and exit
        commands.Add(new("help.diagnostics", "Help", "Copy diagnostics", () => _ = CopyDiagnosticsAsync(), "", "report bug logs"));
        commands.Add(new("help.folder", "Help", "Open data folder", () => launcher.OpenFolder(diagnostics.DataFolder), "", "files database logs"));
        commands.Add(new("app.quit", "App", "Quit HeySip", () => Quit?.Invoke(), "", "exit close stop"));

        return commands;
    }

    /// <summary>Commands made from what the user typed: a date (such as 2026-03-08 or 8 Mar) opens that day.</summary>
    public IEnumerable<PaletteCommand> ForQuery(IShellNavigator shell, string query)
    {
        var today = Today();
        if (DateOnly.TryParse(query, System.Globalization.CultureInfo.CurrentCulture, out var day) && day <= today && day.Year >= 2000)
        {
            yield return new PaletteCommand($"go.date.{day:yyyy-MM-dd}", "Go to", $"Day: {DayLabel.Long(day)}", () => shell.OpenDay(day));
        }
    }

    private void UndoLast()
    {
        var entries = hydration.Today();
        if (entries.Count > 0)
        {
            hydration.Remove(entries[^1].Id);
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            await clipboard.SetTextAsync(diagnostics.BuildReport());
        }
        catch (InvalidOperationException)
        {
            // The clipboard was busy; there is nowhere useful to report this from a palette command.
        }
    }

    private DateTime LocalNow() => ZoneResolver.LocalNow(ZoneResolver.Resolve(settings.Current.TimeZoneId, time), time);

    private DateOnly Today() =>
        ZoneResolver.LocalDate(time.GetUtcNow(), ZoneResolver.Resolve(settings.Current.TimeZoneId, time));
}
