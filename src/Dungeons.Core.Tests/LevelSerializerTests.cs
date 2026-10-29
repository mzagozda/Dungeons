using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class LevelSerializerTests
{
    [Theory]
    [InlineData("-0.000e+99999999999999999999", "0")]
    [InlineData("1.23000e+2", "123")]
    [InlineData("100000e-5", "1")]
    [InlineData("0.00000100", "0.000001")]
    [InlineData("0.000000100", "1e-7")]
    [InlineData("1000000000000000000000", "1e21")]
    [InlineData("9007199254740993", "9007199254740993")]
    [InlineData("1.23456789123456789123456789123456789", "1.23456789123456789123456789123456789")]
    [InlineData("1.000e-100000", "1e-100000")]
    [InlineData("10e+99999999999999999999", "1e100000000000000000000")]
    [InlineData("0.10e-99999999999999999999", "1e-100000000000000000000")]
    [InlineData("10e+9223372036854775807", "1e9223372036854775808")]
    [InlineData("0.10e-9223372036854775808", "1e-9223372036854775809")]
    [InlineData("10e-99999999999999999999", "1e-99999999999999999998")]
    [InlineData("0.10e+99999999999999999999", "1e99999999999999999998")]
    public void DecimalCanonicalizationIsExactAndIdempotent(string input, string expected)
    { Assert.Equal(expected, LevelSerializer.NormalizeNumber(input)); Assert.Equal(expected, LevelSerializer.NormalizeNumber(expected)); }

    [Fact]
    public void CanonicalWriteRoundTripsByteIdenticallyWithoutMutatingTheBoundDocument()
    {
        LevelDocument original = MissionAssets.Load("game/levels/mossy_hollow/level.json");
        string raw = original.ToJson(), identity = original.SourceHash; ulong hash = new GameSession(original, 1).Hash();
        string first = LevelSerializer.Write(original); LevelLoadResult loaded = LevelLoader.Load(first, MissionAssets.Content);
        Assert.True(loaded.Success); Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Info); Assert.Equal(first, LevelSerializer.Write(loaded.Document!));
        Assert.Equal(raw, original.ToJson()); Assert.Equal(identity, original.SourceHash); Assert.Equal(hash, new GameSession(original, 1).Hash());
        Assert.EndsWith("\n", first); Assert.DoesNotContain("\r", first); Assert.DoesNotContain("\n\n", first); Assert.StartsWith("{\n  ", first);
        byte[] bytes = Encoding.UTF8.GetBytes(first); Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Fact]
    public void KeyOrderAndCultureDoNotAffectCanonicalBytesAndArraysKeepTheirOrder()
    {
        LevelDocument original = MissionAssets.Load("game/levels/mossy_hollow/level.json");
        JsonObject reversed = ReverseKeys(original.ToJsonObject()).AsObject();
        LevelDocument other = LevelLoader.Load(reversed.ToJsonString(), MissionAssets.Content).Document!;
        string expected = LevelSerializer.Write(original); CultureInfo before = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Assert.Equal(expected, LevelSerializer.Write(other)); }
        finally { CultureInfo.CurrentCulture = before; }
        using JsonDocument result = JsonDocument.Parse(expected);
        Assert.Equal(original.ToJsonObject()["entities"]!.AsArray().Select(node => node!["id"]?.ToString()), result.RootElement.GetProperty("entities").EnumerateArray().Select(node => node.TryGetProperty("id", out var id) ? id.GetString() : null));
    }

    [Fact]
    public void UnknownNestedMetadataAndExactNumbersSurviveCanonicalAuthoring()
    {
        LevelDocument original = MissionAssets.Load("game/levels/mossy_hollow/level.json");
        original.Source!["futureData"] = JsonNode.Parse("{\"z\":[9007199254740993,0.0000000000000000000000000000000000001,10e99999999999999999999],\"a\":\"Zażółć 🌲\\nline\"}");
        string text = LevelSerializer.Write(original);
        Assert.Contains("9007199254740993", text); Assert.Contains("1e-37", text); Assert.Contains("1e100000000000000000000", text);
        LevelLoadResult roundTrip = LevelLoader.Load(text, MissionAssets.Content); Assert.True(roundTrip.Success);
        Assert.Equal(text, LevelSerializer.Write(roundTrip.Document!)); Assert.Equal("Zażółć 🌲\nline", roundTrip.Document!.ToJsonObject()["futureData"]!["a"]!.GetValue<string>());
    }

    [Fact]
    public void CodeOnlyDocumentsAndNullHaveExplicitErrors()
    { Assert.Throws<InvalidOperationException>(() => LevelSerializer.Write(new(2, 2))); Assert.Throws<ArgumentNullException>(() => LevelSerializer.Write(null!)); }

    [Fact]
    public void GeneratedDecimalValuesMatchAnIndependentExactIntegerOracle()
    {
        var random = new Random(73);
        for (int i = 0; i < 2000; i++)
        {
            string input = (random.Next(2) == 0 ? "-" : "") + random.Next(1000000).ToString(CultureInfo.InvariantCulture) + "." + random.Next(1000000).ToString("D6", CultureInfo.InvariantCulture) + "00E" + random.Next(-100, 101).ToString("+000;-000;000", CultureInfo.InvariantCulture);
            string normalized = LevelSerializer.NormalizeNumber(input); var (a, powerA) = Exact(input); var (b, powerB) = Exact(normalized);
            int common = System.Math.Min(powerA, powerB);
            Assert.Equal(a * BigInteger.Pow(10, powerA - common), b * BigInteger.Pow(10, powerB - common));
            Assert.Equal(normalized, LevelSerializer.NormalizeNumber(normalized));
            using JsonDocument document = JsonDocument.Parse(normalized); Assert.Equal(JsonValueKind.Number, document.RootElement.ValueKind);
        }
        static (BigInteger Numerator, int Power) Exact(string text)
        {
            int e = text.IndexOfAny(['e', 'E']); string mantissa = e < 0 ? text : text[..e]; int dot = mantissa.IndexOf('.');
            int power = (e < 0 ? 0 : int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture)) - (dot < 0 ? 0 : mantissa.Length - dot - 1);
            return (BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture), power);
        }
    }

    private static JsonNode ReverseKeys(JsonNode node)
    {
        if (node is JsonObject obj) { var result = new JsonObject(); foreach (var pair in obj.Reverse()) { result[pair.Key] = pair.Value is null ? null : ReverseKeys(pair.Value); } return result; }
        if (node is JsonArray array) { return new JsonArray(array.Select(item => item is null ? null : ReverseKeys(item)).ToArray()); }
        return node.DeepClone();
    }
}
