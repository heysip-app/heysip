using System.Runtime.CompilerServices;
using HeySip.Core.Features.Companion.Packs;

namespace HeySip.App.Shared.Controls;

/// <summary>Keeps characters prepared for drawing. Which pack an id means is decided by <see cref="CharacterCatalog"/>.</summary>
internal static class CharacterLibrary
{
    private static readonly ConditionalWeakTable<CharacterPack, PreparedCharacter> Characters = [];
    private static readonly ConditionalWeakTable<AccessoryPack, PreparedAccessory> Accessories = [];

    /// <summary>The prepared character for an id from settings; an id that can't be found gives the default character.</summary>
    public static PreparedCharacter For(string? id) => Prepare(CharacterCatalog.Find(id).Pack);

    public static PreparedCharacter Prepare(CharacterPack pack) => Characters.GetValue(pack, key => new PreparedCharacter(key));

    public static PreparedAccessory Prepare(AccessoryPack pack) => Accessories.GetValue(pack, key => new PreparedAccessory(key));

    /// <summary>The pack behind a character id (for tests and for showing its details).</summary>
    public static CharacterPack PackOf(string? id) => CharacterCatalog.Find(id).Pack;
}
