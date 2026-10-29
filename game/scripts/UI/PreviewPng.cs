using System.Buffers.Binary;
using System.IO.Compression;

namespace Dungeons.Game.UI;

/// <summary>Bounded PNG thumbnail admission before the native decoder. Original package bytes are never changed.</summary>
public static class PreviewPng
{
    public const int Width = 320, Height = 180, MaxBytes = 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    /// <summary>Checks CRCs, colour modes, scanline/filter bounds and palette indexes; strips ancillary payloads and rebuilds a bounded zlib stream.</summary>
    public static bool TrySanitize(ReadOnlySpan<byte> input, out byte[] png)
    {
        png = [];
        if (input.Length is < 45 or > MaxBytes || !input[..8].SequenceEqual(Signature)) { return false; }
        byte[]? header = null, palette = null, transparency = null; bool seenData = false, afterData = false, ended = false;
        using var compressed = new MemoryStream();
        int depth = 0, colour = 0, channels = 0, interlace = 0, offset = 8;
        while (offset <= input.Length - 12)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(input[offset..]);
            if (size > (uint)(input.Length - offset - 12)) { return false; }
            int length = (int)size; ReadOnlySpan<byte> type = input.Slice(offset + 4, 4), data = input.Slice(offset + 8, length);
            for (int i = 0; i < 4; i++) { if (type[i] is not (>= 65 and <= 90) and not (>= 97 and <= 122)) { return false; } }
            if ((type[2] & 32) != 0 || Crc(input.Slice(offset + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(input[(offset + 8 + length)..])) { return false; }
            if (header is null && !type.SequenceEqual("IHDR"u8)) { return false; }
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header is not null || length != 13 || BinaryPrimitives.ReadUInt32BigEndian(data) != Width || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != Height || data[10] != 0 || data[11] != 0 || data[12] > 1) { return false; }
                depth = data[8]; colour = data[9]; interlace = data[12]; channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                if (channels == 0 || (colour is 0 or 3 ? depth is not (1 or 2 or 4 or 8) && (colour != 0 || depth != 16) : depth is not (8 or 16))) { return false; }
                header = data.ToArray();
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (seenData || palette is not null || colour is 0 or 4 || length is < 3 or > 768 || length % 3 != 0 || colour == 3 && length / 3 > 1 << depth) { return false; }
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (seenData || transparency is not null || colour is 4 or 6 || (colour == 0 && length != 2) || (colour == 2 && length != 6) || (colour == 3 && (palette is null || length == 0 || length > palette.Length / 3))) { return false; }
                if (colour is 0 or 2) { for (int i = 0; i < length; i += 2) { if (BinaryPrimitives.ReadUInt16BigEndian(data[i..]) > (1 << depth) - 1) { return false; } } }
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (afterData || colour == 3 && palette is null) { return false; } seenData = true; compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            { if (!seenData || length != 0 || offset + 12 != input.Length) { return false; } ended = true; break; }
            else { if ((type[0] & 32) == 0) { return false; } if (seenData) { afterData = true; } }
            offset += length + 12;
        }
        if (!ended || header is null) { return false; }
        using var filtered = new MemoryStream(); compressed.Position = 0;
        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            int[] startX = [0, 4, 0, 2, 0, 1, 0], startY = [0, 0, 4, 0, 2, 0, 1], stepX = [8, 8, 4, 4, 2, 2, 1], stepY = [8, 8, 8, 4, 4, 2, 2];
            for (int pass = 0; pass < (interlace == 0 ? 1 : 7); pass++)
            {
                int width = interlace == 0 ? Width : (Width - startX[pass] + stepX[pass] - 1) / stepX[pass];
                int height = interlace == 0 ? Height : (Height - startY[pass] + stepY[pass] - 1) / stepY[pass];
                int rowBytes = (width * channels * depth + 7) / 8;
                byte[] row = new byte[rowBytes], previous = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    int filter = inflater.ReadByte(); if (filter is < 0 or > 4) { return false; }
                    inflater.ReadExactly(row); filtered.WriteByte((byte)filter); filtered.Write(row);
                    if (colour != 3) { continue; }
                    for (int x = 0; x < rowBytes; x++)
                    {
                        int a = x > 0 ? row[x - 1] : 0, b = previous[x], c = x > 0 ? previous[x - 1] : 0;
                        row[x] = unchecked((byte)(row[x] + (filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 })));
                    }
                    int mask = (1 << depth) - 1;
                    for (int x = 0; x < width; x++) { if (((row[x * depth / 8] >> (8 - depth - x * depth % 8)) & mask) >= palette!.Length / 3) { return false; } }
                    (row, previous) = (previous, row);
                }
            }
            if (inflater.ReadByte() != -1) { return false; }
        }
        catch (Exception error) when (error is IOException or InvalidDataException) { return false; }
        using var cleanData = new MemoryStream();
        using (var deflater = new ZLibStream(cleanData, CompressionLevel.Fastest, leaveOpen: true)) { filtered.Position = 0; filtered.CopyTo(deflater); }
        using var output = new MemoryStream(); output.Write(Signature); Chunk(output, "IHDR"u8, header);
        if (palette is not null) { Chunk(output, "PLTE"u8, palette); } if (transparency is not null) { Chunk(output, "tRNS"u8, transparency); }
        Chunk(output, "IDAT"u8, cleanData.ToArray()); Chunk(output, "IEND"u8, []); png = output.ToArray(); return true;
    }
    private static int Paeth(int a, int b, int c)
    { int p = a + b - c, pa = System.Math.Abs(p - a), pb = System.Math.Abs(p - b), pc = System.Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    { uint crc = uint.MaxValue; foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); } } return ~crc; }
    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length); output.Write(word); output.Write(type); output.Write(data);
        byte[] crcData = new byte[data.Length + 4]; type.CopyTo(crcData); data.CopyTo(crcData.AsSpan(4)); BinaryPrimitives.WriteUInt32BigEndian(word, Crc(crcData)); output.Write(word);
    }
}
