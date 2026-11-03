using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;
using HeySip.Core.SharedKernel.Diagnostics;

namespace HeySip.App.Shared.Audio;

/// <summary>
/// Plays sounds through the computer's default speakers with SoundFlow (which runs on Windows, macOS and Linux). The audio
/// device is only opened when a sound is played and let go a few seconds after the last one has finished, so an app that
/// sits in the tray all day doesn't keep the sound hardware (or the CPU) busy for nothing.
/// </summary>
public sealed class SoundFlowOutput(DiagnosticLog log) : ISoundOutput, IDisposable
{
    private static readonly TimeSpan IdleBeforeRelease = TimeSpan.FromSeconds(8);

    private readonly Lock _gate = new();
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private Timer? _release;
    private int _playing;

    // Counts the times the device has been let go. A sound belongs to the device it started on; when it ends after that
    // device is gone (or replaced by a new one), it must not touch the new device's bookkeeping.
    private int _generation;
    private bool _reportedFailure;
    private bool _disposed;

    /// <summary>True while the audio device is held open: only while a sound plays, and a few seconds after the last one ends.</summary>
    public bool IsDeviceOpen
    {
        get
        {
            lock (_gate)
            {
                return _device is not null;
            }
        }
    }

    public bool Play(byte[] wav, float gain)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                EnsureDevice();
                var provider = new AssetDataProvider(_engine!, wav);
                var player = new SoundPlayer(_engine!, _device!.Format, provider) { Volume = Math.Clamp(gain, 0f, 1f) };
                var generation = _generation;
                _playing++;
                _release?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                // The audio thread must never wait for our lock (Stop() may be waiting for it while we hold the lock), so
                // the tidying-up is handed to another thread.
                player.PlaybackEnded += (_, _) => ThreadPool.QueueUserWorkItem(_ => Finished(player, provider, generation));
                _device.MasterMixer.AddComponent(player);
                player.Play();
                return true;
            }
            catch (Exception exception)
            {
                // Sound is a nicety. No speakers, a missing native library or a busy device must never reach the user as a crash;
                // say so once in the log and carry on silently.
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    log.Warning("Sound", "Sounds can't be played on this computer right now.", exception);
                }

                Release();
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _release?.Dispose();
            _release = null;
            Release();
        }
    }

    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        _engine = new MiniAudioEngine();
        _device = _engine.InitializePlaybackDevice(null, AudioFormat.Cd);
        _device.Start();
    }

    private void Finished(SoundPlayer player, AssetDataProvider provider, int generation)
    {
        lock (_gate)
        {
            // The device this sound played on has been let go already, and with it the sound.
            if (_disposed || generation != _generation)
            {
                return;
            }

            try
            {
                _device?.MasterMixer.RemoveComponent(player);
                provider.Dispose();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
            {
                // The device was released while the sound was ending; there is nothing left to tidy.
            }

            if (--_playing <= 0)
            {
                _playing = 0;
                _release ??= new Timer(_ => ReleaseIfIdle());
                _release.Change(IdleBeforeRelease, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void ReleaseIfIdle()
    {
        lock (_gate)
        {
            if (!_disposed && _playing == 0)
            {
                Release();
            }
        }
    }

    private void Release()
    {
        try
        {
            _device?.Stop();
            _device?.Dispose();
            _engine?.Dispose();
        }
        catch (Exception exception)
        {
            // Letting go of the audio hardware is the very last step and runs from places that must not fail (including the
            // handler for a failed play). The native layer can throw several kinds of exception; whichever it is, the device
            // is dropped below and the app carries on.
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                log.Warning("Sound", "The audio device didn't shut down cleanly.", exception);
            }
        }
        finally
        {
            _device = null;
            _engine = null;
            _playing = 0;
            _generation++;
        }
    }
}
