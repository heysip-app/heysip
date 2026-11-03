using HeySip.Core.Features.Companion;
using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;
using HeySip.Tests.Support;

namespace HeySip.Tests.Features.Settings;

public sealed class SoundSettingTests : IDisposable
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

    private static ReminderStatus Status(bool paused = false) =>
        new(paused, paused ? DateTimeOffset.UtcNow.AddHours(1) : null, null, null, OutsideActiveHours: false);

    [Fact]
    public void Sounds_are_on_but_gentle_to_begin_with()
    {
        var settings = new AppSettings();

        Assert.True(settings.SoundEnabled);
        Assert.Equal(40, settings.SoundVolume);
    }

    [Fact]
    public void A_file_from_before_sounds_gets_the_gentle_defaults()
    {
        var loaded = LoadFile("{ \"SchemaVersion\": 2, \"Name\": \"Kai\" }");

        Assert.True(loaded.SoundEnabled);
        Assert.Equal(40, loaded.SoundVolume);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(400, 100)]
    public void The_volume_is_kept_between_zero_and_one_hundred(int saved, int expected)
    {
        var loaded = LoadFile($"{{ \"SchemaVersion\": 2, \"SoundVolume\": {saved} }}");

        Assert.Equal(expected, loaded.SoundVolume);
    }

    [Fact]
    public void The_choice_survives_a_restart()
    {
        var first = new SettingsService(_data.Location);
        first.Load();
        first.Update(s => s with { SoundEnabled = false, SoundVolume = 75 });

        var second = new SettingsService(_data.Location);
        second.Load();

        Assert.False(second.Current.SoundEnabled);
        Assert.Equal(75, second.Current.SoundVolume);
    }

    // ---- when a sound may play

    [Fact]
    public void A_sound_plays_when_it_is_on_and_audible_and_reminders_are_not_paused()
    {
        Assert.True(SoundPolicy.ShouldPlay(new AppSettings(), Status()));
    }

    [Fact]
    public void A_sound_never_plays_when_switched_off_or_at_volume_zero()
    {
        Assert.False(SoundPolicy.ShouldPlay(new AppSettings { SoundEnabled = false }, Status()));
        Assert.False(SoundPolicy.ShouldPlay(new AppSettings { SoundVolume = 0 }, Status()));
    }

    [Fact]
    public void A_sound_never_plays_while_reminders_are_paused_or_in_quiet_time()
    {
        Assert.False(SoundPolicy.ShouldPlay(new AppSettings(), Status(paused: true)));
        Assert.False(SoundPolicy.ShouldPlay(new AppSettings { SoundVolume = 100 }, Status(paused: true)));
    }

    [Fact]
    public void The_gain_follows_a_curve_so_the_lower_half_of_the_slider_is_fine_grained()
    {
        Assert.Equal(0f, SoundPolicy.Gain(new AppSettings { SoundVolume = 0 }));
        Assert.Equal(1f, SoundPolicy.Gain(new AppSettings { SoundVolume = 100 }));
        Assert.Equal(0.25f, SoundPolicy.Gain(new AppSettings { SoundVolume = 50 }), precision: 4);
        Assert.Equal(0.16f, SoundPolicy.Gain(new AppSettings { SoundVolume = 40 }), precision: 4);
    }

    [Fact]
    public void A_louder_setting_is_never_quieter()
    {
        var gains = Enumerable.Range(0, 101).Select(volume => SoundPolicy.Gain(new AppSettings { SoundVolume = volume })).ToList();

        Assert.Equal(gains.Order(), gains);
        Assert.All(gains, gain => Assert.InRange(gain, 0f, 1f));
    }
}
