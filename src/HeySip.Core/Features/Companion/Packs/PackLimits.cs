using HeySip.Core.SharedKernel;

namespace HeySip.Core.Features.Companion.Packs;

/// <summary>The size and shape limits that keep a pack small, safe and cheap to draw.</summary>
public static class PackLimits
{
    public const int FormatVersion = 1;

    /// <summary>The oldest format version there is; anything below is invalid.</summary>
    public const int FirstFormatVersion = 1;

    private const int Kilobyte = ByteSize.Kilobyte;

    public const int MaxPackBytes = Kilobyte * Kilobyte;
    public const int MaxUnpackedBytes = 2 * Kilobyte * Kilobyte;
    public const int MaxFiles = 8;
    public const int MaxJsonBytes = 256 * Kilobyte;
    public const int MaxJsonDepth = 8;
    public const int MaxSoundBytes = 200 * Kilobyte;
    public const double MaxSoundSeconds = 5;

    /// <summary>Mono or stereo only.</summary>
    public const int MaxSoundChannels = 2;

    /// <summary>No real sound has more samples per second than this; a bigger number means a damaged or hostile file.</summary>
    public const int MaxSoundSampleRate = 192_000;

    /// <summary>The slowest sample rate the player accepts when it decodes a sound (telephone quality).</summary>
    public const int MinSoundSampleRate = 8000;

    public const int MaxShapesPerList = 64;
    public const int MaxShapesTotal = 512;
    public const int MaxPathChars = 4096;
    public const int MaxPathSegments = 256;
    public const double MaxPathNumber = 1000;

    public const int MaxPaletteColors = 16;
    public const int MaxNameLength = 32;

    /// <summary>
    /// The longest palette colour name. The pattern in the parser says the same thing ("a letter, then up to 31 more"), and a
    /// test keeps the two equal.
    /// </summary>
    public const int MaxPaletteNameLength = 32;

    // How long each piece of text in pack.json may be.
    public const int MaxKindLength = 16;
    public const int MaxIdLength = 64;
    public const int MinIdLength = 3;
    public const int MaxVersionLength = 40;
    public const int MaxMinAppVersionLength = 20;
    public const int MaxTierLength = 10;
    public const int MaxStoreIdLength = 64;
    public const int MaxAuthorLength = 64;
    public const int MaxLicenseLength = 64;
    public const int MaxDescriptionLength = 256;
    public const int MaxSoundNameLength = 64;

    /// <summary>The longest of the fixed words (layer, arm, cap, join) a pack may write.</summary>
    public const int MaxKeywordLength = 8;

    public const int MaxEffectsPerPose = 8;

    /// <summary>How much of a value is quoted back in an error message before it is cut short.</summary>
    public const int MaxQuotedValueLength = 24;

    public const int MaxQuotedFileNameLength = 40;

    public const int MaxSignatureBytes = 4 * Kilobyte;

    /// <summary>How much is read from a pack file at a time.</summary>
    public const int ReadChunkBytes = 16 * Kilobyte;

    /// <summary>The size of the picture a character is drawn in.</summary>
    public const double DesignWidth = 100;

    public const double DesignHeight = 140;

    // Things may stick out of the design box a little (hats, clouds).
    public const double MinX = -50;
    public const double MaxX = 150;
    public const double MinY = -50;
    public const double MaxY = 190;

    // Face shapes and accessories are drawn relative to an anchor.
    public const double MaxRelative = 150;

    public const double MaxStrokeWidth = 30;
    public const double MaxRadius = 150;

    // The ranges a motion value is brought into, so a pack can never make the character flash or spin.
    public static readonly PackRange Bob = new(0, 4);
    public static readonly PackRange BobSpeed = new(0.5, 4);
    public static readonly PackRange Squash = new(0, 0.05);
    public static readonly PackRange BlinkEveryMs = new(2000, 10000);
    public static readonly PackRange BlinkMs = new(80, 300);

    // Arm sway.
    public static readonly PackRange SwayAmplitude = new(0, 15);
    public static readonly PackRange SwaySpeed = new(0, 12);
    public const double DefaultSwayAmplitude = 5;
    public const double DefaultSwaySpeed = 6;

    // Arms.
    public static readonly PackRange Stroke = new(0, MaxStrokeWidth);
    public const double DefaultArmWidth = 5;
    public const double DefaultArmOutlineWidth = 8;

    public static readonly PackRange Opacity = new(0, 1);
    public const double FullOpacity = 1;
}
