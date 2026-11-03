namespace HeySip.App.Shared.Audio;

/// <summary>Plays a short sound. The only thing the rest of the app knows about audio, so tests can listen in without a speaker.</summary>
public interface ISoundOutput
{
    /// <summary>Starts playing a WAV file's bytes at the given gain (0 to 1) and returns at once. False if it couldn't be played.</summary>
    bool Play(byte[] wav, float gain);
}
