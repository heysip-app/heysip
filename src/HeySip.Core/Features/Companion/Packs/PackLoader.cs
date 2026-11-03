using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>
/// Reads a pack from a <c>.heysippack</c> zip, a folder, or a set of files, and checks every rule in the format before
/// anything is shown. A bad pack gives a short reason and never throws.
/// </summary>
public static partial class PackLoader
{
    private const string ManifestName = "pack.json";
    private const string SignatureName = "pack.sig";
    private const int MaxSignatureBytes = 4096;

    [GeneratedRegex(@"^sounds/[a-z0-9][a-z0-9._-]{0,47}\.(wav|ogg)$")]
    private static partial Regex SoundNamePattern();

    internal static bool IsSoundFileName(string name) => SoundNamePattern().IsMatch(name);

    public static PackLoadResult FromZip(Stream stream, PackLoadOptions options) => Guard(() =>
    {
        using var buffer = new MemoryStream();
        CopyCapped(stream, buffer, PackLimits.MaxPackBytes, "The pack file is too big");
        buffer.Position = 0;

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;

        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name == "sounds/")
            {
                continue; // the folder itself
            }

            CheckName(name);

            // A symbolic link in a zip made on Linux or macOS: never follow one.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidPackException($"The pack contains a link ({name}), which isn't allowed");
            }

            if (files.Count >= PackLimits.MaxFiles)
            {
                throw new InvalidPackException("The pack has too many files");
            }

            if (files.ContainsKey(name))
            {
                throw new InvalidPackException($"The pack lists {name} twice");
            }

            var cap = CapFor(name);
            if (entry.Length > cap)
            {
                throw new InvalidPackException($"{name} is too big");
            }

            using var content = entry.Open();
            using var copy = new MemoryStream();
            CopyCapped(content, copy, cap, $"{name} is too big"); // a zip can lie about its sizes, so the real size is checked too

            total += copy.Length;
            if (total > PackLimits.MaxUnpackedBytes)
            {
                throw new InvalidPackException("The pack is too big once unpacked");
            }

            files[name] = copy.ToArray();
        }

        return Build(files, options);
    });

    public static PackLoadResult FromFolder(string folder, PackLoadOptions options) => Guard(() =>
    {
        if (!Directory.Exists(folder))
        {
            throw new InvalidPackException("The pack folder wasn't found");
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;

        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            var leaf = Path.GetFileName(path);
            if (IsLink(path))
            {
                throw new InvalidPackException($"The pack contains a link ({leaf}), which isn't allowed");
            }

            if (Directory.Exists(path))
            {
                if (leaf != "sounds")
                {
                    throw new InvalidPackException($"The pack contains an unexpected folder ({leaf})");
                }

                foreach (var sound in Directory.EnumerateFileSystemEntries(path))
                {
                    AddFile(files, $"sounds/{Path.GetFileName(sound)}", sound, ref total);
                }
            }
            else
            {
                AddFile(files, leaf, path, ref total);
            }
        }

        return Build(files, options);
    });

    /// <summary>For callers that already hold the files (and for tests). Names are the paths inside the pack.</summary>
    public static PackLoadResult FromFiles(IReadOnlyDictionary<string, byte[]> files, PackLoadOptions options) =>
        Guard(() => Build(files, options));

    private static void AddFile(Dictionary<string, byte[]> files, string name, string path, ref long total)
    {
        CheckName(name);
        if (IsLink(path))
        {
            throw new InvalidPackException($"The pack contains a link ({name}), which isn't allowed");
        }

        if (files.Count >= PackLimits.MaxFiles)
        {
            throw new InvalidPackException("The pack has too many files");
        }

        var cap = CapFor(name);
        var info = new FileInfo(path);
        if (info.Length > cap)
        {
            throw new InvalidPackException($"{name} is too big");
        }

        total += info.Length;
        if (total > PackLimits.MaxUnpackedBytes)
        {
            throw new InvalidPackException("The pack is too big once unpacked");
        }

        files[name] = File.ReadAllBytes(path);
    }

    private static bool IsLink(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || new FileInfo(path).LinkTarget is not null;

    private static int CapFor(string name) => name switch
    {
        ManifestName => PackLimits.MaxJsonBytes,
        SignatureName => MaxSignatureBytes,
        _ => PackLimits.MaxSoundBytes,
    };

    /// <summary>Only the files the format names are allowed: no paths that climb out, no drives, no other folders.</summary>
    private static void CheckName(string name)
    {
        if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith('/') || name.Contains('\\') || name.Contains(':'))
        {
            throw new InvalidPackException($"The pack has a file name that isn't allowed ({Shorten(name)})");
        }

        if (name is not (ManifestName or SignatureName) && !IsSoundFileName(name))
        {
            throw new InvalidPackException($"The pack contains a file it shouldn't ({Shorten(name)})");
        }
    }

    private static void CopyCapped(Stream from, Stream to, int cap, string message)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > cap)
            {
                throw new InvalidPackException(message);
            }

            to.Write(buffer, 0, read);
        }
    }

    private static PackLoadResult Build(IReadOnlyDictionary<string, byte[]> files, PackLoadOptions options)
    {
        foreach (var name in files.Keys)
        {
            CheckName(name);
        }

        if (!files.TryGetValue(ManifestName, out var manifest))
        {
            throw new InvalidPackException("The pack has no pack.json");
        }

        if (manifest.Length > PackLimits.MaxJsonBytes)
        {
            throw new InvalidPackException("pack.json is too big");
        }

        var pack = PackParser.Parse(manifest, options);

        // Every sound file must be one the pack uses, exist, and really be a short WAV or OGG.
        var used = pack is CharacterPack character
            ? new[] { character.Sounds.Reminder, character.Sounds.Drank }.Where(name => name is not null).Select(name => name!).Distinct().ToList()
            : [];

        var sounds = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in used)
        {
            if (!files.TryGetValue(name, out var data))
            {
                throw new InvalidPackException($"The pack's sound {name} is missing");
            }

            if (SoundCheck.Validate(name, data) is { } problem)
            {
                throw new InvalidPackException($"The pack's {problem}");
            }

            sounds[name] = data;
        }

        var unused = files.Keys.FirstOrDefault(name => IsSoundFileName(name) && !sounds.ContainsKey(name));
        if (unused is not null)
        {
            throw new InvalidPackException($"The pack contains a sound it doesn't use ({unused})");
        }

        var validated = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [ManifestName] = manifest };
        if (files.TryGetValue(SignatureName, out var signature))
        {
            validated[SignatureName] = signature;
        }

        foreach (var (name, data) in sounds)
        {
            validated[name] = data;
        }

        return new PackLoadResult(pack, sounds, null, validated);
    }

    /// <summary>Anything wrong with a pack becomes a short message; a pack must never be able to crash the app.</summary>
    private static PackLoadResult Guard(Func<PackLoadResult> load)
    {
        try
        {
            return load();
        }
        catch (InvalidPackException problem)
        {
            return PackLoadResult.Failure(problem.Message);
        }
        catch (Exception problem) when (problem is JsonException or InvalidOperationException)
        {
            // InvalidOperationException is what the JSON reader throws when a value is a different type than the code asked for;
            // for a pack that is "not valid", never a crash.
            return PackLoadResult.Failure("pack.json isn't valid");
        }
        catch (InvalidDataException)
        {
            return PackLoadResult.Failure("This isn't a valid pack file");
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return PackLoadResult.Failure("The pack couldn't be read");
        }
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
}
