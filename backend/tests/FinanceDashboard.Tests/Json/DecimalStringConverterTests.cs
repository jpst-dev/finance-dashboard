using System.Text.Json;
using FinanceDashboard.Api.Json;

namespace FinanceDashboard.Tests.Json;

public sealed class DecimalStringConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new DecimalStringConverter() } };

    [Theory]
    [InlineData("\"1.234,56\"")]
    [InlineData("\"12.345\"")]
    [InlineData("12.5")]
    [InlineData("\"abc\"")]
    [InlineData("\"\"")]
    [InlineData("\" 1.00\"")]
    [InlineData("\"12345678901234567.00\"")]
    [InlineData("\"12.50\\n\"")]
    [InlineData("\"١٢.٥٠\"")]
    public void Invalid_inputs_are_rejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<decimal>(json, Options));

    [Theory]
    [InlineData("\"-12.50\"", "-12.50")]
    [InlineData("\"0\"", "0")]
    [InlineData("\"1234.5\"", "1234.5")]
    public void Valid_inputs_are_accepted(string json, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Deserialize<decimal>(json, Options));

    [Fact]
    public void Writes_two_decimals_with_invariant_dot() =>
        Assert.Equal("\"12.50\"", JsonSerializer.Serialize(12.5m, Options));
}
