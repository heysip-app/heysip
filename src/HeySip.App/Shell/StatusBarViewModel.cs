using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.Features.Diagnostics;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Shell;

/// <summary>The thin bar along the bottom: today's progress, streak, next nudge and a one-click pause.</summary>
public sealed partial class StatusBarViewModel : ViewModelBase
{
    private static readonly TimeSpan QuickPause = TimeSpan.FromHours(1);

    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly ReminderService _reminders;
    private readonly TimeProvider _time;
    private DateOnly _day;

    public StatusBarViewModel(HydrationService hydration, SettingsService settings, ReminderService reminders, TimeProvider time)
    {
        _hydration = hydration;
        _settings = settings;
        _reminders = reminders;
        _time = time;

        _hydration.Changed += (_, _) => Refresh();
        _settings.Changed += (_, _) => Refresh();
        _reminders.StatusChanged += RefreshReminderLine;
        Refresh();
    }

    /// <summary>Set by the shell so the bar's items can take the user to a page.</summary>
    public Action<ShellPage>? Navigate { get; set; }

    /// <summary>Set by the shell so the bar can open the command palette.</summary>
    public Action? OpenPalette { get; set; }

    public string VersionText { get; } = DiagnosticsService.AppVersion;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool GoalReached { get; set; }

    [ObservableProperty]
    public partial string StreakText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasStreak { get; set; }

    [ObservableProperty]
    public partial string NextText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial string PauseLabel { get; set; } = "";

    [ObservableProperty]
    public partial string PauseTip { get; set; } = "";

    [RelayCommand]
    private void ShowToday() => Navigate?.Invoke(ShellPage.Today);

    [RelayCommand]
    private void ShowStats() => Navigate?.Invoke(ShellPage.Stats);

    [RelayCommand]
    private void ShowHistory() => Navigate?.Invoke(ShellPage.History);

    [RelayCommand]
    private void ShowPalette() => OpenPalette?.Invoke();

    [RelayCommand]
    private void TogglePause()
    {
        if (_reminders.Status().IsPaused)
        {
            _reminders.Resume();
        }
        else
        {
            _reminders.Pause(QuickPause);
        }
    }

    /// <summary>Called by the shell's timer: keeps the countdown fresh, and recounts when the day rolls over.</summary>
    public void Tick()
    {
        if (CurrentDay() != _day)
        {
            Refresh();
        }
        else
        {
            RefreshReminderLine();
        }
    }

    public void Refresh()
    {
        var current = _settings.Current;
        _day = CurrentDay();

        var total = _hydration.TodayMilliliters();
        var goal = current.DailyGoalMilliliters;
        ProgressText = $"{total} / {goal} ml";
        ProgressPercent = Math.Min(100.0, total * 100.0 / goal);
        GoalReached = total >= goal;

        var streak = _hydration.Summary(7).CurrentStreak;
        HasStreak = streak > 0;
        StreakText = streak == 1 ? "1 day streak" : $"{streak} day streak";

        RefreshReminderLine();
    }

    private void RefreshReminderLine()
    {
        var status = _reminders.Status();
        NextText = ReminderStatusText.Brief(status, _settings.Current, _time);
        IsPaused = status.IsPaused;
        PauseLabel = status.IsPaused ? "Resume" : "Pause 1h";
        PauseTip = status.IsPaused ? "Resume reminders" : "Pause reminders for one hour";
    }

    private DateOnly CurrentDay() =>
        ZoneResolver.LocalDate(_time.GetUtcNow(), ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time));
}
