using HeySip.App.Features.Browse;
using HeySip.App.Features.Companion;
using HeySip.App.Features.Control;
using HeySip.App.Features.Day;
using HeySip.App.Features.History;
using HeySip.App.Features.Install;
using HeySip.App.Features.Palette;
using HeySip.App.Features.Onboarding;
using HeySip.App.Features.Settings;
using HeySip.App.Features.Stats;
using HeySip.App.Features.Today;
using HeySip.App.Shared.Audio;
using HeySip.App.Shared.Platform;
using HeySip.App.Shell;
using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.Features.Control;
using HeySip.Core.Features.Diagnostics;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HeySip.App.Composition;

/// <summary>The composition root. Everything is a singleton: there is one log, one scheduler and one window set.</summary>
public static class AppHost
{
    /// <summary>The product name sent with each online request: the version and nothing else, no identifier.</summary>
    private static string UserAgent => $"HeySip/{HeySip.App.Shared.AppVersion.Current.ToString(2)}";

    public static IHost Build(AppDataLocation location, Action<IServiceCollection>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = "HeySip",
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(10);
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });

        AddCore(builder.Services, location);
        AddUi(builder.Services);
        builder.Services.AddHostedService<ReminderHostedService>();
        builder.Services.AddHostedService<ControlHostedService>();
        overrides?.Invoke(builder.Services);
        return builder.Build();
    }

    private static void AddCore(IServiceCollection services, AppDataLocation location)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(location);
        services.AddSingleton(new Random());
        services.AddSingleton(sp => new DiagnosticLog(sp.GetRequiredService<TimeProvider>(), location.LogDirectory));
        services.AddSingleton<SettingsService>();
        services.AddSingleton(new IntakeStore(location.DatabasePath));
        services.AddSingleton<HydrationService>();
        services.AddSingleton<MessageBook>();
        services.AddSingleton<ReminderScheduler>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<DiagnosticsService>();

        // The quiet check for updates and notices. It is off whenever the user turns online lookups off, and sends only the version.
        services.AddSingleton(sp => new ControlService(
            ControlLocation.Options(Environment.GetEnvironmentVariable(ControlLocation.OverrideVariable)),
            location,
            () => sp.GetRequiredService<SettingsService>().Current.OnlineCharacters,
            HeySip.App.Shared.AppVersion.Current,
            sp.GetRequiredService<TimeProvider>(),
            UserAgent,
            sp.GetRequiredService<DiagnosticLog>()));

        services.AddSingleton<IPresenceProbe>(_ => OperatingSystem.IsWindows() ? new WindowsPresenceProbe() : new AlwaysAvailable());
        services.AddSingleton<IStartupRegistration>(_ =>
            OperatingSystem.IsWindows() && Environment.ProcessPath is { } executable && !IsDotnetHost(executable)
                ? new WindowsStartupRegistration(executable)
                : new UnsupportedStartupRegistration());
    }

    /// <summary>When run with <c>dotnet run</c>, the process is dotnet.exe; registering that for start-at-login would launch nothing useful.</summary>
    private static bool IsDotnetHost(string executable) =>
        Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    private static void AddUi(IServiceCollection services)
    {
        services.AddSingleton<DesktopServices>();
        services.AddSingleton<IClipboardService>(sp => sp.GetRequiredService<DesktopServices>());
        services.AddSingleton<IShellLauncher>(sp => sp.GetRequiredService<DesktopServices>());
        services.AddSingleton<ISoundOutput, SoundFlowOutput>();
        services.AddSingleton<SoundService>();
        services.AddSingleton<CompanionViewModel>();
        services.AddSingleton<CompanionController>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<StatsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        // The online list is wherever the signed control file says, and nowhere else: there is no list address in the app. With none
        // given it adds nothing and says nothing. A version the file calls unsupported, or a file that switches the list off, stops
        // looking online.
        services.AddSingleton<IStoreSource>(sp =>
        {
            var control = sp.GetRequiredService<ControlService>();
            var settings = sp.GetRequiredService<SettingsService>();
            return new LayeredStoreSource(
                new ControlledStoreSource(control, UserAgent, sp.GetRequiredService<TimeProvider>()),
                new BundledStoreSource(),
                () => settings.Current.OnlineCharacters && !control.BelowMinimum && control.OnlineStoreEnabled);
        });
        services.AddSingleton(StorePayments.Resolve(Environment.GetEnvironmentVariable(StorePayments.OverrideVariable)));
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<InstallPreviewViewModel>();
        services.AddSingleton<PaletteCommands>();
        services.AddSingleton<DayViewModelFactory>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<RemovalBannerViewModel>();
        services.AddSingleton<NoticeBannerViewModel>();
        services.AddSingleton<ShellViewModel>();
    }
}

/// <summary>Explicit background lifecycle. Start: load settings, open the log, start reminders. Stop: stop reminders.</summary>
public sealed class ReminderHostedService(SettingsService settings, IntakeStore store, ReminderService reminders) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        settings.Load();
        store.Initialize();
        reminders.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => reminders.StopAsync();
}
