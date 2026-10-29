using System.Buffers.Binary;
using System.IO.Compression;

namespace Dungeons.Game.Tests;

// Shared engine-free fixtures are checked by both xUnit and the native PNG decoder.
internal static class PreviewPngFixtures
{
    internal static byte[] Build(int colour, int depth, int interlace, bool metadata = false, int width = 320, int filter = 0, bool extra = false, bool invalidIndex = false)
    {
        using var output = new MemoryStream(); output.Write(new byte[] {137,80,78,71,13,10,26,10}); byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 180); header[8] = (byte)depth; header[9] = (byte)colour; header[12] = (byte)interlace; Write(output, "IHDR", header);
        if (colour == 3) { Write(output, "PLTE", [20, 40, 60]); }
        if (metadata) { Write(output, "tEXt", System.Text.Encoding.ASCII.GetBytes("Note\0private metadata")); }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            int channels = colour is 2 ? 3 : colour is 4 ? 2 : colour is 6 ? 4 : 1;
            int[] widths = interlace == 0 ? [320] : [40,40,80,80,160,160,320], heights = interlace == 0 ? [180] : [23,23,22,45,45,90,90];
            for (int pass = 0; pass < widths.Length; pass++)
            {
                byte[] row = new byte[(widths[pass] * depth * channels + 7) / 8]; if (invalidIndex) { row[0] = 1; }
                for (int y = 0; y < heights[pass]; y++) { zlib.WriteByte((byte)filter); zlib.Write(row); }
            }
            if (extra) { zlib.Write(new byte[1000000]); }
        }
        Write(output, "IDAT", compressed.ToArray()); Write(output, "IEND", []); return output.ToArray();
    }
    internal static int Find(byte[] bytes, string type) => System.Text.Encoding.ASCII.GetString(bytes).IndexOf(type, StringComparison.Ordinal) - 4;
    internal static void FixCrc(byte[] bytes, int start)
    { int length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start)); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(start + 8 + length), Crc(bytes.AsSpan(start + 4, length + 4))); }
    private static void Write(Stream output, string type, byte[] data)
    {
        Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length); output.Write(value);
        byte[] payload = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); output.Write(payload); BinaryPrimitives.WriteUInt32BigEndian(value, Crc(payload)); output.Write(value);
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint[] table = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) { c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1; } table[n] = c; }
        uint crc = uint.MaxValue; foreach (byte b in bytes) { crc = table[(crc ^ b) & 255] ^ (crc >> 8); } return ~crc;
    }
}
