using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class OnlineSettingTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    private AppSettings LoadFile(string json)
    {
        File.WriteAllText(_data.Location.SettingsPath, json);
        var settings = new SettingsService(_data.Location);
        settings.Load();
        return settings.Current;
    }

    [Fact]
    public void Looking_online_is_on_to_begin_with()
    {
        Assert.True(new AppSettings().OnlineCharacters);
    }

    [Fact]
    public void A_file_from_before_the_switch_gets_the_default()
    {
        Assert.True(LoadFile("{ \"SchemaVersion\": 2, \"Name\": \"Kai\" }").OnlineCharacters);
    }

    [Fact]
    public void Turning_it_off_survives_a_restart()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s with { OnlineCharacters = false });

        var second = new SettingsService(_data.Location);
        second.Load();

        Assert.False(second.Current.OnlineCharacters);
    }

    [Fact]
    public void A_file_that_says_off_is_respected()
    {
        Assert.False(LoadFile("{ \"SchemaVersion\": 2, \"OnlineCharacters\": false }").OnlineCharacters);
    }
}
