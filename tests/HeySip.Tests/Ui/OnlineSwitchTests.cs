using Avalonia.Headless.XUnit;
using HeySip.App.Features.Browse;
using HeySip.Core.Features.Settings;

namespace HeySip.Tests.Ui;

public sealed class OnlineSwitchTests
{
    private static UiHarness Harness()
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        return ui;
    }

    [AvaloniaFact]
    public void The_switch_starts_on_and_saves_what_the_user_chooses()
    {
        using var ui = Harness();
        var settings = ui.Shell.Settings;
        Assert.True(settings.OnlineOn);
        Assert.True(ui.Get<SettingsService>().Current.OnlineCharacters);

        settings.OnlineOn = false;

        Assert.False(ui.Get<SettingsService>().Current.OnlineCharacters);

        settings.OnlineOn = true;

        Assert.True(ui.Get<SettingsService>().Current.OnlineCharacters);
    }

    [AvaloniaFact]
    public void The_page_follows_a_change_made_elsewhere()
    {
        using var ui = Harness();

        ui.Get<SettingsService>().Update(s => s with { OnlineCharacters = false });

        Assert.False(ui.Shell.Settings.OnlineOn);
    }

    [AvaloniaFact]
    public void Changing_the_switch_does_not_disturb_the_other_settings()
    {
        using var ui = Harness();
        ui.Get<SettingsService>().Update(s => s with { Name = "Kai", SoundVolume = 12, Character = "heysip.minty" });

        ui.Shell.Settings.OnlineOn = false;

        var current = ui.Get<SettingsService>().Current;
        Assert.Equal("Kai", current.Name);
        Assert.Equal(12, current.SoundVolume);
        Assert.Equal("heysip.minty", current.Character);
    }

    [AvaloniaFact]
    public void It_can_be_found_by_searching_and_is_hidden_from_unrelated_searches()
    {
        using var ui = Harness();

        foreach (var word in new[] { "online", "internet", "download", "privacy", "store" })
        {
            ui.Shell.OpenSettings(word);
            Assert.True(ui.Shell.Settings.ShowOnline, word);
            Assert.True(ui.Shell.Settings.ShowCompanionCard, word);
        }

        ui.Shell.OpenSettings("interval");
        Assert.False(ui.Shell.Settings.ShowOnline);
    }

    [AvaloniaFact]
    public void The_palette_can_jump_to_it()
    {
        using var ui = Harness();

        ui.Shell.Palette.Open();
        ui.Shell.Palette.Query = "check online for updates";
        ui.Shell.Palette.RunSelected();

        Assert.True(ui.Shell.IsSettings);
        Assert.True(ui.Shell.Settings.ShowOnline);
    }

    [AvaloniaFact]
    public void The_note_says_what_is_contacted_and_what_is_sent()
    {
        using var ui = Harness();

        var note = ui.Shell.Settings.OnlineNote;
        Assert.Contains("updates and notices", note);
        Assert.Contains("only the app version", note);
        Assert.Contains("nothing is contacted", note);
    }

    [AvaloniaFact]
    public void The_settings_page_draws_with_the_switch()
    {
        using var ui = Harness();
        ui.Show();

        ui.Shell.OpenSettings("online");
        ui.Show();

        Assert.True(ui.Shell.Settings.ShowOnline);
    }
}
