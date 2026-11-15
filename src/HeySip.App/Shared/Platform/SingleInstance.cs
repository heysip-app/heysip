namespace HeySip.App.Shared.Platform;

/// <summary>
/// Only one HeySip per user session. A second launch tells the running copy to show its window and then exits, so
/// double-clicking the shortcut while HeySip sits in the tray opens it instead of appearing to do nothing.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Developers set this to run a second, separate copy; it names that copy's instance.</summary>
    public const string InstanceNameVariable = "HEYSIP_INSTANCE";

    private const string DefaultName = "HeySip";

    // The names the mutex and the "show your window" signal are registered under; "Local\" keeps them to this user session.
    private const string SessionPrefix = @"Local\";
    private const string MutexSuffix = ".SingleInstance";
    private const string ShowSignalSuffix = ".ShowWindow";

    private const string ListenerThreadName = "HeySip single-instance listener";

    /// <summary>How long disposing waits for the listener thread to notice it should stop.</summary>
    private static readonly TimeSpan ListenerStopTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The index of the show signal in the listener's wait handles (the other one is "stop").</summary>
    private const int ShowSignalIndex = 0;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle showSignal, bool isPrimary)
    {
        _mutex = mutex;
        _showSignal = showSignal;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    /// <param name="name">
    /// Tests pass this so they can't collide with a real running HeySip. Developers can set HEYSIP_INSTANCE to run a second,
    /// separate copy (for example a release build next to a debug build); it does nothing when unset.
    /// </param>
    public static SingleInstance Acquire(string? name = null)
    {
        name ??= Environment.GetEnvironmentVariable(InstanceNameVariable) is { Length: > 0 } custom ? custom : DefaultName;
        var mutex = new Mutex(initiallyOwned: true, $"{SessionPrefix}{name}{MutexSuffix}", out var created);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, $"{SessionPrefix}{name}{ShowSignalSuffix}");
        return new SingleInstance(mutex, signal, created);
    }

    /// <summary>Called by a second launch: asks the running copy to bring its window forward.</summary>
    public void SignalPrimary() => _showSignal.Set();

    /// <summary>Called by the running copy: <paramref name="onSignal"/> runs on a background thread each time another launch asks.</summary>
    public void StartListening(Action onSignal)
    {
        if (!IsPrimary || _listener is not null)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            WaitHandle[] handles = [_showSignal, _stop];
            while (WaitHandle.WaitAny(handles) == ShowSignalIndex)
            {
                onSignal();
            }
        })
        {
            IsBackground = true,
            Name = ListenerThreadName,
        };
        _listener.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return; // "Try again" releases the instance early, and Main disposes it again on the way out
        }

        _disposed = true;
        _stop.Set();
        _listener?.Join(ListenerStopTimeout);
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _showSignal.Dispose();
        _stop.Dispose();
    }
}
