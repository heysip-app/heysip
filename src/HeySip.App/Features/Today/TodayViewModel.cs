using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Today;

public sealed class QuickAmountViewModel(int milliliters, Action<int> log) : ViewModelBase
{
    public string Label { get; } = $"+ {milliliters} ml";

    public System.Windows.Input.ICommand AddCommand { get; } = new RelayCommand(() => log(milliliters));
}

/// <summary>The home page: today's progress, one-tap logging, and the day's log.</summary>
public sealed partial class TodayViewModel : ViewModelBase
{
    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly ReminderService _reminders;
    private readonly TimeProvider _time;
    private DateOnly _shownDay;
    private int _shownHour;

    public TodayViewModel(HydrationService hydration, SettingsService settings, ReminderService reminders, TimeProvider time)
    {
        _hydration = hydration;
        _settings = settings;
        _reminders = reminders;
        _time = time;
        QuickAmounts = [new QuickAmountViewModel(150, Log), new QuickAmountViewModel(330, Log), new QuickAmountViewModel(500, Log)];
        _hydration.Changed += (_, _) => Refresh();
        _settings.Changed += (_, _) => Refresh();
        _reminders.StatusChanged += RefreshReminder;
        Refresh();
    }

    public IReadOnlyList<QuickAmountViewModel> QuickAmounts { get; }

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial string Greeting { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string Caption { get; set; } = "";

    [ObservableProperty]
    public partial string NextText { get; set; } = "";

    [ObservableProperty]
    public partial string GlassLabel { get; set; } = "";

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial DripPose Pose { get; set; }

    [ObservableProperty]
    public partial string Character { get; set; } = PackId.DefaultCharacter;

    /// <summary>What the character wears, from the outfit in settings.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AccessoryPack> Outfit { get; set; } = [];

    [ObservableProperty]
    public partial bool HasEntries { get; set; }

    [ObservableProperty]
    public partial decimal? CustomAmount { get; set; } = 300;

    /// <summary>True while reminders are paused (for a while or until a time), so the Quiet card offers Resume.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    /// <summary>True while reminders are held because it is outside the active hours, so the page offers a way to the schedule.</summary>
    [ObservableProperty]
    public partial bool ShowScheduleLink { get; set; }

    /// <summary>Set by the shell: opens Settings at the schedule.</summary>
    public Action? OpenSchedule { get; set; }

    /// <summary>Set by the shell: opens the Stats page.</summary>
    public Action? OpenStats { get; set; }

    [RelayCommand]
    private void ChangeSchedule() => OpenSchedule?.Invoke();

    [RelayCommand]
    private void SeeStats() => OpenStats?.Invoke();

    /// <summary>The time of day the "Quiet until" box will stay quiet until.</summary>
    [ObservableProperty]
    public partial TimeSpan? QuietTime { get; set; } = new TimeSpan(18, 0, 0);

    [RelayCommand]
    private void PauseHour() => _reminders.Pause(TimeSpan.FromHours(1));

    [RelayCommand]
    private void QuietUntilTomorrow() => _reminders.PauseUntilTomorrow();

    [RelayCommand]
    private void QuietUntilTime()
    {
        if (QuietTime is { } time)
        {
            _reminders.PauseUntil(TimeOnly.FromTimeSpan(time));
        }
    }

    [RelayCommand]
    private void ResumeReminders() => _reminders.Resume();

    [RelayCommand]
    private void LogGlass() => _hydration.LogGlass();

    [RelayCommand]
    private void LogCustom()
    {
        if (CustomAmount is > 0 and var amount)
        {
            _hydration.Log((int)amount);
        }
    }

    [RelayCommand]
    private void Undo()
    {
        // Ask the log, not the list on screen: the list can be stale (for example after midnight).
        var today = _hydration.Today();
        if (today.Count > 0)
        {
            _hydration.Remove(today[^1].Id);
        }
    }

    /// <summary>Called by the shell's timer: keeps the "next nudge" line fresh, and rebuilds the page when the day or hour changes.</summary>
    public void Tick()
    {
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        var now = ZoneResolver.LocalNow(zone, _time);
        if (DateOnly.FromDateTime(now) != _shownDay || now.Hour != _shownHour)
        {
            Refresh();
        }
        else
        {
            NextText = DescribeNext();
        }
    }

    public void Refresh()
    {
        var current = _settings.Current;
        var zone = ZoneResolver.Resolve(current.TimeZoneId, _time);
        var entries = _hydration.Today();
        var total = entries.Sum(entry => entry.Milliliters);
        var goal = current.DailyGoalMilliliters;

        Character = current.Character;
        var worn = CharacterCatalog.Wearing(current.Outfit);
        if (!worn.SequenceEqual(Outfit))
        {
            Outfit = worn;
        }

        GlassLabel = $"Drink a glass ({current.GlassMilliliters} ml)";
        var now = ZoneResolver.LocalNow(zone, _time);
        _shownDay = DateOnly.FromDateTime(now);
        _shownHour = now.Hour;
        Greeting = GreetingFor(now.Hour, current.Name);
        Summary = $"{total} / {goal} ml";
        ProgressPercent = Math.Min(100.0, total * 100.0 / goal);
        var status = _reminders.Status();
        IsPaused = status.IsPaused;
        ShowScheduleLink = status.OutsideActiveHours;
        Pose = PoseFor(total, goal, status, now.Hour);
        Caption = total >= goal
            ? "Daily goal reached. Nice work!"
            : $"{goal - total} ml to go. You've got this.";
        NextText = DescribeNext();

        Entries.Clear();
        foreach (var entry in entries.Reverse())
        {
            Entries.Add(new EntryViewModel(entry, zone, id => _hydration.Remove(id)));
        }

        HasEntries = Entries.Count > 0;
    }

    /// <summary>
    /// Happy once the goal is met, fast asleep late at night, drowsy at the edges of the day, waving hello before the
    /// first sip.
    /// </summary>
    private static DripPose PoseFor(int total, int goal, ReminderStatus status, int hour) =>
        total >= goal ? DripPose.Happy
        : status.OutsideActiveHours ? (hour >= 22 || hour < 6 ? DripPose.Sleeping : DripPose.Sleepy)
        : total == 0 ? DripPose.Waving
        : DripPose.Idle;

    /// <summary>A pause or resume (or an answered reminder) changed what "next nudge" means.</summary>
    private void RefreshReminder()
    {
        var status = _reminders.Status();
        IsPaused = status.IsPaused;
        ShowScheduleLink = status.OutsideActiveHours;
        NextText = DescribeNext();
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        Pose = PoseFor(_hydration.TodayMilliliters(), _settings.Current.DailyGoalMilliliters, status, ZoneResolver.LocalNow(zone, _time).Hour);
    }

    private void Log(int milliliters) => _hydration.Log(milliliters);

    private string DescribeNext() => ReminderStatusText.Full(_reminders.Status(), _settings.Current, _time);

    private static string GreetingFor(int hour, string name)
    {
        var part = hour switch
        {
            < 5 => "Good night",
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening",
        };
        return string.IsNullOrWhiteSpace(name) ? part : $"{part}, {name}";
    }
}
