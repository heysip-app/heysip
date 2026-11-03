using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HeySip.App.Features.Settings;
using HeySip.App.Shared.Components;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;

namespace HeySip.Tests.Ui;

/// <summary>The shared components that pages are built from.</summary>
public sealed class ComponentTests
{
    private static string? ShotPath(string name) =>
        Environment.GetEnvironmentVariable("HEYSIP_SHOTS") is { Length: > 0 } folder ? Path.Combine(folder, name) : null;

    [AvaloniaFact]
    public void Onboarding_shows_every_character_as_a_pick_sized_for_the_welcome_card()
    {
        using var ui = UiHarness.Create();
        ui.Shell.Onboarding.ShowIfFirstRun();
        ui.Shell.Onboarding.NextCommand.Execute(null);
        ui.Shell.Onboarding.NextCommand.Execute(null);
        ui.Show();

        var picks = ui.Window.GetVisualDescendants().OfType<CharacterPick>().Where(pick => pick.IsEffectivelyVisible).ToList();

        Assert.Equal(HeySip.App.Shared.Controls.CharacterCatalog.All.Count, picks.Count);
        Assert.All(picks, pick => Assert.Equal((64d, 90d), (pick.PreviewWidth, pick.PreviewHeight)));
        if (ShotPath("onboarding-characters.png") is { } path)
        {
            ui.Screenshot(path);
        }
    }

    [AvaloniaFact]
    public void Settings_uses_the_default_pick_size_and_selecting_a_pick_changes_the_character()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Shell.Navigate(HeySip.App.Shell.ShellPage.Settings);
        ui.Show();

        var picks = ui.Window.GetVisualDescendants().OfType<CharacterPick>().ToList();
        Assert.NotEmpty(picks);
        Assert.All(picks, pick => Assert.Equal((70d, 98d), (pick.PreviewWidth, pick.PreviewHeight)));

        var other = picks.Select(pick => (CharacterOptionViewModel)pick.DataContext!).First(option => !option.IsSelected);
        other.SelectCommand.Execute(null);

        Assert.Equal(other.Id, ui.Get<SettingsService>().Current.Character);
    }

    [AvaloniaFact]
    public void An_entry_row_shows_a_logged_drink_with_a_remove_button()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Get<HydrationService>().Log(250);
        ui.Show();

        var rows = ui.Window.GetVisualDescendants().OfType<EntryRow>().Where(row => row.IsEffectivelyVisible).ToList();

        Assert.Single(rows);
        Assert.Contains(rows[0].GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Remove"));
    }

    [AvaloniaFact]
    public void A_card_hides_its_heading_when_it_has_none()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Show();

        var cards = ui.Window.GetVisualDescendants().OfType<Card>().ToList();
        var headings = cards.Select(card => card.Heading).ToList();

        Assert.Contains(null, headings);
        Assert.Contains("Log a different amount", headings);
        var titled = cards.First(card => card.Heading == "Log a different amount");
        Assert.Single(titled.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("h2") && block.IsVisible);
    }

    [AvaloniaFact]
    public void Every_page_is_framed_by_the_same_layout()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Show();

        foreach (var page in new[] { HeySip.App.Shell.ShellPage.Today, HeySip.App.Shell.ShellPage.Stats, HeySip.App.Shell.ShellPage.Settings })
        {
            ui.Shell.Navigate(page);
            ui.Show();
            Assert.Contains(ui.Window.GetVisualDescendants().OfType<PageLayout>(), layout => layout.IsEffectivelyVisible);
        }
    }
}
