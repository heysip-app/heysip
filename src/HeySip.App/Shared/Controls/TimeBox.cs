using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Shared.Controls;

/// <summary>
/// A time of day you can simply type ("9", "9:30", "9pm", "21:00", "noon"). Up and Down move it by a few minutes, Enter or
/// leaving the box keeps it, and something that is not a time puts the old time back.
/// </summary>
public sealed class TimeBox : TextBox
{
    public static readonly StyledProperty<TimeSpan?> SelectedTimeProperty =
        AvaloniaProperty.Register<TimeBox, TimeSpan?>(nameof(SelectedTime), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> StepMinutesProperty =
        AvaloniaProperty.Register<TimeBox, int>(nameof(StepMinutes), defaultValue: DefaultStepMinutes);

    private const int DefaultStepMinutes = 5;
    private const int MinutesInDay = 24 * 60;
    private const int InputMaxLength = 12;
    private const string Hint = "e.g. 9:30 pm";

    public TimeBox()
    {
        MaxLength = InputMaxLength;
        PlaceholderText = Hint;
        ShowSelected();
    }

    /// <summary>The time shown. Empty when there is none.</summary>
    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    /// <summary>How many minutes the Up and Down keys move the time by.</summary>
    public int StepMinutes
    {
        get => GetValue(StepMinutesProperty);
        set => SetValue(StepMinutesProperty, value);
    }

    // It looks and behaves like the other text boxes, so it takes their style.
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedTimeProperty)
        {
            ShowSelected();
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Dispatcher.UIThread.Post(SelectAll);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Commit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                e.Handled = true;
                return;
            case Key.Up:
                Nudge(up: true);
                e.Handled = true;
                return;
            case Key.Down:
                Nudge(up: false);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    private TimeOnly? Current => SelectedTime is { } time ? TimeOnly.FromTimeSpan(time) : null;

    private void ShowSelected() => Text = Current is { } time ? TimeInput.Format(time) : "";

    /// <summary>Keeps what was typed if it is a time, otherwise shows the old time again.</summary>
    private void Commit()
    {
        if (TimeInput.TryParse(Text, Current, out var typed))
        {
            SelectedTime = typed.ToTimeSpan();
        }

        ShowSelected();
    }

    /// <summary>Moves to the next (or previous) multiple of the step, wrapping around midnight.</summary>
    private void Nudge(bool up)
    {
        var step = Math.Max(1, StepMinutes);
        var from = TimeInput.TryParse(Text, Current, out var typed) ? typed : Current ?? default;
        var total = from.Hour * 60 + from.Minute;
        var next = up
            ? (total / step + 1) * step % MinutesInDay
            : (total - 1 + MinutesInDay) % MinutesInDay / step * step;

        SelectedTime = TimeSpan.FromMinutes(next);
    }
}
