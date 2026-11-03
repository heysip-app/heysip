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

    public static byte[]? ToWav(string fileName, byte[] data)
    {
        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        return fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? OggToWav(data) : null;
    }

    public static byte[]? OggToWav(byte[] data)
    {
        try
        {
            using var reader = new VorbisReader(new MemoryStream(data), closeOnDispose: true);
            var channels = reader.Channels;
            var rate = reader.SampleRate;
            if (channels is < 1 or > 2 || rate is < 8000 or > 192_000)
            {
                return null;
            }

            var limit = (int)(rate * MaxSeconds) * channels;
            var samples = new List<short>();
            var buffer = new float[4096];
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
        var dataBytes = samples.Count * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
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
