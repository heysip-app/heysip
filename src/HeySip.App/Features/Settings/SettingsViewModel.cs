using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeySip.App.Features.Browse;
using HeySip.App.Shared;
using HeySip.App.Shared.Audio;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Packs;
using HeySip.Core.Features.Diagnostics;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;

namespace HeySip.App.Features.Settings;

public sealed partial class DayToggleViewModel(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public string Label { get; } = day.ToString()[..3];

    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

public sealed partial class CharacterOptionViewModel(string id, string name, Action<string> select) : ObservableObject
{
    /// <summary>The character's pack id, such as "heysip.drip".</summary>
    public string Id { get; } = id;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(Id);
}

/// <summary>One accessory (or "None") you can choose for a slot, drawn on the character you currently have.</summary>
public sealed partial class AccessoryOptionViewModel(string? id, string name, AccessoryPack? pack, Action<string?> select) : ObservableObject
{
    /// <summary>The accessory's pack id, or null for "None".</summary>
    public string? Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>What to draw on the preview: the accessory, or nothing for "None".</summary>
    public IReadOnlyList<AccessoryPack>? Worn { get; } = pack is null ? null : [pack];

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The character the preview is drawn on, so the tile shows how it will really look.</summary>
    [ObservableProperty]
    public partial string Character { get; set; } = PackId.DefaultCharacter;

    [RelayCommand]
    private void Select() => select(Id);
}

/// <summary>One place an accessory can be worn, with the choices for it.</summary>
public sealed class OutfitSlotViewModel(AccessorySlot slot, IReadOnlyList<AccessoryOptionViewModel> options)
{
    public AccessorySlot Slot { get; } = slot;

    public string Title { get; } = OutfitSlots.TitleOf(slot);

    public string Key { get; } = OutfitSlots.KeyOf(slot);

    public IReadOnlyList<AccessoryOptionViewModel> Options { get; } = options;
}

/// <summary>
/// A group of slots in the outfit picker: what comes with the character, or the other accessories. The title is empty when
/// the character comes with nothing, so there is just one group and nothing to tell apart.
/// </summary>
public sealed record OutfitSection(string Title, IReadOnlyList<OutfitSlotViewModel> Slots)
{
    public bool HasTitle => Title.Length > 0;
}

/// <summary>A character or accessory installed from a file, with a way to remove it again.</summary>
public sealed partial class InstalledPackViewModel(string id, string name, string kind, Action<string> remove) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Kind { get; } = kind;

    [RelayCommand]
    private void Remove() => remove(Id);
}

public sealed partial class PresetOptionViewModel(WorkHoursPreset preset, Action<WorkHoursPreset> select) : ObservableObject
{
    public WorkHoursPreset Preset { get; } = preset;

    public string Name => Preset.Name;

    /// <summary>The preset in words, following the user's own weekend.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = preset.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(Preset);
}

public sealed record ZoneOption(string? Id, string Label);

