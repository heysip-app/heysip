using System.Buffers.Binary;
using System.Text;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>Checks that a pack's sound file really is a short WAV or OGG Vorbis file, without decoding it.</summary>
public static class SoundCheck
{
    /// <summary>Returns null when the file is acceptable, otherwise a short reason.</summary>
    public static string? Validate(string fileName, byte[] data)
    {
        if (data.Length == 0 || data.Length > PackLimits.MaxSoundBytes)
        {
            return $"the sound {fileName} is too big";
        }

        var seconds = fileName.EndsWith(".wav", StringComparison.Ordinal) ? WavSeconds(data)
            : fileName.EndsWith(".ogg", StringComparison.Ordinal) ? OggSeconds(data)
            : null;

        if (seconds is not { } length)
        {
            return $"the sound {fileName} isn't a valid WAV or OGG Vorbis file";
        }

        return length > PackLimits.MaxSoundSeconds ? $"the sound {fileName} is longer than {PackLimits.MaxSoundSeconds:0} seconds" : null;
    }

    private static double? WavSeconds(byte[] data)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "WAVE")
        {
            return null;
        }

        double? bytesPerSecond = null;
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            var body = position + 8;

            if (id == "fmt " && size >= 16 && body + 16 <= data.Length)
            {
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 2));
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(body + 4));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 14));
                if (channels is 0 or > 2 || rate is 0 or > 192_000 || bits is not (8 or 16 or 24 or 32))
                {
                    return null;
                }

                bytesPerSecond = rate * channels * (bits / 8.0);
            }
            else if (id == "data")
            {
                return bytesPerSecond is { } perSecond && perSecond > 0 ? size / perSecond : null;
            }

            // Chunks are padded to an even size.
            var next = (long)body + size + (size % 2);
            if (next > data.Length)
            {
                return null;
            }

            position = (int)next;
        }

        return null;
    }

    private static double? OggSeconds(byte[] data)
    {
        // First page: the Vorbis identification header holds the sample rate.
        if (data.Length < 58 || Encoding.ASCII.GetString(data, 0, 4) != "OggS")
        {
            return null;
        }

        var segments = data[26];
        var packet = 27 + segments;
        if (packet + 16 > data.Length || data[packet] != 1 || Encoding.ASCII.GetString(data, packet + 1, 6) != "vorbis")
        {
            return null;
        }

        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet + 12));
        if (sampleRate is 0 or > 192_000)
        {
            return null;
        }

        // Each page says how many samples there are up to its end. The longest claim wins, so a short page tacked on after the
        // real ones (or a stray "OggS" inside the audio) can't make a long sound look short.
        long longest = 0;
        for (var i = 0; i + 27 <= data.Length; i++)
        {
            if (data[i] == 'O' && data[i + 1] == 'g' && data[i + 2] == 'g' && data[i + 3] == 'S')
            {
                longest = Math.Max(longest, BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 6)));
            }
        }

        return longest > 0 ? longest / (double)sampleRate : null;
    }
}
