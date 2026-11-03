using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using HeySip.App.Features.Companion;
using HeySip.App.Features.Palette;
using HeySip.App.Shared.Audio;
using HeySip.App.Shared.Controls;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HeySip.App;

public sealed record StartupOptions(bool StartMinimized, Exception? StartupError = null, SingleInstance? Instance = null, string? PackToOpen = null)
{
    public static StartupOptions Design { get; } = new(false);
}

public partial class App : Application
{
    /// <summary>How often the shell refreshes countdowns, and checks for a new day.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    /// <summary>"Automatic" time zone is cached by .NET for the life of the process; re-read it this often so travelling works.</summary>
    private const int ZoneRefreshEveryTicks = 20;

    private readonly IServiceProvider? _services;
    private readonly StartupOptions _options;
    private TrayController? _tray;

    /// <summary>Used by the previewer, which has no composition root.</summary>
    public App()
        : this(null, StartupOptions.Design)
    {
    }

    public App(IServiceProvider? services, StartupOptions options)
    {
        _services = services;
        _options = options;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (_options.StartupError is { } error)
            {
                ShowStartupError(desktop, _services, error, _options.Instance);
            }
            else
            {
                StartShell(desktop, _services);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(ThemePreference theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    private void StartShell(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsService>();
        var reminders = services.GetRequiredService<ReminderService>();
        var log = services.GetRequiredService<DiagnosticLog>();

        // Installed character packs must be known before the pickers (built with the shell) list them.
        var location = services.GetRequiredService<AppDataLocation>();
        CharacterCatalog.Configure(location.CharactersDirectory, log);
        var shell = services.GetRequiredService<ShellViewModel>();

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            log.Error("UI", "Unexpected error in the user interface.", e.Exception);
            e.Handled = true;
        };

        ApplyTheme(settings.Current.Theme);
        settings.Changed += (_, updated) => Dispatcher.UIThread.Post(() => ApplyTheme(updated.Theme));

        var window = new MainWindow { DataContext = shell };

        // Services that need a window (the clipboard) find it through here. It isn't assigned to desktop.MainWindow,
        // because Avalonia would then show it on startup, even when HeySip was asked to start minimized.
        services.GetRequiredService<DesktopServices>().Owner = window;

        // Reopening keeps whatever page was open; only the first run starts on Today.
        void OpenMain(bool toToday = false)
        {
            if (toToday)
            {
                shell.Navigate(ShellPage.Today);
            }

            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }

        // The tray's page entries: bring the window forward, then show the page. Settings starts from every setting.
        void OpenPage(ShellPage page)
        {
            OpenMain();
            if (page == ShellPage.Settings)
            {
                shell.OpenSettings(null);
            }
            else
            {
                shell.Navigate(page);
            }
        }

        void Quit()
        {
            window.IsQuitting = true;
            _tray?.Dispose();
            desktop.Shutdown();
        }

        window.HideOnClose = settings.Current.CloseToTray;
        window.QuitRequested = Quit;
        services.GetRequiredService<PaletteCommands>().Quit = Quit;
        settings.Changed += (_, updated) => Dispatcher.UIThread.Post(() => window.HideOnClose = updated.CloseToTray);

        services.GetRequiredService<SoundService>().Attach();
        var companion = services.GetRequiredService<CompanionController>();
        companion.OpenMain = () => OpenMain(toToday: true);
        companion.Attach();
        _tray = new TrayController(
            this,
            services.GetRequiredService<Core.Features.Hydration.HydrationService>(),
            reminders,
            settings,
            () => OpenMain(),
            OpenPage,
            Quit,
            services.GetRequiredService<Core.Features.Control.ControlService>(),
            url => services.GetRequiredService<Shared.Platform.IShellLauncher>().OpenUrl(url));

        // Launching HeySip again while it sits in the tray brings this copy's window forward.
        _options.Instance?.StartListening(() => Dispatcher.UIThread.Post(() =>
        {
            OpenMain();

            // The other launch left a pack file for us to show.
            if (PendingPack.TryTake(location) is { } path)
            {
                shell.OpenPackFile(path);
            }
        }));

        KeepStartupEntryCurrent(services, settings, log);

        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TickInterval };
        timer.Tick += (_, _) =>
        {
            if (++ticks % ZoneRefreshEveryTicks == 0)
            {
                TimeZoneInfo.ClearCachedData();
            }

            shell.Tick();
            _tray?.Refresh();
        };
        timer.Start();

        shell.Onboarding.ShowIfFirstRun();
        shell.Onboarding.Completed += () => reminders.Trigger();

        var firstRun = !settings.Current.Onboarded;
        if (_options.PackToOpen is { } packToOpen)
        {
            OpenMain();
            shell.OpenPackFile(packToOpen);
        }
        else if (firstRun || !_options.StartMinimized)
        {
            OpenMain(toToday: true);
        }
    }

    /// <summary>
    /// The "start with Windows" entry stores the exe's path. If the user moved or updated the app, rewrite it so it
    /// doesn't point at a folder that no longer exists.
    /// </summary>
    private static void KeepStartupEntryCurrent(IServiceProvider services, SettingsService settings, DiagnosticLog log)
    {
        var startup = services.GetRequiredService<IStartupRegistration>();
        if (!startup.IsSupported || !settings.Current.StartWithOs)
        {
            return;
        }

        try
        {
            startup.SetEnabled(true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log.Warning("Startup", "Couldn't refresh the start-with-Windows entry.", exception);
        }
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services, Exception error, SingleInstance? instance)
    {
        var location = services.GetRequiredService<AppDataLocation>();
        StartupErrorWindow? window = null;

        // The failed start left the app half built, so trying again means a fresh process. This one lets go of the
        // single-instance lock first, otherwise the new copy would take it for a second launch and exit.
        void Retry()
        {
            instance?.Dispose();
            if (Environment.ProcessPath is { } exe)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Nothing more can be done from here; closing leaves the user to start HeySip themselves.
                }
            }

            window?.Close();
        }

        window = new StartupErrorWindow(error.Message, location.RootDirectory, error.ToString(), Retry);
        window.Closed += (_, _) => desktop.Shutdown();
        window.Show();
    }
}
