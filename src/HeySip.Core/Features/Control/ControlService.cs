using System.Text.Json;
using HeySip.Core.Features.Companion.Store;
using HeySip.Core.SharedKernel;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.Core.Features.Control;

/// <summary>
/// Keeps the app in touch with the signed control file: the newest version, notices, and where the online list is.
/// Everything about it is quiet and optional. A failed check only goes to the log, never to the user; after failures it
/// backs off (1 hour, 6 hours, a day, three days, a week) so a missing server costs next to nothing; the last good copy
/// is kept on disk and used when nothing can be reached; and nothing here can stop a reminder.
/// </summary>
public sealed class ControlService : IDisposable
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromHours(72), TimeSpan.FromHours(168)];

    private const int MaxSignatureBytes = 1024;
    private const int MaxDismissed = 200;

    private readonly ControlOptions _options;
    private readonly string _cacheFile;
    private readonly string _signatureFile;
    private readonly string _stateFile;
    private readonly Func<bool> _allowed;
    private readonly Version _appVersion;
    private readonly TimeProvider _time;
    private readonly DiagnosticLog? _log;
    private readonly BoundedHttp _http;
    private readonly Lock _gate = new();
    private ControlFile? _current;
    private State _state = new();

    /// <param name="allowed">Asked before every check: false (the user turned online lookups off) means nothing is contacted.</param>
    public ControlService(
        ControlOptions options,
        AppDataLocation location,
        Func<bool> allowed,
        Version appVersion,
        TimeProvider time,
        string userAgent,
        DiagnosticLog? log = null,
        HttpMessageHandler? handler = null)
    {
        _options = options;
        _cacheFile = Path.Combine(location.RootDirectory, "control.json");
        _signatureFile = Path.Combine(location.RootDirectory, "control.sig");
        _stateFile = Path.Combine(location.RootDirectory, "control-state.json");
        _allowed = allowed;
        _appVersion = appVersion;
        _time = time;
        _log = log;
        _http = new BoundedHttp(userAgent, RequestTimeout, handler);
    }

    /// <summary>Raised, on whatever thread, when the file or the dismissed notices change.</summary>
    public event Action? Changed;

    /// <summary>The last good control file (from the internet or the copy kept on disk), or null if there has never been one.</summary>
    public ControlFile? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>When the next check is due, or null if one is due now.</summary>
    public DateTimeOffset? NextCheckAt
    {
        get
        {
            lock (_gate)
            {
                return _state.NextCheckAt;
            }
        }
    }

    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate)
            {
                return _state.Failures;
            }
        }
    }

    /// <summary>This version is older than the oldest one the file still supports.</summary>
    public bool BelowMinimum => Current?.MinSupportedVersion is { } minimum && _appVersion < minimum;

    /// <summary>A newer version exists and there is somewhere to get it.</summary>
    public bool UpdateAvailable => Current is { LatestVersion: { } latest, UpdateUrl: not null } && latest > _appVersion;

    /// <summary>The control file hasn't switched the online list off (true when there is no file yet).</summary>
    public bool OnlineStoreEnabled => Current?.OnlineStore ?? true;

    /// <summary>Where the online list is for this version of the app, in the order to try them. Empty when the file gives none.</summary>
    public IReadOnlyList<Uri> CatalogUrls() =>
        Current?.Catalog.FirstOrDefault(rule => rule.Covers(_appVersion))?.Urls ?? [];

    /// <summary>Whether a file (such as the online list) was signed by one of the keys this version trusts. <paramref name="signatureFile"/> is the text of the <c>.sig</c> file.</summary>
    public bool IsSignedByTrustedKey(byte[] data, byte[] signatureFile) =>
        ControlVerifier.ReadSignature(signatureFile) is { } signature && ControlVerifier.Verify(data, signature, _options.PublicKeys);

    public bool IsRevoked(string packId, string? sha256) =>
        Current is { } file && file.Revoked.Any(value => string.Equals(value, packId, StringComparison.Ordinal) || (sha256 is not null && string.Equals(value, sha256, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Reads the copy kept on disk and the check schedule. Safe to call when there is nothing yet; never throws.</summary>
    public void Load()
    {
        ControlFile? cached = null;
        try
        {
            if (File.Exists(_cacheFile) && File.Exists(_signatureFile))
            {
                // The kept copy is checked again like a fresh one, so a file changed on disk is not trusted.
                cached = Check(File.ReadAllBytes(_cacheFile), File.ReadAllBytes(_signatureFile));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log?.Warning("Control", "The kept control file couldn't be read.", exception);
        }

        var state = new State();
        try
        {
            if (File.Exists(_stateFile) && JsonSerializer.Deserialize<State>(File.ReadAllText(_stateFile)) is { } read)
            {
                state = read;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _log?.Warning("Control", "The control check schedule couldn't be read; starting it over.", exception);
        }

        lock (_gate)
        {
            _current = cached;
            _state = state;
        }

        Changed?.Invoke();
    }

    /// <summary>Whether it is time to look again: online lookups are on and the schedule says so.</summary>
    public bool IsDue
    {
        get
        {
            if (!_allowed())
            {
                return false;
            }

            lock (_gate)
            {
                return _state.NextCheckAt is not { } next || _time.GetUtcNow() >= next;
            }
        }
    }

    /// <summary>One attempt, whatever the schedule says (unless online lookups are off). True when a good, signed file was read.</summary>
    public async Task<bool> CheckAsync(CancellationToken cancel = default)
    {
        if (!_allowed())
        {
            return false;
        }

        foreach (var url in _options.Urls)
        {
            try
            {
                var data = await _http.GetAsync(url, ControlParser.MaxBytes, cancel);
                var signature = await _http.GetAsync(new Uri(url, "control.sig"), MaxSignatureBytes, cancel);
                if (Check(data, signature) is not { } file)
                {
                    throw new IOException("The control file isn't signed by a key this version trusts, or isn't readable");
                }

                Accept(file, data, signature);
                return true;
            }
            catch (IOException exception)
            {
                _log?.Warning("Control", $"Couldn't read the control file from {url.Host}: {exception.Message}");
            }
        }

        RecordFailure();
        return false;
    }

    /// <summary>The background loop: a short wait after start, then a check whenever one is due, looking at the clock each hour.</summary>
    public async Task RunAsync(CancellationToken cancel, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= (span, token) => Task.Delay(span, _time, token);
        try
        {
            await delay(TimeSpan.FromSeconds(20), cancel);
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    if (IsDue)
                    {
                        await CheckAsync(cancel);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _log?.Error("Control", "A control check failed unexpectedly; it will be tried again later.", exception);
                }

                await delay(TimeSpan.FromHours(1), cancel);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// What to tell the user now, most important first: that this version is no longer supported, that a newer one exists,
    /// then the notices from the file that apply to this version, haven't expired and haven't been dismissed.
    /// </summary>
    public IReadOnlyList<ControlNotice> ActiveNotices()
    {
        lock (_gate)
        {
            return [.. Applicable(_current).Where(notice => !notice.Dismissible || !_state.Dismissed.Contains(notice.Id))];
        }
    }

    /// <summary>Hides a notice for good (on this computer). A notice that can't be dismissed is left alone.</summary>
    public void Dismiss(string id)
    {
        lock (_gate)
        {
            if (_state.Dismissed.Contains(id) || Applicable(_current).All(notice => notice.Id != id || !notice.Dismissible))
            {
                return;
            }

            _state.Dismissed.Add(id);
            if (_state.Dismissed.Count > MaxDismissed)
            {
                _state.Dismissed.RemoveAt(0);
            }

            SaveState();
        }

        Changed?.Invoke();
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Every notice that applies to this version right now, dismissed or not.</summary>
    private List<ControlNotice> Applicable(ControlFile? file)
    {
        if (file is null)
        {
            return [];
        }

        var now = _time.GetUtcNow();
        var notices = new List<ControlNotice>();
        if (file.MinSupportedVersion is { } minimum && _appVersion < minimum)
        {
            notices.Add(new ControlNotice("unsupported", $"This version of HeySip is no longer supported. Please update to {file.LatestVersion?.ToString(3) ?? "the latest version"}.", file.UpdateUrl, null, null, null, Dismissible: false));
        }

        if (file is { LatestVersion: { } latest, UpdateUrl: not null } && latest > _appVersion)
        {
            notices.Add(new ControlNotice($"update-{latest.ToString(3)}", $"HeySip {latest.ToString(3)} is available.", file.UpdateUrl, null, null, null, Dismissible: true));
        }

        notices.AddRange(file.Notices.Where(notice =>
            (notice.MinVersion is null || _appVersion >= notice.MinVersion)
            && (notice.MaxVersion is null || _appVersion <= notice.MaxVersion)
            && (notice.Expires is null || now < notice.Expires)));
        return notices;
    }

    private ControlFile? Check(byte[] data, byte[] signatureFile) =>
        ControlVerifier.ReadSignature(signatureFile) is { } signature
        && ControlVerifier.Verify(data, signature, _options.PublicKeys)
            ? ControlParser.Parse(data, _options.LinkAllowed)
            : null;

    private void Accept(ControlFile file, byte[] data, byte[] signature)
    {
        var apply = true;
        lock (_gate)
        {
            // A genuine file older than the one already kept is a stale copy (a cache somewhere, or a replay): keep the newer one.
            if (_current is { } kept && file.IssuedAt < kept.IssuedAt)
            {
                apply = false;
            }
            else
            {
                _current = file;
            }

            _state.Failures = 0;
            _state.NextCheckAt = _time.GetUtcNow() + CheckEvery;
            SaveState();
        }

        if (!apply)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(_cacheFile, data);
            File.WriteAllBytes(_signatureFile, signature);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log?.Warning("Control", "The control file couldn't be kept on disk.", exception);
        }

        Changed?.Invoke();
    }

    private void RecordFailure()
    {
        lock (_gate)
        {
            _state.Failures++;
            _state.NextCheckAt = _time.GetUtcNow() + Backoff[Math.Min(_state.Failures, Backoff.Length) - 1];
            SaveState();
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_state));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log?.Warning("Control", "The control check schedule couldn't be saved.", exception);
        }
    }

    /// <summary>What is remembered between runs: when to look next, how many times in a row it failed, and which notices were dismissed.</summary>
    private sealed class State
    {
        public DateTimeOffset? NextCheckAt { get; set; }

        public int Failures { get; set; }

        public List<string> Dismissed { get; set; } = [];
    }
}
