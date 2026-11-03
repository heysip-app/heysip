using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Day;

/// <summary>Creates day tabs. Kept as a factory so the shell doesn't need to know what a day tab depends on.</summary>
public sealed class DayViewModelFactory(HydrationService hydration, SettingsService settings, TimeProvider time)
{
    public DayViewModel Create(DateOnly day) => new(day, hydration, settings, time);
}

/// <summary>One day on its own: its drinks, how it went against the goal, and a way to add a forgotten drink.</summary>
public sealed partial class DayViewModel : ViewModelBase, IDisposable
{
    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;
    private readonly EventHandler _hydrationChanged;
    private readonly EventHandler<AppSettings> _settingsChanged;

    public DayViewModel(DateOnly day, HydrationService hydration, SettingsService settings, TimeProvider time)
    {
        Day = day;
        _hydration = hydration;
        _settings = settings;
        _time = time;

        var zone = ZoneResolver.Resolve(settings.Current.TimeZoneId, time);
        var now = ZoneResolver.LocalNow(zone, time);
        IsToday = day == DateOnly.FromDateTime(now);
        Heading = DayLabel.Long(day);

        AddAmount = settings.Current.GlassMilliliters;
        // Today starts at "now" so a just-forgotten drink is one click; an earlier day starts at midday.
        AddTime = IsToday ? new TimeSpan(now.Hour, now.Minute, 0) : new TimeSpan(12, 0, 0);

        _hydrationChanged = (_, _) => Refresh();
        _settingsChanged = (_, _) => Refresh();
        _hydration.Changed += _hydrationChanged;
        _settings.Changed += _settingsChanged;
        Refresh();
    }

    public DateOnly Day { get; }

    public string Heading { get; }

    public bool IsToday { get; }

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    /// <summary>Set by the shell: opens (or focuses) another day's tab.</summary>
    public Action<DateOnly>? OpenDay { get; set; }

    /// <summary>Set by the shell: goes to the History page.</summary>
    public Action? OpenHistory { get; set; }

    /// <summary>False on the latest day that has begun, so "Next day" never leads into the future.</summary>
    [ObservableProperty]
    public partial bool HasNextDay { get; set; }

    [RelayCommand]
    private void PreviousDay() => OpenDay?.Invoke(Day.AddDays(-1));

    [RelayCommand]
    private void NextDay()
    {
        if (Day < CurrentDay())
        {
            OpenDay?.Invoke(Day.AddDays(1));
        }
    }

    [RelayCommand]
    private void BackToHistory() => OpenHistory?.Invoke();

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string Caption { get; set; } = "";

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool GoalMet { get; set; }

    [ObservableProperty]
    public partial bool HasEntries { get; set; }

    [ObservableProperty]
    public partial decimal? AddAmount { get; set; }

    [ObservableProperty]
    public partial TimeSpan? AddTime { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [RelayCommand]
    private void AddDrink()
    {
        if (AddAmount is not { } amount || amount <= 0)
        {
            Message = "Enter an amount above zero.";
            return;
        }

        if (AddTime is not { } at)
        {
            Message = "Choose the time you drank it.";
            return;
        }

        try
        {
            var milliliters = (int)Math.Round(amount);
            _hydration.LogAt(Day, TimeOnly.FromTimeSpan(at), milliliters);
            Message = $"Added {milliliters} ml at {at:hh\\:mm}.";
        }
        catch (ArgumentOutOfRangeException)
        {
            Message = "That time hasn't happened yet.";
        }
    }

    public void Dispose()
    {
        _hydration.Changed -= _hydrationChanged;
        _settings.Changed -= _settingsChanged;
    }

    private DateOnly CurrentDay() =>
        ZoneResolver.LocalDate(_time.GetUtcNow(), ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time));

    private void Refresh()
    {
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        var log = _hydration.Day(Day);
        var goal = _settings.Current.DailyGoalMilliliters;

        HasNextDay = Day < ZoneResolver.LocalDate(_time.GetUtcNow(), zone);
        Summary = $"{log.Milliliters} / {goal} ml";
        ProgressPercent = Math.Min(100.0, log.Milliliters * 100.0 / goal);
        GoalMet = log.GoalMet;
        Caption = log.GoalMet ? "Goal met." : log.Milliliters == 0 ? "Nothing logged on this day." : $"{goal - log.Milliliters} ml short of the goal.";

        Entries.Clear();
        foreach (var entry in log.Entries)
        {
            Entries.Add(new EntryViewModel(entry, zone, id => _hydration.Remove(id)));
        }

        HasEntries = Entries.Count > 0;
    }
}
