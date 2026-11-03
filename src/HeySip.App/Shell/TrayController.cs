using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Control;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;

namespace HeySip.App.Shell;

/// <summary>The notification-area icon: quick actions, pause controls and a live tooltip.</summary>
public sealed class TrayController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly HydrationService _hydration;
    private readonly ReminderService _reminders;
    private readonly SettingsService _settings;
    private readonly Action _openMain;
    private readonly Action<ShellPage> _openPage;
    private readonly Action _quit;

    // What the menu and icon were last built from, so they are only rebuilt when one of these actually changes.
    // Replacing a native menu while the user has it open would close it under their cursor.
    private bool _menuPaused;
    private int _menuGlass;
    private string? _menuUpdate;
    private readonly ControlService? _control;
    private readonly Action<string>? _openUrl;
    private string _iconCharacter;

    public TrayController(
        Application application,
        HydrationService hydration,
        ReminderService reminders,
        SettingsService settings,
        Action openMain,
        Action<ShellPage> openPage,
        Action quit,
        ControlService? control = null,
        Action<string>? openUrl = null)
    {
        _control = control;
        _openUrl = openUrl;
        _hydration = hydration;
        _reminders = reminders;
        _settings = settings;
        _openMain = openMain;
        _openPage = openPage;
        _quit = quit;

        _iconCharacter = settings.Current.Character;
        _tray = new TrayIcon { Icon = RenderIcon(_iconCharacter), IsVisible = true };
        _tray.Clicked += (_, _) => _openMain();
        TrayIcon.SetIcons(application, [_tray]);

        _hydration.Changed += (_, _) => Refresh();
        _reminders.StatusChanged += () => Dispatcher.UIThread.Post(Refresh);
        _settings.Changed += (_, _) => Refresh();
        if (_control is not null)
        {
            _control.Changed += () => Dispatcher.UIThread.Post(Refresh);
        }

        Refresh();
    }

    /// <summary>Updates the tooltip, and rebuilds the menu or icon only if what they show has changed.</summary>
    public void Refresh()
    {
        var current = _settings.Current;
        var status = _reminders.Status();

        var next = ReminderStatusText.Brief(status, current, TimeProvider.System).ToLowerInvariant();
        _tray.ToolTipText = $"HeySip: {_hydration.TodayMilliliters()} / {current.DailyGoalMilliliters} ml ({next})";

        var update = _control is { UpdateAvailable: true } ? _control.Current?.LatestVersion?.ToString(3) : null;
        if (_tray.Menu is null || status.IsPaused != _menuPaused || current.GlassMilliliters != _menuGlass || update != _menuUpdate)
        {
            _menuPaused = status.IsPaused;
            _menuGlass = current.GlassMilliliters;
            _menuUpdate = update;
            _tray.Menu = BuildMenu(status, current);
        }

        if (current.Character != _iconCharacter)
        {
            _iconCharacter = current.Character;
            _tray.Icon = RenderIcon(_iconCharacter);
        }
    }

    public void Dispose() => _tray.Dispose();

    private NativeMenu BuildMenu(ReminderStatus status, AppSettings current)
    {
        var menu = new NativeMenu();
        if (_menuUpdate is { } version && _control?.Current?.UpdateUrl is { } updateUrl)
        {
            menu.Add(Item($"Update available: HeySip {version}", () => _openUrl?.Invoke(updateUrl.AbsoluteUri)));
        }

        menu.Add(Item("Open HeySip", _openMain));
        menu.Add(Item("Stats", () => _openPage(ShellPage.Stats)));
        menu.Add(Item("History", () => _openPage(ShellPage.History)));
        menu.Add(Item("Settings", () => _openPage(ShellPage.Settings)));
        menu.Add(new NativeMenuItemSeparator());

        // Logging a drink also answers any reminder, so there is nothing else to do here.
        menu.Add(Item($"I drank a glass 💧 ({current.GlassMilliliters} ml)", () => _hydration.LogGlass()));
        menu.Add(Item("Remind me now", _reminders.Trigger));
        menu.Add(new NativeMenuItemSeparator());

        if (status.IsPaused)
        {
            menu.Add(Item("Resume reminders", _reminders.Resume));
        }
        else
        {
            var pause = new NativeMenuItem("Pause reminders") { Menu = new NativeMenu() };
            pause.Menu.Add(Item("For 30 minutes", () => _reminders.Pause(TimeSpan.FromMinutes(30))));
            pause.Menu.Add(Item("For 1 hour", () => _reminders.Pause(TimeSpan.FromHours(1))));
            pause.Menu.Add(Item("For 2 hours", () => _reminders.Pause(TimeSpan.FromHours(2))));
            pause.Menu.Add(Item("Until I resume", () => _reminders.Pause(null)));
            menu.Add(pause);
        }

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit HeySip", _quit));
        return menu;
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Paints the mascot into a small bitmap so the tray needs no icon file.</summary>
    private static WindowIcon RenderIcon(string character)
    {
        var drip = new DripControl { Character = character, IsAnimated = false, Width = 64, Height = 64 };
        drip.Measure(new Size(64, 64));
        drip.Arrange(new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        bitmap.Render(drip);
        return new WindowIcon(bitmap);
    }
}
