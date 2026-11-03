namespace HeySip.Core.Features.Companion.Packs;

/// <summary>What installing a pack would do, given what is already installed.</summary>
public enum InstallKind
{
    /// <summary>Nothing of this pack is installed yet.</summary>
    New,

    /// <summary>An older version is installed; this one replaces it as the version in use.</summary>
    Update,

    /// <summary>This exact version is installed already; installing again just rewrites it.</summary>
    Reinstall,

    /// <summary>A newer version is installed, so this older one would never be used.</summary>
    Older,
}

public sealed record InstallPlan(InstallKind Kind, string? InstalledVersion);

public sealed record InstallOutcome(bool Ok, string? Error, string? Folder)
{
    public static InstallOutcome Failure(string error) => new(false, error, null);
}

/// <summary>
/// Puts a checked pack into the characters folder as <c>&lt;id&gt;\&lt;version&gt;\</c>, and takes one out again. A pack is
/// written to a temporary folder first and swapped into place whole, so a failure never leaves a half-installed pack, and a
/// working copy is never lost to a failed replace.
/// </summary>
public static class PackInstaller
{
    /// <summary>What installing <paramref name="pack"/> would do. Looks at the folder names only; nothing is read or changed.</summary>
    public static InstallPlan Plan(Pack pack, string directory)
    {
        var idFolder = Path.Combine(directory, pack.Header.Id);
        if (!IsSafe(pack.Header) || !Directory.Exists(idFolder))
        {
            return new InstallPlan(InstallKind.New, null);
        }

        var installed = Directory.EnumerateDirectories(idFolder)
            .Select(folder => Path.GetFileName(folder))
            .Where(PackVersion.IsValid)
            .OrderByDescending(name => name, Comparer<string>.Create(PackVersion.Compare))
            .ToList();
        if (installed.Count == 0)
        {
            return new InstallPlan(InstallKind.New, null);
        }

        var newest = installed[0];
        var comparison = PackVersion.Compare(pack.Header.Version, newest);
        return new InstallPlan(comparison > 0 ? InstallKind.Update : comparison == 0 ? InstallKind.Reinstall : InstallKind.Older, newest);
    }

    public static InstallOutcome Install(PackLoadResult loaded, string directory)
    {
        if (loaded.Pack is not { } pack)
        {
            return InstallOutcome.Failure(loaded.Error ?? "There is no pack to install");
        }

        if (!IsSafe(pack.Header) || pack.Header.Id.StartsWith(PackId.BuiltInPrefix, StringComparison.Ordinal))
        {
            return InstallOutcome.Failure("This pack's id or version can't be installed");
        }

        var files = loaded.ValidatedFiles;
        if (!files.ContainsKey("pack.json"))
        {
            return InstallOutcome.Failure("The pack has no pack.json");
        }

        var target = Path.Combine(directory, pack.Header.Id, pack.Header.Version);
        var idFolder = Path.GetDirectoryName(target)!;
        var staging = Path.Combine(directory, ".incoming-" + Guid.NewGuid().ToString("N"));

        // A leading "." keeps these out of the scan, which skips anything that is not a plain version folder.
        var backup = Path.Combine(idFolder, "." + pack.Header.Version + ".replaced-" + Guid.NewGuid().ToString("N")[..8]);
        var movedAside = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var (name, data) in files)
            {
                // Names were checked when the pack was loaded (pack.json, pack.sig, sounds/...); this is a second lock on the door.
                var destination = Path.GetFullPath(Path.Combine(staging, name));
                if (!destination.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    return InstallOutcome.Failure("The pack contains a file name that isn't allowed");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, data);
            }

            Directory.CreateDirectory(idFolder);

            // The copy that works now is only moved out of the way, never deleted, until the new one is in place.
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedAside = true;
            }

            Directory.Move(staging, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (movedAside && !Directory.Exists(target))
            {
                TryMove(backup, target); // put the working copy back
            }

            return InstallOutcome.Failure("The pack couldn't be saved. Check that HeySip may write to its data folder.");
        }
        finally
        {
            TryDelete(staging);
        }

        // The new copy is in place, so the old one and any older versions are no use any more.
        if (movedAside)
        {
            TryDelete(backup);
        }

        PruneOlderVersions(idFolder, pack.Header.Version);
        return new InstallOutcome(true, null, target);
    }

    /// <summary>Removes an installed pack, every version of it. The built-in characters can't be removed.</summary>
    public static bool Uninstall(string id, string directory)
    {
        if (!PackId.IsValid(id) || id.StartsWith(PackId.BuiltInPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var folder = Path.Combine(directory, id);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ids and versions become folder names, so only the shapes the format allows may ever reach the disk.</summary>
    private static bool IsSafe(PackHeader header) => PackId.IsValid(header.Id) && PackVersion.IsValid(header.Version);

    /// <summary>Once a version is installed, the older ones are no use any more and are tidied away (best effort).</summary>
    private static void PruneOlderVersions(string idFolder, string installedVersion)
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(idFolder))
            {
                var name = Path.GetFileName(folder);
                if (PackVersion.IsValid(name) && PackVersion.Compare(name, installedVersion) < 0)
                {
                    TryDelete(folder);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover old versions only cost disk space; the newest one is always the one used.
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the backup stays beside it, and starts with the id's own name so it isn't mistaken for a pack.
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover staging folders start with "." and are ignored by the scan, so this is only tidiness.
        }
    }
}
