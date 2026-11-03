using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Features.Browse;
using HeySip.App.Features.Day;
using HeySip.App.Features.History;
using HeySip.App.Features.Install;
using HeySip.App.Features.Onboarding;
using HeySip.App.Features.Palette;
using HeySip.App.Features.Settings;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shared;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Shell;

public enum ShellPage
{
    Today,
    Stats,
    History,
    Settings,
    Browse,
}

/// <summary>Composes the feature pages into the main window: the open tabs, and navigation between them.</summary>
public sealed partial class ShellViewModel : ViewModelBase, IShellNavigator
{
    private readonly SettingsService _appSettings;
    private readonly DayViewModelFactory _days;
    private readonly TimeProvider _time;
    private string? _queuedPack;

    public ShellViewModel(
        TodayViewModel today,
        StatsViewModel stats,
        HistoryViewModel history,
        SettingsViewModel settings,
        BrowseViewModel browse,
        OnboardingViewModel onboarding,
        InstallPreviewViewModel installPreview,
        StatusBarViewModel statusBar,
        RemovalBannerViewModel removalBanner,
        NoticeBannerViewModel noticeBanner,
        DayViewModelFactory days,
        PaletteCommands paletteCommands,
        SettingsService appSettings,
        TimeProvider time)
    {
        Today = today;
        Stats = stats;
        History = history;
        Settings = settings;
        Browse = browse;
        Onboarding = onboarding;
        InstallPreview = installPreview;
        StatusBar = statusBar;
        RemovalBanner = removalBanner;
        NoticeBanner = noticeBanner;
        _days = days;
        _appSettings = appSettings;
        _time = time;

        Palette = new CommandPaletteViewModel(() => paletteCommands.Build(this), query => paletteCommands.ForQuery(this, query));
        StatusBar.Navigate = Navigate;
        StatusBar.OpenPalette = Palette.Open;
        Stats.OpenDay = OpenDay;
        Stats.OpenToday = () => Navigate(ShellPage.Today);
        History.OpenDay = OpenDay;
        History.OpenToday = () => Navigate(ShellPage.Today);
        Today.OpenSchedule = () => OpenSettings("schedule");
        Today.OpenStats = () => Navigate(ShellPage.Stats);
        Settings.InstallPackRequested = InstallFromFile;
        Settings.BrowseRequested = () =>
        {
            Browse.ShowCharactersCommand.Execute(null);
            Navigate(ShellPage.Browse);
        };
        Settings.BrowseAccessoriesRequested = () =>
        {
            Browse.ShowAccessoriesCommand.Execute(null);
            Navigate(ShellPage.Browse);
        };
        Browse.ShowInstall = (result, source, size, bundle) => InstallPreview.Show(result, source, size, bundle);

        // A pack opened while the welcome card is up waits until the welcome is done.
        Onboarding.Completed += () =>
        {
            if (_queuedPack is { } queued)
            {
                _queuedPack = null;
                OpenPackFile(queued);
            }
        };

        // Today is always there and can't be closed, so the window never has nothing to show.
        var todayTab = new TabViewModel("today", "Today", today, isClosable: false, Select, Close);
        Tabs.Add(todayTab);
        SelectedTab = todayTab;
        Tabs.CollectionChanged += (_, _) => SyncDayTabs();
    }

    public TodayViewModel Today { get; }

