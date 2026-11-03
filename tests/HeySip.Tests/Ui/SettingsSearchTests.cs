using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HeySip.App.Features.Settings;
using HeySip.App.Shell;
using HeySip.Core.Features.Settings;

namespace HeySip.Tests.Ui;

public sealed class SettingsSearchTests
{
    private static UiHarness Harness()
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        return ui;
    }

    // ---- the catalog and the view model stay in step

    [Fact]
    public void Every_setting_in_the_catalog_has_a_unique_key_and_something_to_search_on()
    {
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(entry => entry.Key).Distinct().Count());
        Assert.All(SettingsCatalog.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.Keywords));
        });
    }

    [Fact]
    public void Every_catalog_entry_has_a_matching_Show_property_for_the_page_to_bind()
    {
        var properties = typeof(SettingsViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);

        Assert.All(SettingsCatalog.All, entry =>
        {
            Assert.True(properties.TryGetValue("Show" + entry.Key, out var property), $"SettingsViewModel is missing Show{entry.Key}.");
            Assert.Equal(typeof(bool), property!.PropertyType);
        });
    }

    [Theory]
    [InlineData("Name", true)]
    [InlineData("NoSuchSetting", false)]
    public void Looking_up_a_key_that_does_not_exist_is_an_error_not_a_silent_match(string key, bool exists)
    {
        if (exists)
        {
            Assert.True(SettingsCatalog.Matches(key, ""));
        }
        else
        {
            Assert.Throws<KeyNotFoundException>(() => SettingsCatalog.Matches(key, ""));
        }
    }

    // ---- filtering

    [AvaloniaFact]
    public void With_no_search_everything_is_shown()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        Assert.True(settings.ShowAboutCard && settings.ShowRemindersCard && settings.ShowCompanionCard);
        Assert.True(settings.ShowAppCard && settings.ShowHelpCard && settings.ShowDataCard);
        Assert.False(settings.NoResults);
    }

    [AvaloniaFact]
    public void A_search_shows_only_the_matching_setting_and_its_card()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        settings.SearchText = "interval";

        Assert.True(settings.ShowInterval);
        Assert.False(settings.ShowSnooze);
        Assert.False(settings.ShowName);
        Assert.True(settings.ShowRemindersCard);
        Assert.False(settings.ShowAboutCard);
        Assert.False(settings.ShowAppCard);
        Assert.False(settings.ShowHelpCard);
        Assert.False(settings.NoResults);
    }

    [AvaloniaFact]
    public void Several_words_must_all_match()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        settings.SearchText = "snooze minutes";
        Assert.True(settings.ShowSnooze);

        settings.SearchText = "snooze zebra";
        Assert.False(settings.ShowSnooze);
        Assert.True(settings.NoResults);
    }

    [AvaloniaFact]
    public void Searching_ignores_case_and_finds_things_by_the_words_people_actually_use()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        settings.SearchText = "THEME";
        Assert.True(settings.ShowTheme);

        settings.SearchText = "mascot";           // a keyword, not in the title
        Assert.True(settings.ShowCharacter);
        Assert.False(settings.ShowMessages);

        settings.SearchText = "autostart";
        Assert.Equal(settings.StartupSupported, settings.ShowStartup);

        settings.SearchText = "idle";             // found through its description
        Assert.True(settings.ShowAway);
    }

    [AvaloniaFact]
    public void Settings_that_share_a_row_hide_the_row_only_when_both_are_hidden()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        settings.SearchText = "goal";
        Assert.True(settings.ShowGoal);
        Assert.False(settings.ShowGlass);
        Assert.True(settings.ShowAmountsRow);

        settings.SearchText = "monitor";
        Assert.True(settings.ShowStyleRow);

        settings.SearchText = "theme";
        Assert.False(settings.ShowAmountsRow);
        Assert.False(settings.ShowTimingRow);
        Assert.False(settings.ShowStyleRow);
    }

    [AvaloniaFact]
    public void A_search_with_no_matches_says_so_and_clearing_it_restores_everything()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;

        settings.SearchText = "qqqq";
        Assert.True(settings.NoResults);
        Assert.False(settings.ShowRemindersCard);

        settings.ClearSearchCommand.Execute(null);

        Assert.Equal("", settings.SearchText);
        Assert.False(settings.NoResults);
        Assert.True(settings.ShowRemindersCard);
    }

    [AvaloniaFact]
    public void Searching_never_saves_or_changes_a_setting()
    {
        using var ui = Harness();
        var service = ui.Get<SettingsService>();
        service.Update(s => s with { IntervalMinutes = 30, Name = "Alex" });
        var saves = 0;
        service.Changed += (_, _) => saves++;

        foreach (var text in new[] { "i", "in", "int", "interval", "qqq", "" })
        {
            ui.Shell.Settings.SearchText = text;
        }

        Assert.Equal(0, saves);
        Assert.Equal(30, service.Current.IntervalMinutes);
        Assert.Equal("Alex", service.Current.Name);
        Assert.Equal(30m, ui.Shell.Settings.IntervalMinutes);
    }

    [AvaloniaFact]
    public void Settings_still_save_normally_while_a_search_is_active()
    {
        using var ui = Harness();
        ui.Shell.Settings.SearchText = "interval";

        ui.Shell.Settings.IntervalMinutes = 20;

        Assert.Equal(20, ui.Get<SettingsService>().Current.IntervalMinutes);
    }

    // ---- what the page actually draws

    [AvaloniaFact]
    public void The_page_draws_only_the_cards_that_match()
    {
        using var ui = Harness();
        ui.Shell.OpenSettings("theme");
        ui.Show();

        var visibleCards = ui.Window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("card") && border.IsEffectivelyVisible)
            .ToList();

        Assert.Single(visibleCards);

        ui.Shell.Settings.ClearSearchCommand.Execute(null);
        ui.Show();
        var allCards = ui.Window.GetVisualDescendants().OfType<Border>()
            .Count(border => border.Classes.Contains("card") && border.IsEffectivelyVisible);
        Assert.Equal(6, allCards);
    }

    [AvaloniaFact]
    public void Going_to_Settings_from_the_palette_or_a_shortcut_starts_from_everything_while_a_named_setting_filters()
    {
        using var ui = Harness();

        ui.Shell.OpenSettings("Reminder interval");
        Assert.True(ui.Shell.Settings.ShowInterval);
        Assert.False(ui.Shell.Settings.ShowName);
        Assert.IsType<SettingsViewModel>(ui.Shell.Current);

        ui.Shell.OpenSettings(null);
        Assert.Equal("", ui.Shell.Settings.SearchText);
        Assert.True(ui.Shell.Settings.ShowName);
    }
}
