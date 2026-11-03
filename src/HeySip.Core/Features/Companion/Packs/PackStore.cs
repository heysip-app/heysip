using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>An installed pack that passed every check, with the folder it was read from.</summary>
public sealed record InstalledPack(Pack Pack, string Folder);

/// <summary>
/// The packs installed under <c>characters\&lt;id&gt;\&lt;version&gt;\</c>. A pack that fails its checks is left out and logged;
/// it never stops the others from loading, or the app from starting.
/// </summary>
public static class PackStore
{
    /// <summary>Loads the newest valid version of every installed pack.</summary>
    public static IReadOnlyList<InstalledPack> Scan(string directory, PackLoadOptions options, DiagnosticLog? log = null)
    {
        var found = new List<InstalledPack>();
        if (!Directory.Exists(directory))
        {
            return found;
        }

        foreach (var idFolder in SafeFolders(directory))
        {
            var id = Path.GetFileName(idFolder);
            if (id.StartsWith('.'))
            {
                continue; // an install in progress (see PackInstaller), not a pack
            }

            // Newest version first, so an older one is only used when the newer one is broken.
            var versions = SafeFolders(idFolder)
                .Where(folder => !Path.GetFileName(folder).StartsWith('.')) // a backup or leftover, not a version
                .OrderByDescending(folder => Path.GetFileName(folder), Comparer<string>.Create(PackVersion.CompareFolderNames))
                .ToList();

            foreach (var versionFolder in versions)
            {
                var result = PackLoader.FromFolder(versionFolder, options);
                if (result.Pack is { } pack && pack.Header.Id == id && pack.Header.Version == Path.GetFileName(versionFolder))
                {
                    found.Add(new InstalledPack(pack, versionFolder));
                    break;
                }

                var reason = result.Error ?? "the folder name doesn't match the pack's id and version";
                log?.Warning("Packs", $"Skipped {id}/{Path.GetFileName(versionFolder)}: {reason}");
            }
        }

        return found;
    }

    private static IEnumerable<string> SafeFolders(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).Where(folder => (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
