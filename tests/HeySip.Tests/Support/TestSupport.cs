using HeySip.Core.Features.Hydration;
using HeySip.Core.Features.Settings;
using HeySip.Core.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace HeySip.Tests.Support;

/// <summary>A throwaway data folder, deleted on dispose.</summary>
public sealed class TempData : IDisposable
{
    public TempData()
    {
        Root = Path.Combine(Path.GetTempPath(), "heysip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Location = new AppDataLocation(Root);
    }

    public string Root { get; }

    public AppDataLocation Location { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Real settings and a real SQLite file under a fake clock, so tests exercise the actual code paths.</summary>
public sealed class CoreHarness : IDisposable
{
    public CoreHarness(DateTimeOffset? start = null)
    {
        Time = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Data = new TempData();
        Settings = new SettingsService(Data.Location);
        Settings.Load();
        Store = new IntakeStore(Data.Location.DatabasePath);
        Store.Initialize();
        Hydration = new HydrationService(Store, Settings, Time);
    }

    public FakeTimeProvider Time { get; }

    public TempData Data { get; }

    public SettingsService Settings { get; }

    public IntakeStore Store { get; }

    public HydrationService Hydration { get; }

    public void Dispose() => Data.Dispose();
}
