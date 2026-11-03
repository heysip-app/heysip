using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Stats;

public sealed record BarViewModel(string Label, string Tooltip, double Height, bool GoalMet, DateOnly Day, Action<DateOnly> Open)
{
    /// <summary>Opens this bar's day in its own tab.</summary>
    public RelayCommand OpenCommand { get; } = new(() => Open(Day));
}

/// <summary>One line of the "Recent days" list: the day, how much was drunk, and a way to open it.</summary>
public sealed record RecentDayViewModel(string Label, string TotalText, double Percent, bool GoalMet, DateOnly Day, Action<DateOnly> Open)
{
    public RelayCommand OpenCommand { get; } = new(() => Open(Day));
}

public sealed partial class StatsViewModel : ViewModelBase
{
    private const double ChartHeight = 200;
    private const int RecentDayCount = 14;

    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;
    private DateOnly _shownDay;

    public StatsViewModel(HydrationService hydration, SettingsService settings, TimeProvider time)
    {
        _hydration = hydration;
        _settings = settings;
        _time = time;
        _hydration.Changed += (_, _) => Refresh();
        settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<BarViewModel> Bars { get; } = [];

    /// <summary>The last two weeks, newest first, whatever range the chart shows.</summary>
    public ObservableCollection<RecentDayViewModel> RecentDays { get; } = [];

    /// <summary>Set by the shell: opens a day in its own tab.</summary>
    public Action<DateOnly>? OpenDay { get; set; }

    /// <summary>Set by the shell: goes to Today, where drinks are logged.</summary>
    public Action? OpenToday { get; set; }

    [ObservableProperty]
    public partial int Days { get; set; } = 7;

    /// <summary>True when nothing was logged in the range shown, so the page offers a way to start.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [RelayCommand]
    private void LogDrink() => OpenToday?.Invoke();

    [ObservableProperty]
    public partial string Streak { get; set; } = "";

    [ObservableProperty]
    public partial string BestStreak { get; set; } = "";

    [ObservableProperty]
    public partial string Average { get; set; } = "";

    [ObservableProperty]
    public partial string GoalDays { get; set; } = "";

    [ObservableProperty]
    public partial string Total { get; set; } = "";

    [ObservableProperty]
    public partial Thickness GoalLineMargin { get; set; }

    [ObservableProperty]
    public partial string GoalLineLabel { get; set; } = "";

    public bool IsWeek => Days == 7;

    public bool IsMonth => Days == 30;

    public double PlotHeight => ChartHeight;

    [RelayCommand]
    private void ShowWeek() => Days = 7;

    [RelayCommand]
    private void ShowMonth() => Days = 30;

    partial void OnDaysChanged(int value)
    {
        OnPropertyChanged(nameof(IsWeek));
        OnPropertyChanged(nameof(IsMonth));
        Refresh();
    }

    /// <summary>Called by the shell's timer: rebuilds the chart when the day rolls over while the app stays open.</summary>
    public void Tick()
    {
        if (CurrentDay() != _shownDay)
        {
            Refresh();
        }
    }

    private DateOnly CurrentDay() =>
        ZoneResolver.LocalDate(_time.GetUtcNow(), ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time));

    public void Refresh()
    {
        _shownDay = CurrentDay();
        var summary = _hydration.Summary(Days);
        var peak = Math.Max(summary.GoalMilliliters, summary.Days.Count == 0 ? 0 : summary.Days.Max(day => day.Milliliters)) * 1.08;

        Bars.Clear();
        foreach (var day in summary.Days)
        {
            var label = Days <= 7 ? day.Date.ToString("ddd") : day.Date.Day.ToString();
            Bars.Add(new BarViewModel(
                label,
                $"{day.Date:ddd d MMM}: {day.Milliliters} ml. Click to open this day.",
                Math.Max(2, day.Milliliters / peak * ChartHeight),
                day.Milliliters >= summary.GoalMilliliters,
                day.Date,
                date => OpenDay?.Invoke(date)));
        }

        RefreshRecentDays();
        GoalLineMargin = new Thickness(0, 0, 0, summary.GoalMilliliters / peak * ChartHeight);
        GoalLineLabel = $"Goal {summary.GoalMilliliters} ml";
        Streak = $"{summary.CurrentStreak} {(summary.CurrentStreak == 1 ? "day" : "days")}";
        BestStreak = $"{summary.BestStreak} {(summary.BestStreak == 1 ? "day" : "days")}";
        Average = $"{summary.AverageMilliliters} ml";
        GoalDays = $"{summary.GoalDays} of {summary.Days.Count}";
        Total = $"{summary.TotalMilliliters / 1000.0:0.0} L";
        IsEmpty = summary.TotalMilliliters == 0;
    }

    private void RefreshRecentDays()
    {
        var summary = _hydration.Summary(RecentDayCount);
        var today = _shownDay;
        RecentDays.Clear();
        foreach (var day in summary.Days.OrderByDescending(day => day.Date))
        {
            RecentDays.Add(new RecentDayViewModel(
                DayLabel.Short(day.Date, today),
                day.Milliliters == 0 ? "none yet" : $"{day.Milliliters} ml",
                Math.Min(100, day.Milliliters * 100.0 / Math.Max(1, summary.GoalMilliliters)),
                day.Milliliters >= summary.GoalMilliliters,
                day.Date,
                date => OpenDay?.Invoke(date)));
        }
    }
}
