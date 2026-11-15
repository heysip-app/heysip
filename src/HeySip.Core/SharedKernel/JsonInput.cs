using System.Text.Json;

namespace HeySip.Core.SharedKernel;

/// <summary>What every JSON file the app reads from outside (packs, the store list, the control file) has in common.</summary>
public static class JsonInput
{
    /// <summary>How deeply objects and lists may nest. These files are flat, so anything deeper is refused.</summary>
    public const int MaxDepth = 8;

    /// <summary>The name of the number at the top of each file that says which layout it is written in.</summary>
    public const string FormatVersionKey = "formatVersion";

    /// <summary>The layout of the store list and the control file; a file with any other number is not understood.</summary>
    public const int SupportedFormatVersion = 1;

    /// <summary>Editors on Windows (Notepad, PowerShell) save UTF-8 with these three bytes in front. They are not part of the JSON.</summary>
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>The same bytes without a leading byte order mark, if there was one.</summary>
    public static ReadOnlyMemory<byte> WithoutByteOrderMark(ReadOnlyMemory<byte> json) =>
        json.Span.StartsWith(ByteOrderMark) ? json[ByteOrderMark.Length..] : json;

    /// <summary>Strict reading: no comments, no trailing commas, limited depth.</summary>
    public static JsonDocumentOptions Strict(int maxDepth = MaxDepth) => new()
    {
        MaxDepth = maxDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>True when the object has a whole-number <c>formatVersion</c> equal to <paramref name="supported"/>.</summary>
    public static bool HasFormatVersion(JsonElement root, int supported) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(FormatVersionKey, out var format)
        && format.ValueKind == JsonValueKind.Number
        && format.TryGetInt32(out var version)
        && version == supported;
}
