using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Shared;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.History;

/// <summary>One day in the history list: its total and its drinks. Updated in place so the list doesn't jump while you scroll.</summary>
public sealed partial class HistoryDayViewModel(DateOnly date, Action<DateOnly> open) : ObservableObject
{
    public DateOnly Date { get; } = date;

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    public RelayCommand OpenCommand { get; } = new(() => open(date));

    [ObservableProperty]
    public partial string Label { get; set; } = "";

    [ObservableProperty]
    public partial string TotalText { get; set; } = "";

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial bool GoalMet { get; set; }

    public void Update(DayLog log, string label, int goal, TimeZoneInfo zone, Action<long> remove)
    {
        Label = label;
        TotalText = $"{log.Milliliters} ml";
        Percent = Math.Min(100, log.Milliliters * 100.0 / Math.Max(1, goal));
        GoalMet = log.GoalMet;

        // Reuse nothing fancy: a day only ever has a handful of drinks, so rebuilding its rows is cheap.
        Entries.Clear();
        foreach (var entry in log.Entries)
        {
            Entries.Add(new EntryViewModel(entry, zone, remove));
        }
    }
}

/// <summary>
/// Every day that has drinks in it, newest first, loaded a couple of weeks at a time as you scroll (or press the button).
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    public const int PageSize = 14;

    private readonly HydrationService _hydration;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;
    private DateOnly? _next;
    private DateOnly _shownDay;

    public HistoryViewModel(HydrationService hydration, SettingsService settings, TimeProvider time)
    {
        _hydration = hydration;
        _settings = settings;
        _time = time;
        _hydration.Changed += (_, _) => Refresh();
        _settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<HistoryDayViewModel> Days { get; } = [];

    /// <summary>Set by the shell: opens a day in its own tab.</summary>
    public Action<DateOnly>? OpenDay { get; set; }

    /// <summary>Set by the shell: goes to Today, where drinks are logged.</summary>
    public Action? OpenToday { get; set; }

    [RelayCommand]
    private void LogDrink() => OpenToday?.Invoke();

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>Called by the shell's timer: relabels "Today" and "Yesterday" when midnight passes.</summary>
    public void Tick()
    {
        if (CurrentDay() != _shownDay)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Re-reads what is already on screen, back to the oldest day shown, and updates it in place. A new drink therefore adds
    /// a day at the top without pushing the bottom one off the list, and an older page you loaded stays loaded.
    /// </summary>
    public void Refresh()
    {
        // If the whole history is already listed, anything new belongs on the list, even an older day that was just added.
        // Otherwise only what reaches back to the oldest day shown is kept; older days wait for "load more".
        var oldestShown = Days.Count > 0 && _next is not null ? Days[^1].Date : (DateOnly?)null;
        var everythingShown = Days.Count > 0 && _next is null;
        var logs = new List<DayLog>();
        DateOnly? cursor = null;
        DateOnly? next;
        do
        {
            var page = _hydration.BrowseHistory(cursor, PageSize);
            logs.AddRange(page.Days);
            next = page.Next;
            cursor = next;
        }
        while (next is not null && (everythingShown || logs.Count < PageSize || (oldestShown is { } oldest && logs[^1].Date > oldest)));

        if (oldestShown is { } limit)
        {
            var kept = logs.Where(log => log.Date >= limit).ToList();
            if (kept.Count < logs.Count && kept.Count > 0)
            {
                next = kept[^1].Date; // there is more below what we keep: carry on from here
            }

            logs = kept;
        }

        _next = next;
        Apply(logs);
    }

    [RelayCommand]
    public void LoadMore()
    {
        if (_next is null)
        {
            return;
        }

        var page = _hydration.BrowseHistory(_next, PageSize);
        _next = page.Next;
        foreach (var log in page.Days.Where(log => log.Entries.Count > 0))
        {
            var node = new HistoryDayViewModel(log.Date, date => OpenDay?.Invoke(date));
            Fill(node, log);
            Days.Add(node);
        }

        HasMore = _next is not null;
        IsEmpty = Days.Count == 0;
    }

    private void Fill(HistoryDayViewModel node, DayLog log)
    {
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        node.Update(log, Label(log.Date), _hydration.GoalMilliliters, zone, id => _hydration.Remove(id));
    }

    private void Apply(IReadOnlyList<DayLog> logs)
    {
        _shownDay = CurrentDay();

        // A day that has just lost its last drink disappears; one that gained its first appears in its place.
        var wanted = logs.Where(log => log.Entries.Count > 0).ToList();
        for (var i = Days.Count - 1; i >= 0; i--)
        {
            if (wanted.All(log => log.Date != Days[i].Date))
            {
                Days.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var log = wanted[i];
            if (i >= Days.Count || Days[i].Date != log.Date)
            {
                Days.Insert(i, new HistoryDayViewModel(log.Date, date => OpenDay?.Invoke(date)));
            }

            Fill(Days[i], log);
        }

        HasMore = _next is not null;
        IsEmpty = Days.Count == 0;
    }

    private string Label(DateOnly date)
    {
        var label = DayLabel.Short(date, _shownDay);
        return date.Year != _shownDay.Year && date < _shownDay.AddDays(-1) ? $"{label} {date.Year}" : label;
    }

    private DateOnly CurrentDay()
    {
        var zone = ZoneResolver.Resolve(_settings.Current.TimeZoneId, _time);
        return ZoneResolver.LocalDate(_time.GetUtcNow(), zone);
    }
}
