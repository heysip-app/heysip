using HeySip.Core.Features.Scheduling;
using HeySip.Core.Features.Settings;

namespace HeySip.Core.Features.Companion;

/// <summary>The two moments that have a sound.</summary>
public enum SoundKind
{
    /// <summary>A reminder has appeared.</summary>
    Reminder,

    /// <summary>A drink was logged.</summary>
    Drank,
}

/// <summary>
/// When a sound may play and how loud. The rule is the user's, and nothing a character pack carries can change it: sounds
/// are off when switched off, off at volume zero, and off while reminders are paused or quiet.
/// </summary>
public static class SoundPolicy
{
    public static bool ShouldPlay(AppSettings settings, ReminderStatus status) =>
        settings.SoundEnabled && settings.SoundVolume > 0 && !status.IsPaused;

    /// <summary>
    /// The playback gain for the volume setting. Loudness is heard roughly on a curve, so the slider is squared: 50 sounds
    /// about half as loud as 100, and the lower half of the slider is still usefully fine-grained.
    /// </summary>
    public static float Gain(AppSettings settings)
    {
        var level = Math.Clamp(settings.SoundVolume, 0, 100) / 100f;
        return level * level;
    }
}
