using System.Text.RegularExpressions;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>Orders pack versions the way semantic versioning does: 1.10.0 after 1.9.0, and 1.0.0-beta before 1.0.0.</summary>
public static partial class PackVersion
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();

    /// <summary>True for a version that is safe to use as a folder name and that the format allows, such as 1.2.3 or 1.2.3-beta.1.</summary>
    public static bool IsValid(string? version) => version is not null && Pattern().IsMatch(version);

    /// <summary>Less than zero if <paramref name="a"/> is older, zero if they rank the same, more than zero if it is newer.</summary>
    public static int Compare(string a, string b)
    {
        var (coreA, preA) = Split(a);
        var (coreB, preB) = Split(b);

        var cores = coreA.CompareTo(coreB);
        if (cores != 0)
        {
            return cores;
        }

        // A release is newer than any pre-release of the same number.
        if (preA is null || preB is null)
        {
            return preA is null ? (preB is null ? 0 : 1) : -1;
        }

        var partsA = preA.Split('.');
        var partsB = preB.Split('.');
        for (var i = 0; i < Math.Min(partsA.Length, partsB.Length); i++)
        {
            var numericA = long.TryParse(partsA[i], out var numberA);
            var numericB = long.TryParse(partsB[i], out var numberB);

            // Numbers rank below words; two numbers compare as numbers; two words compare as text.
            var result = numericA && numericB ? numberA.CompareTo(numberB)
                : numericA ? -1
                : numericB ? 1
                : string.CompareOrdinal(partsA[i], partsB[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return partsA.Length.CompareTo(partsB.Length);
    }

    /// <summary>A folder name that is not a version sorts as older than every real one.</summary>
    public static int CompareFolderNames(string a, string b) =>
        (IsValid(a), IsValid(b)) switch
        {
            (true, true) => Compare(a, b),
            (true, false) => 1,
            (false, true) => -1,
            _ => 0,
        };

    private static (Version Core, string? PreRelease) Split(string version)
    {
        var dash = version.IndexOf('-');
        var core = dash < 0 ? version : version[..dash];
        return (Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0), dash < 0 ? null : version[(dash + 1)..]);
    }
}
