using System.Buffers.Binary;
using System.Text;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>Checks that a pack's sound file really is a short WAV or OGG Vorbis file, without decoding it.</summary>
public static class SoundCheck
{
    private const string WavExtension = ".wav";
    private const string OggExtension = ".ogg";

    private const int BitsPerByte = 8;

    // ---- WAV: a "RIFF" header, then chunks of (4-letter id, 4-byte size, body).
    private const string RiffTag = "RIFF";
    private const string WaveTag = "WAVE";
    private const string FormatChunkTag = "fmt ";
    private const string DataChunkTag = "data";
    private const int WaveTagOffset = 8;
    private const int RiffHeaderLength = 12;
    private const int ChunkHeaderLength = 8;
    private const int ChunkSizeOffset = 4;
    private const int FormatChunkMinSize = 16;
    private const int ChannelsOffset = 2;
    private const int SampleRateOffset = 4;
    private const int BitsPerSampleOffset = 14;
    private const int ChunkPadding = 2;

    // ---- OGG: pages start with "OggS"; the first holds the Vorbis identification header.
    private const string OggTag = "OggS";
    private static readonly byte[] OggTagBytes = Encoding.ASCII.GetBytes(OggTag);
    private const string VorbisTag = "vorbis";
    private const byte VorbisIdentificationPacket = 1;
    private const int OggPageHeaderLength = 27;
    private const int OggSegmentCountOffset = 26;
    private const int OggGranuleOffset = 6;
    private const int VorbisHeaderLength = 16;
    private const int VorbisTagOffset = 1;
    private const int VorbisSampleRateOffset = 12;

    /// <summary>The smallest possible Ogg Vorbis file: a page header, one segment and the identification header.</summary>
    private const int MinOggBytes = 58;

    /// <summary>Returns null when the file is acceptable, otherwise a short reason.</summary>
    public static string? Validate(string fileName, byte[] data)
    {
        if (data.Length == 0 || data.Length > PackLimits.MaxSoundBytes)
        {
            return $"the sound {fileName} is too big";
        }

        var seconds = fileName.EndsWith(WavExtension, StringComparison.Ordinal) ? WavSeconds(data)
            : fileName.EndsWith(OggExtension, StringComparison.Ordinal) ? OggSeconds(data)
            : null;

        if (seconds is not { } length)
        {
            return $"the sound {fileName} isn't a valid WAV or OGG Vorbis file";
        }

        return length > PackLimits.MaxSoundSeconds ? $"the sound {fileName} is longer than {PackLimits.MaxSoundSeconds:0} seconds" : null;
    }

    private static bool HasTag(byte[] data, int offset, string tag) =>
        Encoding.ASCII.GetString(data, offset, tag.Length) == tag;

    private static bool IsValidBitDepth(int bits) => bits is 8 or 16 or 24 or 32;

    private static double? WavSeconds(byte[] data)
    {
        if (data.Length < RiffHeaderLength || !HasTag(data, 0, RiffTag) || !HasTag(data, WaveTagOffset, WaveTag))
        {
            return null;
        }

        double? bytesPerSecond = null;
        var position = RiffHeaderLength;
        while (position + ChunkHeaderLength <= data.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + ChunkSizeOffset));
            var body = position + ChunkHeaderLength;

            if (HasTag(data, position, FormatChunkTag) && size >= FormatChunkMinSize && body + FormatChunkMinSize <= data.Length)
            {
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + ChannelsOffset));
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(body + SampleRateOffset));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + BitsPerSampleOffset));
                if (channels is 0 or > PackLimits.MaxSoundChannels || rate is 0 or > PackLimits.MaxSoundSampleRate || !IsValidBitDepth(bits))
                {
                    return null;
                }

                bytesPerSecond = rate * channels * ((double)bits / BitsPerByte);
            }
            else if (HasTag(data, position, DataChunkTag))
            {
                return bytesPerSecond is { } perSecond && perSecond > 0 ? size / perSecond : null;
            }

            // Chunks are padded to an even size.
            var next = (long)body + size + (size % ChunkPadding);
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
        if (data.Length < MinOggBytes || !HasTag(data, 0, OggTag))
        {
            return null;
        }

        var segments = data[OggSegmentCountOffset];
        var packet = OggPageHeaderLength + segments;
        if (packet + VorbisHeaderLength > data.Length
            || data[packet] != VorbisIdentificationPacket
            || !HasTag(data, packet + VorbisTagOffset, VorbisTag))
        {
            return null;
        }

        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(packet + VorbisSampleRateOffset));
        if (sampleRate is 0 or > PackLimits.MaxSoundSampleRate)
        {
            return null;
        }

        // Each page says how many samples there are up to its end. The longest claim wins, so a short page tacked on after the
        // real ones (or a stray "OggS" inside the audio) can't make a long sound look short.
        long longest = 0;
        for (var i = 0; i + OggPageHeaderLength <= data.Length; i++)
        {
            if (data.AsSpan(i, OggTagBytes.Length).SequenceEqual(OggTagBytes))
            {
                longest = Math.Max(longest, BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + OggGranuleOffset)));
            }
        }

        return longest > 0 ? longest / (double)sampleRate : null;
    }
}
