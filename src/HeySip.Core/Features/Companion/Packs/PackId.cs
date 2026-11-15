using System.Text.RegularExpressions;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The identity of a pack: lower-case <c>author.name</c>, such as <c>heysip.drip</c>. It is what settings store.</summary>
public static partial class PackId
{
    /// <summary>The id of the character used when nothing else is chosen, or when the chosen one can't be found.</summary>
    public const string DefaultCharacter = Drip;

    public const string Drip = "heysip.drip";
    public const string Minty = "heysip.minty";
    public const string Sunny = "heysip.sunny";
    public const string Blossom = "heysip.blossom";

    /// <summary>Ids starting with this belong to the characters built into the app; installed packs may not use it.</summary>
    public const string BuiltInPrefix = "heysip.";

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)+$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? id) =>
        id is not null && id.Length >= PackLimits.MinIdLength && id.Length <= PackLimits.MaxIdLength && Pattern().IsMatch(id);

    /// <summary>The id a character had before packs existed, when it was one of four names. Anything unknown becomes the default.</summary>
    public static string FromLegacyName(string? name) => IsValid(name?.Trim()) ? name!.Trim() : name?.Trim().ToLowerInvariant() switch
    {
        "drip" => Drip,
        "minty" => Minty,
        "sunny" => Sunny,
        "blossom" => Blossom,
        _ => DefaultCharacter,
    };
}
