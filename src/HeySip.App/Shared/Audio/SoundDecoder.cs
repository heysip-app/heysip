using HeySip.Core.Features.Companion.Packs;
using NVorbis;

namespace HeySip.App.Shared.Audio;

/// <summary>
/// Gets a pack's sound into the one format the player takes: a WAV file. WAV passes straight through; OGG Vorbis is decoded
/// to 16-bit WAV in memory (the sounds are a few seconds long, so this takes a moment and a little memory, once).
/// </summary>
public static class SoundDecoder
{
    // Packs are limited to five seconds. This is a second lock on the door: whatever a file claims, decoding stops here.
    private const double MaxSeconds = 10;

    private const string WavExtension = ".wav";
    private const string OggExtension = ".ogg";

    /// <summary>How many samples are read from the Vorbis stream at a time.</summary>
    private const int ReadChunkSamples = 4096;

    // The 16-bit PCM WAV file that is written. It is a "RIFF" header, a "fmt " chunk and a "data" chunk.
    private const short PcmFormat = 1;
    private const short BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;
    private const int FormatChunkSize = 16;

    /// <summary>The bytes of a WAV file before the sound itself: the RIFF header, the format chunk and the data chunk's header.</summary>
    private const int HeaderBytes = 44;

    /// <summary>What the RIFF header's size field counts: the whole file except the first 8 bytes ("RIFF" and the size itself).</summary>
    private const int RiffHeaderOverhead = HeaderBytes - 8;

    public static byte[]? ToWav(string fileName, byte[] data)
    {
        if (fileName.EndsWith(WavExtension, StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        return fileName.EndsWith(OggExtension, StringComparison.OrdinalIgnoreCase) ? OggToWav(data) : null;
    }

    public static byte[]? OggToWav(byte[] data)
    {
        try
        {
            using var reader = new VorbisReader(new MemoryStream(data), closeOnDispose: true);
            var channels = reader.Channels;
            var rate = reader.SampleRate;
            if (channels is < 1 or > PackLimits.MaxSoundChannels || rate is < PackLimits.MinSoundSampleRate or > PackLimits.MaxSoundSampleRate)
            {
                return null;
            }

            var limit = (int)(rate * MaxSeconds) * channels;
            var samples = new List<short>();
            var buffer = new float[ReadChunkSamples];
            int read;
            while (samples.Count < limit && (read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read && samples.Count < limit; i++)
                {
                    samples.Add((short)Math.Clamp((int)Math.Round(buffer[i] * short.MaxValue), short.MinValue, short.MaxValue));
                }
            }

            return samples.Count == 0 ? null : Wav(samples, channels, rate);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or ArgumentException or IOException or NotSupportedException)
        {
            // Not a Vorbis file after all, or a damaged one: no sound is better than a crash.
            return null;
        }
    }

    private static byte[] Wav(List<short> samples, int channels, int rate)
    {
        var dataBytes = samples.Count * BytesPerSample;
        using var stream = new MemoryStream(HeaderBytes + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(RiffHeaderOverhead + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(FormatChunkSize);
        writer.Write(PcmFormat);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * BytesPerSample);
        writer.Write((short)(channels * BytesPerSample));
        writer.Write(BitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