    public StatsViewModel Stats { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public BrowseViewModel Browse { get; }

    public OnboardingViewModel Onboarding { get; }

    public InstallPreviewViewModel InstallPreview { get; }

    /// <summary>Set by the window: shows the system's file picker and returns the chosen path, or null if cancelled.</summary>
    public Func<Task<string?>>? PickPackFile { get; set; }

    public StatusBarViewModel StatusBar { get; }

    public RemovalBannerViewModel RemovalBanner { get; }

    /// <summary>A message from the project (an update, a notice), shown across the top.</summary>
    public NoticeBannerViewModel NoticeBanner { get; }

    public CommandPaletteViewModel Palette { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>The opened days, listed in the left rail under the fixed pages.</summary>
    public ObservableCollection<TabViewModel> DayTabs { get; } = [];

    [ObservableProperty]
    public partial TabViewModel SelectedTab { get; set; } = null!;

    /// <summary>What the main area is showing: the selected tab's page.</summary>
    public ViewModelBase Current => SelectedTab.Content;

    public bool HasDayTabs => DayTabs.Count > 0;

    public bool IsToday => Current == Today;

    public bool IsStats => Current == Stats;

    public bool IsHistory => Current == History;

    public bool IsSettings => Current == Settings;

    public bool IsBrowse => Current == Browse;

    partial void OnSelectedTabChanged(TabViewModel oldValue, TabViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsStats));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsBrowse));
    }

    // While the welcome card or a pack's install card is up, the pages behind it are out of reach: shortcuts must not switch or close tabs.
    private bool IsModalShowing => Onboarding.IsVisible || InstallPreview.IsVisible;

    [RelayCommand]
    private void ShowToday() => NavigateUnlessModal(ShellPage.Today);

    [RelayCommand]
    private void ShowStats() => NavigateUnlessModal(ShellPage.Stats);

    [RelayCommand]
    private void ShowHistory() => NavigateUnlessModal(ShellPage.History);

    [RelayCommand]
    private void ShowBrowse() => NavigateUnlessModal(ShellPage.Browse);

    /// <summary>Always starts from every setting, like the palette's "Settings", so a filter left by a named setting doesn't linger.</summary>
    [RelayCommand]
    private void ShowSettings()
    {
        if (!IsModalShowing)
        {
            OpenSettings(null);
        }
    }

    [RelayCommand]
    public void CloseCurrentTab()
    {
        if (!IsModalShowing)
        {
            Close(SelectedTab);
        }
    }

    [RelayCommand]
    public void NextTab()
    {
        if (!IsModalShowing)
        {
            Step(+1);
        }
    }

    [RelayCommand]
    public void PreviousTab()
    {
        if (!IsModalShowing)
        {
            Step(-1);
        }
    }

    /// <summary>Escape: skips the welcome, or cancels the install card, whichever shows; otherwise closes the palette.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (Onboarding.IsVisible)
        {
            Onboarding.SkipCommand.Execute(null);
        }
        else if (InstallPreview.IsVisible)
        {
            InstallPreview.Close();
        }
        else
        {
            Palette.Close();
        }
    }

    /// <summary>Opens the picker for a pack file, then shows what is in it.</summary>
    public void InstallFromFile() => _ = InstallFromFileAsync();

    public async Task InstallFromFileAsync()
    {
        if (IsModalShowing || PickPackFile is null)
        {
            return;
        }

        try
        {
            if (await PickPackFile() is { } path)
            {
                OpenPackFile(path);
            }
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException)
        {
            InstallPreview.ShowError("The file picker couldn't be opened");
        }
    }

    /// <summary>
    /// Checks a pack file (or folder) and shows the install card for it. Nothing is installed until the user says so.
    /// </summary>
    public void OpenPackFile(string path)
    {
        if (Onboarding.IsVisible)
        {
            _queuedPack = path;
            return;
        }

        var options = new PackLoadOptions(Shared.AppVersion.Current);
        long size = 0;
        PackLoadResult result;
        try
        {
            if (Directory.Exists(path))
            {
                result = PackLoader.FromFolder(path, options);
            }
            else if (!File.Exists(path))
            {
                result = PackLoadResult.Failure("That file wasn't found");
            }
            else
            {
                size = new FileInfo(path).Length;
                if (size > PackLimits.MaxPackBytes)
                {
                    result = PackLoadResult.Failure("The pack file is too big");
                }
                else
                {
                    using var stream = File.OpenRead(path);
                    result = PackLoader.FromZip(stream, options);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result = PackLoadResult.Failure("The file couldn't be read");
        }

        InstallPreview.Show(result, Path.GetFileName(path), size);
    }

    /// <summary>Shows the first-run welcome again, starting from the current settings.</summary>
    public void ShowWelcome() => Onboarding.Restart();

    private void NavigateUnlessModal(ShellPage page)
    {
        if (!IsModalShowing)
        {
            Navigate(page);
        }
    }

    /// <summary>Opens (or focuses) the tab for a page.</summary>
    public void Navigate(ShellPage page)
    {
        switch (page)
        {
            case ShellPage.Stats:
                Stats.Refresh();
                Open("stats", "Stats", Stats, closable: true);
                break;
            case ShellPage.History:
                History.Refresh();
                Open("history", "History", History, closable: true);
                break;
            case ShellPage.Settings:
                Open("settings", "Settings", Settings, closable: true);
                break;
            case ShellPage.Browse:
                Open("browse", "Characters", Browse, closable: true);
                _ = Browse.RefreshAsync();
                break;
            default:
                Today.Refresh();
                SelectedTab = Tabs[0];
                break;
        }
    }

    [RelayCommand]
    private void OpenPalette()
    {
        if (!IsModalShowing)
        {
            Palette.Open();
        }
    }

    /// <summary>Opens (or focuses) the Settings tab, filtered to <paramref name="search"/>. Null or empty shows everything.</summary>
    public void OpenSettings(string? search)
    {
        Settings.SearchText = search ?? "";
        Navigate(ShellPage.Settings);
    }

    /// <summary>Opens (or focuses) a tab showing a single day.</summary>
    public void OpenDay(DateOnly day)
    {
        var key = $"day:{day:yyyy-MM-dd}";
        var existing = Tabs.FirstOrDefault(tab => tab.Key == key);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var zone = ZoneResolver.Resolve(_appSettings.Current.TimeZoneId, _time);
        var today = ZoneResolver.LocalDate(_time.GetUtcNow(), zone);
        var content = _days.Create(day);
        content.OpenDay = OpenDay;
        content.OpenHistory = () => Navigate(ShellPage.History);
        Open(key, DayTitle(day, today), content, closable: true, glyph: day.Day.ToString(System.Globalization.CultureInfo.InvariantCulture), caption: day.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture));
    }

    /// <summary>Called every few seconds by the app's timer to keep countdowns fresh and notice a new day.</summary>
    public void Tick()
    {
        Today.Tick();
        Stats.Tick();
        History.Tick();
        StatusBar.Tick();
        RenameDayTabs();
    }

    /// <summary>A tab opened as "Today's log" or "Yesterday" must not keep that name once midnight has passed.</summary>
    private void RenameDayTabs()
    {
        var zone = ZoneResolver.Resolve(_appSettings.Current.TimeZoneId, _time);
        var today = ZoneResolver.LocalDate(_time.GetUtcNow(), zone);
        foreach (var tab in DayTabs)
        {
            if (DateOnly.TryParseExact(tab.Key["day:".Length..], "yyyy-MM-dd", out var day))
            {
                tab.Title = DayTitle(day, today);
            }
        }
    }

    private static string DayTitle(DateOnly day, DateOnly today) => day == today ? "Today's log" : DayLabel.Short(day, today);

    private void Open(string key, string title, ViewModelBase content, bool closable, string? glyph = null, string? caption = null)
    {
        var tab = Tabs.FirstOrDefault(existing => existing.Key == key)
            ?? AddTab(new TabViewModel(key, title, content, closable, Select, Close, glyph, caption));
        SelectedTab = tab;
    }

    private TabViewModel AddTab(TabViewModel tab)
    {
        Tabs.Add(tab);
        return tab;
    }

    private void Select(TabViewModel tab) => SelectedTab = tab;

    private void SyncDayTabs()
    {
        var days = Tabs.Where(tab => tab.Glyph is not null).ToList();
        for (var i = DayTabs.Count - 1; i >= 0; i--)
        {
            if (!days.Contains(DayTabs[i]))
            {
                DayTabs.RemoveAt(i);
            }
        }

        foreach (var day in days.Where(day => !DayTabs.Contains(day)))
        {
            DayTabs.Add(day);
        }

        OnPropertyChanged(nameof(HasDayTabs));
    }

    private void Close(TabViewModel tab)
    {
        if (!tab.IsClosable || !Tabs.Contains(tab))
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        (tab.Content as DayViewModel)?.Dispose(); // day tabs listen for changes; stop them when closed. The other pages live as long as the app.

        if (tab == SelectedTab)
        {
            // Land on the tab that slid into the closed one's place, or the one before if it was last.
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }

    private void Step(int direction)
    {
        var index = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(index + direction + Tabs.Count) % Tabs.Count];
    }
}
