using System.Buffers.Binary;
using Dungeons.Game.Tests;
using Dungeons.Game.UI;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class PreviewPngTests
{
    [Theory]
    [InlineData(0, 1, 0)] [InlineData(0, 16, 1)] [InlineData(2, 8, 0)] [InlineData(2, 16, 1)]
    [InlineData(3, 2, 0)] [InlineData(3, 8, 1)] [InlineData(4, 8, 0)] [InlineData(6, 16, 1)]
    public void ValidColourModesAndInterlacePassesRoundtripWithoutMetadata(int colour, int depth, int interlace)
    {
        byte[] source = PreviewPngFixtures.Build(colour, depth, interlace, metadata: true);
        Assert.True(PreviewPng.TrySanitize(source, out byte[] clean)); Assert.True(PreviewPng.TrySanitize(clean, out byte[] again));
        Assert.Equal(clean, again); Assert.False(System.Text.Encoding.ASCII.GetString(clean).Contains("private metadata", StringComparison.Ordinal));
        Assert.Equal((uint)320, BinaryPrimitives.ReadUInt32BigEndian(clean.AsSpan(16)));
    }
    [Fact]
    public void CorruptCrcOversizedDimensionsInvalidFiltersAndInflationAreRejected()
    {
        byte[] source = PreviewPngFixtures.Build(6, 8, 0); source[^1] ^= 1; Assert.False(PreviewPng.TrySanitize(source, out _));
        Assert.False(PreviewPng.TrySanitize(new byte[PreviewPng.MaxBytes + 1], out _));
        Assert.False(PreviewPng.TrySanitize(PreviewPngFixtures.Build(6, 8, 0, width: 999999), out _));
        Assert.False(PreviewPng.TrySanitize(PreviewPngFixtures.Build(6, 8, 0, filter: 5), out _));
        Assert.False(PreviewPng.TrySanitize(PreviewPngFixtures.Build(6, 8, 0, extra: true), out _));
        byte[] badStream = PreviewPngFixtures.Build(6, 8, 0); int idat = PreviewPngFixtures.Find(badStream, "IDAT"); badStream[idat + 8] = 0;
        PreviewPngFixtures.FixCrc(badStream, idat); Assert.False(PreviewPng.TrySanitize(badStream, out _));
        Assert.False(PreviewPng.TrySanitize(PreviewPngFixtures.Build(3, 8, 0, invalidIndex: true), out _));
    }
    [Fact]
    public void TruncationAndGeneratedBytesNeverEscapeTheBoundary()
    {
        byte[] source = PreviewPngFixtures.Build(2, 8, 0);
        for (int length = 0; length < source.Length; length++) { Assert.False(PreviewPng.TrySanitize(source.AsSpan(0, length), out _)); }
        var random = new Random(1729);
        for (int i = 0; i < 1000; i++) { byte[] bytes = new byte[random.Next(0, 1500)]; random.NextBytes(bytes); Assert.False(PreviewPng.TrySanitize(bytes, out _)); }
    }
}
