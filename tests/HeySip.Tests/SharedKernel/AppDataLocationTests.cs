using HeySip.Core.SharedKernel;

namespace HeySip.Tests.SharedKernel;

public sealed class AppDataLocationTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "heysip-loc-" + Guid.NewGuid().ToString("N"));

    public AppDataLocationTests() => Directory.CreateDirectory(_parent);

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    [Fact]
    public void A_fresh_install_gets_a_HeySip_folder()
    {
        var location = AppDataLocation.ResolveUnder(_parent);

        Assert.Equal(Path.Combine(_parent, "HeySip"), location.RootDirectory);
        Assert.True(Directory.Exists(location.RootDirectory));
    }

    [Fact]
    public void Data_from_the_old_HydrateBuddy_folder_is_carried_over_once()
    {
        var legacy = Path.Combine(_parent, "HydrateBuddy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{ \"name\": \"Alex\" }");
        File.WriteAllText(Path.Combine(legacy, "hydratebuddy.db"), "db");

        var location = AppDataLocation.ResolveUnder(_parent);

        Assert.False(Directory.Exists(legacy));
        Assert.Contains("Alex", File.ReadAllText(location.SettingsPath));
        Assert.Equal("db", File.ReadAllText(location.DatabasePath));
    }

    [Fact]
    public void An_existing_HeySip_folder_is_never_overwritten()
    {
        var current = Path.Combine(_parent, "HeySip");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(current, "settings.json"), "new");
        var legacy = Path.Combine(_parent, "HydrateBuddy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "old");

        var location = AppDataLocation.ResolveUnder(_parent);

        Assert.Equal("new", File.ReadAllText(location.SettingsPath));
    }

    [Fact]
    public void Migration_happens_once_and_never_overwrites_later_data()
    {
        var legacy = Path.Combine(_parent, "HydrateBuddy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "from the old app");
        AppDataLocation.ResolveUnder(_parent);
        File.WriteAllText(Path.Combine(_parent, "HeySip", "settings.json"), "changed since");

        Directory.CreateDirectory(legacy); // even if an old folder appears again
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "stale copy");
        var second = AppDataLocation.ResolveUnder(_parent);

        Assert.Equal("changed since", File.ReadAllText(second.SettingsPath));
    }
}
