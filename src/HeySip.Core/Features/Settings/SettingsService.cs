using System.Text.Json;
using System.Text.Json.Serialization;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.Core.Features.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON, and announces changes.</summary>
public sealed class SettingsService(AppDataLocation location, DiagnosticLog? log = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _gate = new();
    private AppSettings _current = new AppSettings().Normalize();

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AppSettings>? Changed;

    /// <summary>Reads the settings file; a missing or unreadable file falls back to defaults.</summary>
    public void Load()
    {
        AppSettings loaded;
        var existed = File.Exists(location.SettingsPath);
        var needsSave = false;
        try
        {
            loaded = existed
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(location.SettingsPath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep the unreadable file so the user's choices aren't lost when the defaults are saved over it.
            log?.Warning("Settings", "settings.json couldn't be read; using defaults and keeping a copy as settings.json.bad.", exception);
            TryBackUpUnreadableFile();
            loaded = new AppSettings();
        }

        if (existed && loaded.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            var fileVersion = loaded.SchemaVersion;
            if (fileVersion > AppSettings.CurrentSchemaVersion)
            {
                // Written by a newer HeySip. Read what we understand, and keep the original in case saving drops something.
                log?.Warning("Settings", $"settings.json is from a newer version (layout {fileVersion}); a copy is kept as settings.json.v{fileVersion}.bak.");
                TryKeepCopy($".v{fileVersion}.bak");
            }
            else
            {
                log?.Info("Settings", $"Upgrading settings.json from layout {fileVersion} to {AppSettings.CurrentSchemaVersion}.");
                TryKeepCopy($".v{fileVersion}.bak");
                loaded = SettingsMigrations.Upgrade(loaded, fileVersion);
                needsSave = true;
            }
        }

        lock (_gate)
        {
            _current = loaded.Normalize();
            if (needsSave)
            {
                TrySave(_current);
            }
        }
    }

    /// <summary>Copies settings.json aside once (never over an earlier copy) before it is rewritten.</summary>
    private void TryKeepCopy(string suffix)
    {
        try
        {
            var target = location.SettingsPath + suffix;
            if (!File.Exists(target))
            {
                File.Copy(location.SettingsPath, target);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryBackUpUnreadableFile()
    {
        try
        {
            File.Copy(location.SettingsPath, location.SettingsPath + ".bad", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The last error from writing settings.json, or null. Shown in diagnostics.</summary>
    public Exception? LastSaveError { get; private set; }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated;
        lock (_gate)
        {
            updated = change(_current).Normalize();
            if (updated.SameAs(_current))
            {
                return _current;
            }

            _current = updated;
            TrySave(updated);
        }

        Changed?.Invoke(this, updated);
        return updated;
    }

    /// <summary>
    /// A setting that can't be written (read-only folder, antivirus lock) still takes effect for this session, and the
    /// problem is logged and kept for diagnostics instead of surfacing as a crash in a property setter.
    /// </summary>
    private void TrySave(AppSettings settings)
    {
        try
        {
            Save(settings);
            LastSaveError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastSaveError = exception;
            log?.Error("Settings", "Couldn't save settings.json; the change applies until HeySip is closed.", exception);
        }
    }

    private void Save(AppSettings settings)
    {
        Directory.CreateDirectory(location.RootDirectory);
        var temp = location.SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, location.SettingsPath, overwrite: true);
    }
}
