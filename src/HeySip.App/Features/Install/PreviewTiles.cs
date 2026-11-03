using HeySip.App.Shared.Controls;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.App.Features.Install;

/// <summary>The small pictures that show what a pack looks like: used by the install card and by the character browser.</summary>
public static class PreviewTiles
{
    /// <summary>
    /// A character is shown in four poses, wearing the accessories it comes with the way it will when it is new
    /// (<paramref name="bundled"/>, if it comes with any); an accessory is shown on the character you have now.
    /// </summary>
    public static IReadOnlyList<PreviewTileViewModel> For(Pack pack, string currentCharacter, IReadOnlyList<AccessoryPack>? bundled = null) => pack switch
    {
        CharacterPack character =>
        [
            new(DripPose.Idle, character, PackId.DefaultCharacter, DefaultLook(bundled), "Standing"),
            new(DripPose.Waving, character, PackId.DefaultCharacter, DefaultLook(bundled), "Waving"),
            new(DripPose.Cheering, character, PackId.DefaultCharacter, DefaultLook(bundled), "Cheering"),
            new(DripPose.Sleeping, character, PackId.DefaultCharacter, DefaultLook(bundled), "Sleeping"),
        ],
        AccessoryPack accessory =>
        [
            new(DripPose.Idle, null, currentCharacter, [accessory], "Standing"),
            new(DripPose.Waving, null, currentCharacter, [accessory], "Waving"),
            new(DripPose.Happy, null, currentCharacter, [accessory], "Happy"),
        ],
        _ => [],
    };

    private static IReadOnlyList<AccessoryPack>? DefaultLook(IReadOnlyList<AccessoryPack>? bundled) =>
        bundled is { Count: > 0 } ? PackBundle.DefaultLook(bundled) : null;

    /// <summary>The accessories that come with a character, each worn by that character and named underneath.</summary>
    public static IReadOnlyList<PreviewTileViewModel> Bundled(CharacterPack character, IReadOnlyList<AccessoryPack> accessories) =>
        [.. accessories.Select(accessory => new PreviewTileViewModel(DripPose.Idle, character, PackId.DefaultCharacter, [accessory], accessory.Header.Name))];

    /// <summary>"Plus 3 more accessories you can add", or what a character with nothing of its own works with; empty when there is nothing to say.</summary>
    public static string MoreText(int comesWith, int more) => (comesWith, more) switch
    {
        (_, <= 0) => "",
        (0, _) => $"Works with {more} {(more == 1 ? "accessory" : "accessories")} from the Accessories tab.",
        _ => $"Plus {more} more {(more == 1 ? "accessory" : "accessories")} you can add from the Accessories tab.",
    };
}
