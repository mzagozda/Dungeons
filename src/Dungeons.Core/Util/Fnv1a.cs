namespace Dungeons.Core.Util;

/// <summary>Stable FNV-1a 64-bit hashing with explicit little-endian integer encoding.</summary>
public struct Fnv1a
{
    private ulong _value;
    /// <summary>Offset basis for an empty byte sequence.</summary>
    public const ulong OffsetBasis = 14695981039346656037UL;
    /// <summary>Creates a fresh hash.</summary>
    public Fnv1a() { _value = OffsetBasis; }
    /// <summary>Current hash.</summary>
    public readonly ulong Value => _value;
    /// <summary>Adds one byte.</summary>
    public void Add(byte value) { _value = unchecked((_value ^ value) * 1099511628211UL); }
    /// <summary>Adds a byte sequence.</summary>
    public void Add(ReadOnlySpan<byte> bytes) { for (int i = 0; i < bytes.Length; i++) { Add(bytes[i]); } }
    /// <summary>Adds a signed 32-bit little-endian integer.</summary>
    public void Add(int value) => Add(unchecked((uint)value));
    /// <summary>Adds an unsigned 32-bit little-endian integer.</summary>
    public void Add(uint value) { for (int i = 0; i < 4; i++) { Add((byte)value); value >>= 8; } }
    /// <summary>Adds an unsigned 64-bit little-endian integer.</summary>
    public void Add(ulong value) { for (int i = 0; i < 8; i++) { Add((byte)value); value >>= 8; } }
    /// <summary>Adds a boolean as one byte.</summary>
    public void Add(bool value) => Add((byte)(value ? 1 : 0));
    /// <summary>Adds UTF-8 without allocating; malformed UTF-16 uses U+FFFD.</summary>
    public void Add(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            int code = value[i];
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { code = char.ConvertToUtf32(value[i], value[++i]); }
            else if (char.IsSurrogate(value[i])) { code = 0xFFFD; }
            if (code < 0x80) { Add((byte)code); }
            else if (code < 0x800) { Add((byte)(0xC0 | (code >> 6))); Add((byte)(0x80 | (code & 0x3F))); }
            else if (code < 0x10000) { Add((byte)(0xE0 | (code >> 12))); Add((byte)(0x80 | ((code >> 6) & 0x3F))); Add((byte)(0x80 | (code & 0x3F))); }
            else { Add((byte)(0xF0 | (code >> 18))); Add((byte)(0x80 | ((code >> 12) & 0x3F))); Add((byte)(0x80 | ((code >> 6) & 0x3F))); Add((byte)(0x80 | (code & 0x3F))); }
        }
    }
    /// <summary>Hashes a complete byte sequence.</summary>
    public static ulong Compute(ReadOnlySpan<byte> bytes) { var hash = new Fnv1a(); hash.Add(bytes); return hash.Value; }
}
