using HeySip.Core.SharedKernel;

namespace HeySip.App.Shared.Platform;

/// <summary>
/// Hands a pack file from a second launch to the copy that is already running. Opening a .heysippack while HeySip sits
/// in the tray starts a second process, which writes the path here, wakes the first one, and exits.
/// </summary>
public static class PendingPack
{
    public const string Extension = ".heysippack";

    private static string FileFor(AppDataLocation location) => Path.Combine(location.RootDirectory, "open-pack.txt");

    /// <summary>The pack path in the command line (HeySip.exe "path\to\character.heysippack"), if there is one.</summary>
    public static string? FromArguments(IEnumerable<string> args) =>
        args.FirstOrDefault(arg => arg.EndsWith(Extension, StringComparison.OrdinalIgnoreCase));

    public static void Write(AppDataLocation location, string path)
    {
        try
        {
            File.WriteAllText(FileFor(location), Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The first copy just shows its window; the user can open the file from Settings.
        }
    }

    /// <summary>Takes the waiting path (and clears it), or null if there is none.</summary>
    public static string? TryTake(AppDataLocation location)
    {
        var file = FileFor(location);
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var path = File.ReadAllText(file).Trim();
            File.Delete(file);
            return path.Length > 0 ? path : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
