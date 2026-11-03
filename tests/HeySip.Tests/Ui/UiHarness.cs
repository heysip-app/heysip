using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HeySip.App.Composition;
using HeySip.App.Shell;
using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: AvaloniaTestApplication(typeof(HeySip.Tests.Ui.TestAppBuilder))]

// The character catalogue is shared by the whole app, so tests that install or remove packs must not overlap with one another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HeySip.Tests.Ui;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

/// <summary>Runs the real composition root and main window headlessly, against a temporary data folder.</summary>
public sealed class UiHarness : IDisposable
{
    private UiHarness(Action<IServiceCollection>? overrides)
    {
        Data = new TempData();
        Host = AppHost.Build(Data.Location, services =>
        {
            services.AddSingleton<IPresenceProbe, AlwaysAvailable>();
            overrides?.Invoke(services);
        });
        Host.Services.GetRequiredService<SettingsService>().Load();
        Host.Services.GetRequiredService<IntakeStore>().Initialize();
        Shell = Host.Services.GetRequiredService<ShellViewModel>();
        Window = new MainWindow { DataContext = Shell, Width = 1040, Height = 760 };

        // Headless runs never finish animations, so a cross-fade would keep the old page attached forever.
        foreach (var host in Window.GetLogicalDescendants().OfType<TransitioningContentControl>())
        {
            host.PageTransition = null;
        }
    }

    public TempData Data { get; }

    public IHost Host { get; }

    public ShellViewModel Shell { get; }

    public MainWindow Window { get; }

    public T Get<T>()
        where T : notnull => Host.Services.GetRequiredService<T>();

    public static UiHarness Create(Action<IServiceCollection>? overrides = null) => new(overrides);

    public void Show()
    {
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Renders the window to a PNG so a person (or a reviewer) can look at it.</summary>
    public void Screenshot(string path)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = Window.CaptureRenderedFrame();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(frame, path);
    }

    /// <summary>Saves a rendered frame as PNG. The non-obsolete overload needs encoder options we don't need.</summary>
    public static void SavePng(Avalonia.Media.Imaging.Bitmap? frame, string path)
    {
#pragma warning disable CS0618
        frame?.Save(path);
#pragma warning restore CS0618
    }

    public void Dispose()
    {
        Window.IsQuitting = true;
        Window.Close();
        Host.Dispose();
        Data.Dispose();
    }
}
