namespace HeySip.Tests.Support;

/// <summary>Packs PNG images into a Windows .ico file (PNG-compressed entries, supported since Windows Vista).</summary>
public static class IconWriter
{
    public static byte[] Pack(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)images.Count);

        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // width (0 means 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height
            writer.Write((byte)0); // palette colors
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }

        return stream.ToArray();
    }
}
