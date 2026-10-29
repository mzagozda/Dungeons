using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Dungeons.Core.Level;

/// <summary>Canonical, lossless authoring serialization, independent of runtime replay identities.</summary>
public static class LevelSerializer
{
    /// <summary>Writes ordinally sorted object keys, original array order, minimal decimal numbers,
    /// two-space indentation and one final LF. Does not mutate the document or its exact-source hash.</summary>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="InvalidOperationException">The document is a code-only scenario without authored JSON.</exception>
    public static string Write(LevelDocument document)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        using JsonDocument source = JsonDocument.Parse(document.ToJson(), new JsonDocumentOptions { MaxDepth = Util.JsonData.MaxDepth });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) { WriteElement(source.RootElement, writer); }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
    }
    private static void WriteElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteElement(property.Value, writer); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (JsonElement item in element.EnumerateArray()) { WriteElement(item, writer); } writer.WriteEndArray(); break;
            case JsonValueKind.Number: writer.WriteRawValue(NormalizeNumber(element.GetRawText()), skipInputValidation: true); break;
            default: element.WriteTo(writer); break;
        }
    }
    // Operate on decimal digits instead of double/decimal: forward-compatible
    // metadata must retain integers above 2^53, tiny values and huge exponents.
    internal static string NormalizeNumber(string raw)
    {
        bool negative = raw[0] == '-'; int start = negative ? 1 : 0;
        int separator = raw.IndexOfAny(['e', 'E']); string mantissa = separator < 0 ? raw[start..] : raw[start..separator];
        int dot = mantissa.IndexOf('.'), fraction = dot < 0 ? 0 : mantissa.Length - dot - 1;
        string digits = mantissa.Replace(".", "").TrimStart('0');
        if (digits.Length == 0) { return "0"; }
        int length = digits.Length; digits = digits.TrimEnd('0'); int trailing = length - digits.Length;
        string exponent = separator < 0 ? "0" : raw[(separator + 1)..];
        string scientific = AddExponent(exponent, trailing - fraction + digits.Length - 1);
        string sign = negative ? "-" : "";
        if (int.TryParse(scientific, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int power) && power >= -6 && power < 21)
        {
            int point = power + 1;
            if (point <= 0) { return sign + "0." + new string('0', -point) + digits; }
            if (point >= digits.Length) { return sign + digits + new string('0', point - digits.Length); }
            return sign + digits[..point] + "." + digits[point..];
        }
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + scientific;
    }
    private static string AddExponent(string exponent, int delta)
    {
        if (long.TryParse(exponent, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number))
        {
            try { return checked(number + delta).ToString(CultureInfo.InvariantCulture); }
            catch (OverflowException) { /* Preserve an arbitrary-size exponent below. */ }
        }
        bool negative = exponent[0] == '-'; string magnitude = exponent.TrimStart('+', '-').TrimStart('0');
        // An exponent outside Int64 is much larger than the bounded mantissa
        // adjustment, so its sign cannot change. Linear decimal carry avoids BigInteger parsing.
        char[] digits = magnitude.ToCharArray(); long carry = negative ? -(long)delta : delta;
        for (int i = digits.Length - 1; i >= 0 && carry != 0; i--)
        {
            long sum = digits[i] - '0' + carry, digit = (sum % 10 + 10) % 10;
            digits[i] = (char)('0' + digit); carry = (sum - digit) / 10;
        }
        string result = ((carry > 0 ? carry.ToString(CultureInfo.InvariantCulture) : "") + new string(digits)).TrimStart('0');
        return (negative ? "-" : "") + result;
    }
}
