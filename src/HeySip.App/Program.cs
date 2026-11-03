using Avalonia;
using Avalonia.Controls;
using HeySip.App.Composition;
using HeySip.App.Shared.Platform;
using HeySip.Core.Features.Diagnostics;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HeySip.App;

internal static class Program
{
    public const string MinimizedArgument = "--minimized";

    /// <summary>
    /// Startup: single-instance check, settings and database, background reminders, then the UI loop.
    /// If the data can't be opened, the UI loop still runs but shows an explanation instead of the app.
    /// Shutdown (after the UI loop ends): stop the host, which stops the reminder timer.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var packPath = PendingPack.FromArguments(args);
        using var instance = SingleInstance.Acquire();
        if (!instance.IsPrimary)
        {
            // Already running (probably in the tray): leave a pack file for it to show if we were opened with one, ask that
            // copy to show its window, then leave.
            if (packPath is not null)
            {
                PendingPack.Write(AppDataLocation.Resolve(), packPath);
            }

            instance.SignalPrimary();
            return 0;
        }

        var location = AppDataLocation.Resolve();
        using var host = AppHost.Build(location);
        var log = host.Services.GetRequiredService<DiagnosticLog>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Error("App", "Unhandled error.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("App", "Unobserved background error.", e.Exception);
            e.SetObserved();
        };

        Exception? startupError = null;
        try
        {
            host.Start();
            log.Info("App", $"HeySip {DiagnosticsService.AppVersion} started.");
        }
        catch (Exception exception)
        {
            // Startup is the outermost boundary: explain what happened in a window instead of exiting silently.
            log.Error("App", "HeySip couldn't open its data.", exception);
            startupError = exception;
        }

        var minimized = args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp(host.Services, new StartupOptions(minimized, startupError, instance, packPath))
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

        if (startupError is null)
        {
            // Continuations must not be posted to the UI dispatcher, which has stopped by now.
            Task.Run(() => host.StopAsync()).GetAwaiter().GetResult();
        }

        return 0;
    }

    /// <summary>Entry point for the Avalonia previewer and design tools.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null, StartupOptions.Design);

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? services, StartupOptions options) =>
        AppBuilder.Configure(() => new App(services, options))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
