using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HeySip.App.Shell;
using HeySip.Core.Features.Settings;

namespace HeySip.Tests.Ui;

/// <summary>The settings screen must offer exactly the limits the settings file keeps.</summary>
public sealed class SettingsBoundsTests
{
    [AvaloniaFact]
    public void Number_boxes_use_the_shared_limits()
    {
        using var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true });
        ui.Shell.Navigate(ShellPage.Settings);
        ui.Show();

        var boxes = ui.Window.GetVisualDescendants().OfType<NumericUpDown>().ToList();

        AssertHasBox(boxes, SettingsLimits.IntervalMinutes);
        AssertHasBox(boxes, SettingsLimits.SnoozeMinutes);
        AssertHasBox(boxes, SettingsLimits.DailyGoalMilliliters);
        AssertHasBox(boxes, SettingsLimits.GlassMilliliters);
        AssertHasBox(boxes, SettingsLimits.Monitor);
    }

    private static void AssertHasBox(IEnumerable<NumericUpDown> boxes, HeySip.Core.SharedKernel.IntRange range) =>
        Assert.Contains(boxes, box => box.Minimum == range.Min && box.Maximum == range.Max && box.Increment == range.Step);

    [AvaloniaFact]
    public void Values_near_the_limits_are_shown_and_kept_not_clamped_while_the_screen_loads()
    {
        using var ui = UiHarness.Create();
        var settings = ui.Get<SettingsService>();
        settings.Update(s => s with
        {
            Onboarded = true,
            IntervalMinutes = SettingsLimits.IntervalMinutes.Max,
            SnoozeMinutes = SettingsLimits.SnoozeMinutes.Max,
            DailyGoalMilliliters = SettingsLimits.DailyGoalMilliliters.Max,
            GlassMilliliters = SettingsLimits.GlassMilliliters.Min,
        });

        ui.Shell.Navigate(ShellPage.Settings);
        ui.Show();

        var boxes = ui.Window.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        Assert.Contains(boxes, box => box.Maximum == SettingsLimits.IntervalMinutes.Max && box.Value == SettingsLimits.IntervalMinutes.Max);
        Assert.Contains(boxes, box => box.Maximum == SettingsLimits.SnoozeMinutes.Max && box.Value == SettingsLimits.SnoozeMinutes.Max);
        Assert.Contains(boxes, box => box.Maximum == SettingsLimits.DailyGoalMilliliters.Max && box.Value == SettingsLimits.DailyGoalMilliliters.Max);
        Assert.Contains(boxes, box => box.Minimum == SettingsLimits.GlassMilliliters.Min && box.Value == SettingsLimits.GlassMilliliters.Min);

        // Opening the screen must not have rewritten anything.
        Assert.Equal(SettingsLimits.IntervalMinutes.Max, settings.Current.IntervalMinutes);
        Assert.Equal(SettingsLimits.DailyGoalMilliliters.Max, settings.Current.DailyGoalMilliliters);
    }
}
