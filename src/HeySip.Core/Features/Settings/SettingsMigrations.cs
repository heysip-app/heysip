using HeySip.Core.Features.Companion.Packs;

namespace HeySip.Core.Features.Settings;

/// <summary>
/// Brings a settings file written by an older build up to the current layout, one version at a time. Adding a plain new
/// setting needs no step (it just gets its default); a step is for when an existing setting changes meaning or is renamed.
/// </summary>
public static class SettingsMigrations
{
    /// <summary><c>Steps[n]</c> upgrades a file from version n to n + 1.</summary>
    public static IReadOnlyList<Func<AppSettings, AppSettings>> Steps { get; } =
    [
        settings => settings, // 0 -> 1: files from before versions existed already have the version 1 layout

        // 1 -> 2: the character was one of four names ("Minty"); it is now the id of a character pack ("heysip.minty").
        settings => settings with { Character = PackId.FromLegacyName(settings.Character) },

        // 2 -> 3: the outfit was one for everybody; each character now keeps its own. The old one goes to the character in use.
        settings =>
        {
            if (settings.LegacyOutfit is not { Count: > 0 } old)
            {
                return settings with { LegacyOutfit = null };
            }

            var character = PackId.IsValid(settings.Character) ? settings.Character : PackId.DefaultCharacter;
            var outfits = new Dictionary<string, Dictionary<string, string>>(settings.Outfits) { [character] = new(old) };
            return settings with { Outfits = outfits, LegacyOutfit = null };
        },
    ];

    public static AppSettings Upgrade(AppSettings settings, int fromVersion, IReadOnlyList<Func<AppSettings, AppSettings>>? steps = null)
    {
        steps ??= Steps;
        for (var version = Math.Max(0, fromVersion); version < steps.Count; version++)
        {
            settings = steps[version](settings);
        }

        return settings;
    }
}
