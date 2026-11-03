using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FinanceDashboard.Api.Json;

/// <summary>Money as a JSON string with a dot and at most 2 decimals ("12.50"); numbers and other formats are rejected.</summary>
public sealed partial class DecimalStringConverter : JsonConverter<decimal>
{
    [GeneratedRegex(@"^-?\d{1,16}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyPattern();

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Money must be a JSON string like \"12.50\".");
        }

        var text = reader.GetString();
        if (text is null || !MoneyPattern().IsMatch(text))
        {
            throw new JsonException("Invalid money format; use a string like \"12.50\".");
        }

        return decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}
