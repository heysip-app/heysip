namespace HeySip.Core.SharedKernel;

/// <summary>Where HeySip keeps its files. <c>HEYSIP_DATA</c> overrides the default (used by tests).</summary>
public sealed record AppDataLocation(string RootDirectory)
{
    public const string OverrideVariable = "HEYSIP_DATA";

    /// <summary>The folder name used before the app was renamed from HydrateBuddy.</summary>
    public const string LegacyFolderName = "HydrateBuddy";

    public string DatabasePath => Path.Combine(RootDirectory, "heysip.db");

    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    /// <summary>Where installed character and accessory packs live: one folder per pack id, then one per version.</summary>
    public string CharactersDirectory => Path.Combine(RootDirectory, "characters");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public static AppDataLocation Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Directory.CreateDirectory(overridden);
            return new AppDataLocation(overridden);
        }

        var parent = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ResolveUnder(parent);
    }

    /// <summary>
    /// The data folder under <paramref name="parent"/>. A folder left by the old HydrateBuddy name is moved across once,
    /// so existing settings and drink history carry over.
    /// </summary>
    public static AppDataLocation ResolveUnder(string parent)
    {
        var root = Path.Combine(parent, "HeySip");
        var legacy = Path.Combine(parent, LegacyFolderName);
        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, root);
                var oldDatabase = Path.Combine(root, "hydratebuddy.db");
                var newDatabase = Path.Combine(root, "heysip.db");
                if (File.Exists(oldDatabase) && !File.Exists(newDatabase))
                {
                    File.Move(oldDatabase, newDatabase);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Couldn't move (in use or no permission): start fresh rather than failing to launch. The old folder is untouched.
            }
        }

        Directory.CreateDirectory(root);
        return new AppDataLocation(root);
    }
}
