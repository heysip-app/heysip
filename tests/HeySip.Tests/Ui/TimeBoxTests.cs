using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeySip.App.Shared.Controls;
using HeySip.App.Shell;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.Tests.Ui;

/// <summary>The active hours are typed, not scrolled through; these walk the box on the Settings page the way a person would.</summary>
public sealed class TimeBoxTests
{
    private static (UiHarness Ui, TimeBox From, TimeBox Until) OpenSettings()
    {
        var ui = UiHarness.Create();
        ui.Get<SettingsService>().Update(s => s with { Onboarded = true, ActiveStart = new TimeOnly(10, 0), ActiveEnd = new TimeOnly(23, 0) });
        ui.Shell.Navigate(ShellPage.Settings);
        ui.Show();

        var boxes = ui.Window.GetVisualDescendants().OfType<TimeBox>().ToList();
        Assert.Equal(2, boxes.Count);
        return (ui, boxes[0], boxes[1]);
    }

    private static void Type(UiHarness ui, TimeBox box, string text)
    {
        box.Focus();
        box.Text = text;
        ui.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static string Written(int hour, int minute) => TimeInput.Format(new TimeOnly(hour, minute));

    [AvaloniaFact]
    public void It_shows_the_current_hours_as_text()
    {
        var (ui, from, until) = OpenSettings();
        using (ui)
        {
            Assert.Equal(Written(10, 0), from.Text);
            Assert.Equal(Written(23, 0), until.Text);
        }
    }

    [AvaloniaFact]
    public void A_typed_time_is_kept_when_enter_is_pressed()
    {
        var (ui, from, _) = OpenSettings();
        using (ui)
        {
            Type(ui, from, "8am");

            Assert.Equal(new TimeSpan(8, 0, 0), from.SelectedTime);
            Assert.Equal(new TimeSpan(8, 0, 0), ui.Shell.Settings.ActiveStart);
            Assert.Equal(Written(8, 0), from.Text);
        }
    }

    [AvaloniaFact]
    public void A_bare_hour_keeps_the_evening_when_an_evening_time_was_showing()
    {
        var (ui, _, until) = OpenSettings();
        using (ui)
        {
            Type(ui, until, "9");

            Assert.Equal(new TimeSpan(21, 0, 0), until.SelectedTime);
        }
    }

    [AvaloniaFact]
    public void Something_that_is_not_a_time_puts_the_old_time_back()
    {
        var (ui, from, _) = OpenSettings();
        using (ui)
        {
            Type(ui, from, "banana");

            Assert.Equal(new TimeSpan(10, 0, 0), from.SelectedTime);
            Assert.Equal(Written(10, 0), from.Text);
        }
    }

    [AvaloniaFact]
    public void Up_and_down_move_to_the_next_step_and_wrap_around_midnight()
    {
        var (ui, from, _) = OpenSettings();
        using (ui)
        {
            from.Focus();
            from.SelectedTime = new TimeSpan(9, 3, 0);

            ui.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal(new TimeSpan(9, 5, 0), from.SelectedTime);

            ui.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(new TimeSpan(9, 0, 0), from.SelectedTime);

            from.SelectedTime = TimeSpan.Zero;
            ui.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(new TimeSpan(23, 55, 0), from.SelectedTime);

            ui.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal(TimeSpan.Zero, from.SelectedTime);
        }
    }
}
