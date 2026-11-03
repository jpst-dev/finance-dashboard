using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class NameKeysTests
{
    [Theory]
    [InlineData("  José   Luis  da Silva ", "JOSE LUIS DA SILVA")]
    [InlineData("joão", "JOAO")]
    [InlineData("Ação\tÇedilha", "ACAO CEDILHA")]
    public void Normalize_uppercases_strips_accents_collapses_spaces(string raw, string expected) =>
        Assert.Equal(expected, NameKeys.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_empty_is_null(string? raw) => Assert.Null(NameKeys.Normalize(raw));

    [Fact]
    public void Coincide_prefix_in_both_directions()
    {
        Assert.True(NameKeys.Coincide("MARIA LUISA SANTOS", "MARIA L"));
        Assert.True(NameKeys.Coincide("MARIA L", "MARIA LUISA SANTOS"));
    }

    [Fact]
    public void Coincide_equal_names() => Assert.True(NameKeys.Coincide("MARIA LUISA", "MARIA LUISA"));

    [Fact]
    public void Coincide_requires_min_length_7()
    {
        Assert.False(NameKeys.Coincide("MARIA", "MARIA LUISA"));
        Assert.True(NameKeys.Coincide("MARIA L", "MARIA LUISA"));
        Assert.Equal(7, NameKeys.MinLength);
    }

    [Fact]
    public void Coincide_null_is_false()
    {
        Assert.False(NameKeys.Coincide(null, "MARIA LUISA"));
        Assert.False(NameKeys.Coincide("MARIA LUISA", null));
        Assert.False(NameKeys.Coincide(null, null));
    }

    [Fact]
    public void Coincide_non_prefix_is_false() => Assert.False(NameKeys.Coincide("ALUGUEL MARCO", "CLIENTE X SA"));
}