/// <summary>
/// The settings page. Every change is saved immediately: edits flow through <see cref="Push"/>, and external changes
/// (such as the onboarding wizard) flow back in through <see cref="Load"/>.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settings;
    private readonly HydrationService _hydration;
    private readonly ReminderService _reminders;
    private readonly IStartupRegistration _startup;
    private readonly DiagnosticsService _diagnostics;
    private readonly IClipboardService _clipboard;
    private readonly IShellLauncher _shell;
    private readonly AppDataLocation _location;
    private Action _stopListening = () => { };
    private readonly SoundService _sounds;
    private bool _loading;

    public SettingsViewModel(
        SettingsService settings,
        HydrationService hydration,
        ReminderService reminders,
        IStartupRegistration startup,
        DiagnosticsService diagnostics,
        IClipboardService clipboard,
        IShellLauncher shell,
        AppDataLocation location,
        SoundService sounds)
    {
        _location = location;
        _sounds = sounds;
        _settings = settings;
        _hydration = hydration;
        _reminders = reminders;
        _startup = startup;
        _diagnostics = diagnostics;
        _clipboard = clipboard;
        _shell = shell;

        Zones =
        [
            new ZoneOption(null, "Automatic (this computer)"),
            .. TimeZoneInfo.GetSystemTimeZones().Select(zone => new ZoneOption(zone.Id, zone.DisplayName)),
        ];
        Days = Enum.GetValues<DayOfWeek>()
            .OrderBy(day => ((int)day + 6) % 7) // Monday first
            .Select(day => new DayToggleViewModel(day))
            .ToList();
        WeekendDays = Enum.GetValues<DayOfWeek>()
            .OrderBy(day => ((int)day + 6) % 7)
            .Select(day => new DayToggleViewModel(day))
            .ToList();
        BuildChoices();
        Presets = WorkHoursPresets.All.Select(preset => new PresetOptionViewModel(preset, ApplyPreset)).ToList();

        Load(_settings.Current);

        // Installing or removing a pack changes what can be chosen. The page lives as long as the app, so listen weakly.
        var self = new WeakReference<SettingsViewModel>(this);
        Action? onCatalogChanged = null;
        onCatalogChanged = () =>
        {
            if (self.TryGetTarget(out var page))
            {
                // The lists are read by the UI; they may only be rebuilt there, whichever thread announced the change.
                if (Dispatcher.UIThread.CheckAccess())
                {
                    page.RefreshChoices();
                }
                else
                {
                    Dispatcher.UIThread.Post(page.RefreshChoices);
                }
            }
            else
            {
                CharacterCatalog.Changed -= onCatalogChanged;
            }
        };
        CharacterCatalog.Changed += onCatalogChanged;
        _stopListening = () => CharacterCatalog.Changed -= onCatalogChanged;

        PropertyChanged += OnOwnPropertyChanged;
        foreach (var day in Days.Concat(WeekendDays))
        {
            day.PropertyChanged += OnToggleChanged;
        }

        _settings.Changed += (_, updated) =>
        {
            // Each character has its own set of accessories to list first, so another character means another outfit picker.
            if (updated.Character != _outfitFor)
            {
                BuildOutfit(updated.Character);
                RaiseVisibilityChanged();
            }

            Load(updated);
        };
    }

    /// <summary>Stops listening for changes to the installed characters. The page lives as long as the app, so this matters mostly to tests.</summary>
    public void Dispose() => _stopListening();

    public IReadOnlyList<ZoneOption> Zones { get; }

    public IReadOnlyList<DayToggleViewModel> Days { get; }

    /// <summary>Which days count as the weekend: any number of them, any days.</summary>
    public IReadOnlyList<DayToggleViewModel> WeekendDays { get; }

    public ObservableCollection<CharacterOptionViewModel> Characters { get; } = [];

    /// <summary>
    /// Every row of the outfit picker (hat, glasses, scarf, held), each with its choices. When the character comes with
    /// accessories, a slot can have two rows: the ones that come with it, and the others.
    /// </summary>
    public ObservableCollection<OutfitSlotViewModel> Outfit { get; } = [];

    /// <summary>The same rows, in the groups they are shown in.</summary>
    public ObservableCollection<OutfitSection> OutfitSections { get; } = [];

    /// <summary>The character the outfit rows were built for, so they are rebuilt when another is chosen.</summary>
    private string? _outfitFor;

    /// <summary>Characters and accessories that came from a file, so they can be taken off again.</summary>
    public ObservableCollection<InstalledPackViewModel> Installed { get; } = [];

    public bool HasInstalled => Installed.Count > 0;

    /// <summary>Set by the shell: opens the file picker to install a pack.</summary>
    public Action? InstallPackRequested { get; set; }

    [RelayCommand]
    private void InstallPack() => InstallPackRequested?.Invoke();

    /// <summary>Set by the shell: opens the page where characters and accessories are found.</summary>
    public Action? BrowseRequested { get; set; }

    [RelayCommand]
    private void BrowseCharacters() => BrowseRequested?.Invoke();

    /// <summary>Set by the shell: opens the page where characters and accessories are found, on the accessories.</summary>
    public Action? BrowseAccessoriesRequested { get; set; }

    [RelayCommand]
    private void BrowseAccessories() => BrowseAccessoriesRequested?.Invoke();

    private void BuildChoices()
    {
        Characters.Clear();
        foreach (var entry in CharacterCatalog.All)
        {
            Characters.Add(new CharacterOptionViewModel(entry.Id, entry.Name, Choose));
        }

        BuildOutfit(_settings.Current.Character);

        Installed.Clear();
        foreach (var entry in CharacterCatalog.All.Where(entry => !entry.BuiltIn))
        {
            Installed.Add(new InstalledPackViewModel(entry.Id, entry.Name, "Character", RemoveInstalled));
        }

        foreach (var entry in CharacterCatalog.Accessories.Where(entry => !entry.BuiltIn))
        {
            Installed.Add(new InstalledPackViewModel(entry.Id, entry.Name, $"Accessory · {OutfitSlots.TitleOf(entry.Slot)}", RemoveInstalled));
        }

        OnPropertyChanged(nameof(HasInstalled));
    }

    /// <summary>
    /// The outfit picker for one character. What comes with it is listed first, under its own heading, and the other
    /// accessories after it. Only one accessory can be worn per slot, whichever group it is in, so a slot's "None" sits in
    /// the first row shown for that slot rather than in both.
    /// </summary>
    private void BuildOutfit(string characterId)
    {
        var character = CharacterCatalog.Find(characterId);
        _outfitFor = characterId;

        var included = new List<OutfitSlotViewModel>();
        var others = new List<OutfitSlotViewModel>();
        foreach (var slot in OutfitSlots.All)
        {
            var all = CharacterCatalog.AccessoriesFor(slot);
            var own = all.Where(entry => character.Included.Contains(entry.Id)).ToList();
            var rest = all.Except(own).ToList();

            OutfitSlotViewModel Row(bool withNone, IEnumerable<AccessoryEntry> entries)
            {
                var options = new List<AccessoryOptionViewModel>();
                if (withNone)
                {
                    options.Add(new AccessoryOptionViewModel(null, "None", null, id => ChooseAccessory(slot, id)));
                }

                options.AddRange(entries.Select(entry => new AccessoryOptionViewModel(entry.Id, entry.Name, entry.Pack, id => ChooseAccessory(slot, id))));
                return new OutfitSlotViewModel(slot, options);
            }

            if (own.Count > 0)
            {
                included.Add(Row(withNone: true, own));
                if (rest.Count > 0)
                {
                    others.Add(Row(withNone: false, rest));
                }
            }
            else if (rest.Count > 0)
            {
                others.Add(Row(withNone: true, rest));
            }
        }

        Outfit.Clear();
        OutfitSections.Clear();
        foreach (var row in included.Concat(others))
        {
            Outfit.Add(row);
        }

        if (included.Count > 0)
        {
            OutfitSections.Add(new OutfitSection($"Included with {character.Name}", included));
            if (others.Count > 0)
            {
                OutfitSections.Add(new OutfitSection("More accessories", others));
            }
        }
        else if (others.Count > 0)
        {
            OutfitSections.Add(new OutfitSection("", others));
        }
    }

    /// <summary>The catalogue changed: rebuild what can be chosen, and mark what is chosen now.</summary>
    private void RefreshChoices()
    {
        BuildChoices();
        Load(_settings.Current);
        RaiseVisibilityChanged();
    }

    private void RemoveInstalled(string id)
    {
        PackInstaller.Uninstall(id, _location.CharactersDirectory);
        var stillThere = Directory.Exists(Path.Combine(_location.CharactersDirectory, id));
        CharacterCatalog.Configure(_location.CharactersDirectory);

        // If something is holding its files open, say so instead of letting the Remove button seem to do nothing.
        InstalledMessage = stillThere ? "That couldn't be removed just now. Close anything using its files and try again." : "";
    }

    /// <summary>Shown under the installed list when a removal didn't work.</summary>
    [ObservableProperty]
    public partial string InstalledMessage { get; set; } = "";

    public IReadOnlyList<PresetOptionViewModel> Presets { get; }

    public IReadOnlyList<ThemePreference> Themes { get; } = Enum.GetValues<ThemePreference>();

    public IReadOnlyList<MessageStyle> MessageStyles { get; } = Enum.GetValues<MessageStyle>();

    public bool StartupSupported => _startup.IsSupported;

    public string VersionText { get; } = $"HeySip {DiagnosticsService.AppVersion}";

    /// <summary>Shown under the characters when the chosen one isn't installed.</summary>
    [ObservableProperty]
    public partial string CharacterNote { get; set; } = "";

    /// <summary>Whether a chime plays for reminders and a gulp for drinks.</summary>
    [ObservableProperty]
    public partial bool SoundsOn { get; set; }

    /// <summary>Whether HeySip may go online: to check for updates and notices, and for the Characters page to look for new characters.</summary>
    [ObservableProperty]
    public partial bool OnlineOn { get; set; }

    /// <summary>Said under the switch: exactly what is contacted and what is sent.</summary>
    public string OnlineNote { get; } =
        "HeySip checks a fixed address for updates and notices when it starts and about once a day, and the Characters page asks for the list of characters when it opens. "
        + "It sends only the app version: no name, settings or drink data. Turn this off and nothing is contacted.";

    /// <summary>How loud, 0 to 100.</summary>
    [ObservableProperty]
    public partial double SoundVolume { get; set; }

    [RelayCommand]
    private void TestSound() => _sounds.PlayTest();

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial decimal? IntervalMinutes { get; set; }

    [ObservableProperty]
    public partial decimal? SnoozeMinutes { get; set; }

    [ObservableProperty]
    public partial TimeSpan? ActiveStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? ActiveEnd { get; set; }

    [ObservableProperty]
    public partial ZoneOption? Zone { get; set; }

    [ObservableProperty]
    public partial decimal? DailyGoal { get; set; }

    [ObservableProperty]
    public partial decimal? GlassSize { get; set; }

    [ObservableProperty]
    public partial bool Escalate { get; set; }

    [ObservableProperty]
    public partial bool PauseWhenAway { get; set; }

    [ObservableProperty]
    public partial bool RemindLessAfterGoal { get; set; }

    [ObservableProperty]
    public partial ThemePreference Theme { get; set; }

    [ObservableProperty]
    public partial MessageStyle Messages { get; set; }

    [ObservableProperty]
    public partial decimal? Monitor { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithOs { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingClear { get; set; }

    [ObservableProperty]
    public partial string DiagnosticsStatus { get; set; } = "";

    [ObservableProperty]
    public partial string StartupMessage { get; set; } = "";

    /// <summary>What the user typed into the search box. Empty shows every setting.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    partial void OnSearchTextChanged(string value) => RaiseVisibilityChanged();

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    // One "Show" property per setting (named Show + the catalog key), so the page can bind each row's visibility.
    public bool ShowName => Shows("Name");

    public bool ShowGoal => Shows("Goal");

    public bool ShowGlass => Shows("Glass");

    public bool ShowInterval => Shows("Interval");

    public bool ShowSnooze => Shows("Snooze");

    public bool ShowHours => Shows("Hours");

    public bool ShowPresets => Shows("Presets");

    public bool ShowDays => Shows("Days");

    public bool ShowZone => Shows("Zone");

    public bool ShowEscalate => Shows("Escalate");

    public bool ShowAfterGoal => Shows("AfterGoal");

    public bool ShowAway => Shows("Away");

    public bool ShowTest => Shows("Test");

    public bool ShowCharacter => Shows("Character");

    public bool ShowOutfit => Shows("Outfit") && Outfit.Count > 0;

    public bool ShowSounds => Shows("Sounds");

    public bool ShowOnline => Shows("Online");

    public bool ShowMessages => Shows("Messages");

    public bool ShowMonitor => Shows("Monitor");

    public bool ShowTheme => Shows("Theme");

    public bool ShowTray => Shows("Tray");

    public bool ShowStartup => Shows("Startup") && StartupSupported;

    public bool ShowHelp => Shows("Help");

    public bool ShowData => Shows("Data");

    // Rows that sit side by side hide together, so no empty gap is left behind.
    public bool ShowAmountsRow => ShowGoal || ShowGlass;

    public bool ShowTimingRow => ShowInterval || ShowSnooze;

    public bool ShowStyleRow => ShowMessages || ShowMonitor;

    // A card shows while any of its settings does.
    public bool ShowAboutCard => ShowName || ShowAmountsRow;

    public bool ShowRemindersCard => ShowTimingRow || ShowPresets || ShowHours || ShowDays || ShowZone || ShowEscalate || ShowAfterGoal || ShowAway || ShowTest;

    public bool ShowCompanionCard => ShowCharacter || ShowOnline || ShowOutfit || ShowSounds || ShowStyleRow;

    public bool ShowAppCard => ShowTheme || ShowTray || ShowStartup;

    public bool ShowHelpCard => ShowHelp;

    public bool ShowDataCard => ShowData;

    public bool NoResults => !(ShowAboutCard || ShowRemindersCard || ShowCompanionCard || ShowAppCard || ShowHelpCard || ShowDataCard);

    [RelayCommand]
    private void TestReminder() => _reminders.Trigger();

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        DiagnosticsStatus = await _clipboard.SetTextAsync(_diagnostics.BuildReport())
            ? "Copied. Paste it into a bug report; it doesn't include your name or drink history."
            : "Couldn't reach the clipboard.";
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        DiagnosticsStatus = _shell.OpenFolder(_diagnostics.DataFolder) ? "" : "Couldn't open the folder.";

    [RelayCommand]
    private void AskToClear() => IsConfirmingClear = true;

    [RelayCommand]
    private void CancelClear() => IsConfirmingClear = false;

    [RelayCommand]
    private void ConfirmClear()
    {
        _hydration.ClearHistory();
        IsConfirmingClear = false;
    }

    private bool Shows(string key) => SettingsCatalog.Matches(key, SearchText);

    private static readonly string[] VisibilityProperties =
    [
        nameof(ShowName), nameof(ShowGoal), nameof(ShowGlass), nameof(ShowInterval), nameof(ShowSnooze), nameof(ShowHours),
        nameof(ShowDays), nameof(ShowZone), nameof(ShowEscalate), nameof(ShowAway), nameof(ShowPresets), nameof(ShowAfterGoal), nameof(ShowTest), nameof(ShowCharacter), nameof(ShowOutfit), nameof(ShowOnline), nameof(ShowSounds),
        nameof(ShowMessages), nameof(ShowMonitor), nameof(ShowTheme), nameof(ShowTray), nameof(ShowStartup), nameof(ShowHelp),
        nameof(ShowData), nameof(ShowAmountsRow), nameof(ShowTimingRow), nameof(ShowStyleRow), nameof(ShowAboutCard),
        nameof(ShowRemindersCard), nameof(ShowCompanionCard), nameof(ShowAppCard), nameof(ShowHelpCard), nameof(ShowDataCard),
        nameof(NoResults),
    ];

    private void RaiseVisibilityChanged()
    {
        foreach (var name in VisibilityProperties)
        {
            OnPropertyChanged(name);
        }
    }

    private void ApplyPreset(WorkHoursPreset preset) => _settings.Update(preset.Apply);

    private void ChooseAccessory(AccessorySlot slot, string? id) => _settings.Update(current => current.WithAccessory(slot, id));

    private void Choose(string id) => _settings.Update(current => current with { Character = id });

    private void Load(AppSettings s)
    {
        _loading = true;
        try
        {
            Name = s.Name;
            IntervalMinutes = s.IntervalMinutes;
            SnoozeMinutes = s.SnoozeMinutes;
            ActiveStart = s.ActiveStart.ToTimeSpan();
            ActiveEnd = s.ActiveEnd.ToTimeSpan();
            Zone = Zones.FirstOrDefault(zone => zone.Id == s.TimeZoneId) ?? Zones[0];
            DailyGoal = s.DailyGoalMilliliters;
            GlassSize = s.GlassMilliliters;
            Escalate = s.Escalate;
            PauseWhenAway = s.PauseWhenAway;
            RemindLessAfterGoal = s.RemindLessAfterGoal;
            Theme = s.Theme;
            Messages = s.Messages;
            Monitor = s.Monitor;
            CloseToTray = s.CloseToTray;
            OnlineOn = s.OnlineCharacters;
            SoundsOn = s.SoundEnabled;
            SoundVolume = s.SoundVolume;
            StartWithOs = s.StartWithOs;
            foreach (var day in Days)
            {
                day.IsOn = s.ActiveDays.Contains(day.Day);
            }

            foreach (var day in WeekendDays)
            {
                day.IsOn = s.WeekendDays.Contains(day.Day);
            }

            foreach (var character in Characters)
            {
                character.IsSelected = character.Id == s.Character;
            }

            foreach (var slot in Outfit)
            {
                // A saved accessory that isn't installed any more counts as "None", so something is always selected.
                // A slot can have two rows (what comes with the character, and the rest), so look through both.
                var saved = s.Outfit.GetValueOrDefault(slot.Key);
                if (saved is not null && !Outfit.Any(row => row.Key == slot.Key && row.Options.Any(option => option.Id == saved)))
                {
                    saved = null;
                }

                foreach (var option in slot.Options)
                {
                    option.IsSelected = option.Id == saved;
                    option.Character = s.Character;
                }
            }

            // A saved character whose pack is gone shows as the default one; say so instead of leaving nothing selected.
            CharacterNote = CharacterCatalog.Contains(s.Character)
                ? ""
                : $"The character \"{s.Character}\" isn't installed, so {CharacterCatalog.Default.Name} is shown.";

            foreach (var preset in Presets)
            {
                preset.IsSelected = preset.Preset.Matches(s);
                preset.Description = preset.Preset.Describe(s);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnOwnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only real settings are saved. Search text, visibility flags and status messages are view state.
        if (e.PropertyName is nameof(IsConfirmingClear) or nameof(DiagnosticsStatus) or nameof(StartupMessage) or nameof(SearchText) or nameof(CharacterNote) or nameof(HasInstalled) or nameof(InstalledMessage)
            || e.PropertyName is { } name && VisibilityProperties.Contains(name))
        {
            return;
        }

        Push();
    }

    private void OnToggleChanged(object? sender, PropertyChangedEventArgs e) => Push();

    private bool IsUnlisted(string? zoneId) => zoneId is not null && Zones.All(zone => zone.Id != zoneId);

    private void Push()
    {
        if (_loading)
        {
            return;
        }

        var before = _settings.Current;
        var after = _settings.Update(current => current with
        {
            Name = Name,
            IntervalMinutes = Whole(IntervalMinutes, current.IntervalMinutes),
            SnoozeMinutes = Whole(SnoozeMinutes, current.SnoozeMinutes),
            ActiveStart = ActiveStart is { } start ? TimeOnly.FromTimeSpan(start) : current.ActiveStart,
            ActiveEnd = ActiveEnd is { } end ? TimeOnly.FromTimeSpan(end) : current.ActiveEnd,
            // A saved zone this computer doesn't list shows as "Automatic" but must not be overwritten by changing something else.
            TimeZoneId = Zone?.Id is null && IsUnlisted(current.TimeZoneId) ? current.TimeZoneId : Zone?.Id,
            DailyGoalMilliliters = Whole(DailyGoal, current.DailyGoalMilliliters),
            GlassMilliliters = Whole(GlassSize, current.GlassMilliliters),
            Escalate = Escalate,
            PauseWhenAway = PauseWhenAway,
            RemindLessAfterGoal = RemindLessAfterGoal,
            Theme = Theme,
            Messages = Messages,
            Monitor = Whole(Monitor, current.Monitor),
            CloseToTray = CloseToTray,
            OnlineCharacters = OnlineOn,
            SoundEnabled = SoundsOn,
            SoundVolume = (int)Math.Round(SoundVolume),
            StartWithOs = StartWithOs,
            ActiveDays = Days.Where(day => day.IsOn).Select(day => day.Day).ToArray(),
            WeekendDays = WeekendDays.Where(day => day.IsOn).Select(day => day.Day).ToArray(),
        });

        if (after.StartWithOs != before.StartWithOs && _startup.IsSupported)
        {
            try
            {
                _startup.SetEnabled(after.StartWithOs);
                StartupMessage = "";
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Windows (or a policy) refused. Put the switch back so it doesn't claim something that isn't true.
                _settings.Update(current => current with { StartWithOs = before.StartWithOs });
                StartupMessage = "Windows didn't allow that change, so it was left as it was.";
            }
        }

        if (after.IntervalMinutes != before.IntervalMinutes)
        {
            _reminders.Restart();
        }
    }

    private static int Whole(decimal? value, int fallback) => value is { } v ? (int)Math.Round(v) : fallback;
}
