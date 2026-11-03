using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class CategoryRuleMatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RuleView Rule(string pattern, CategoryKind kind = CategoryKind.Expense,
        DateTimeOffset? createdAt = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), pattern, Guid.NewGuid(), kind, createdAt ?? T0);

    [Theory]
    [InlineData("BARBEARIA CENTRO", "BAR", false)]
    [InlineData("PIX MARIANA SOUZA", "MARIA", false)]
    [InlineData("COMPRA SUPERMERCADO EXEMPLO 12", "SUPERMERCADO EXEMPLO", true)]
    [InlineData("SUPERMERCADO EXEMPLO", "SUPERMERCADO EXEMPLO", true)]
    [InlineData("BARBEARIA BAR CENTRO", "BAR", true)]      // 1a ocorrência falha, 2a vale
    [InlineData("XBAR BAR", "BAR", true)]
    [InlineData("LOJA A1", "A", false)]
    public void Matches_requires_word_boundaries(string description, string pattern, bool expected) =>
        Assert.Equal(expected, CategoryRuleMatcher.Matches(description, pattern));

    [Fact]
    public void Matches_keeps_punctuation()
    {
        Assert.False(CategoryRuleMatcher.Matches("COMPRA IFOOD LOJA", "IFOOD *LOJA"));
        Assert.True(CategoryRuleMatcher.Matches("COMPRA IFOOD *LOJA 1", "IFOOD *LOJA"));
    }

    [Fact]
    public void Winner_longest_pattern_wins()
    {
        var shortR = Rule("SUPERMERCADO");
        var longR = Rule("SUPERMERCADO EXEMPLO");
        Assert.Equal(longR.Id, CategoryRuleMatcher.Winner("Supermercado Exemplo 12", -10m, [shortR, longR]));
        Assert.Equal(shortR.Id, CategoryRuleMatcher.Winner("Supermercado Outro", -10m, [shortR, longR]));
        Assert.Null(CategoryRuleMatcher.Winner("Padaria", -10m, [shortR, longR]));
    }

    [Fact]
    public void Winner_tie_prefers_oldest_then_id()
    {
        var older = Rule("ASSINATURA", createdAt: T0, id: Guid.Parse("00000000-0000-0000-0000-0000000000ff"));
        var newer = Rule("ASSINATURA", createdAt: T0.AddDays(1), id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.Equal(older.Id, CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", -5m, [newer, older]));

        var lowId = Rule("ASSINATURA", createdAt: T0, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.Equal(lowId.Id, CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", -5m, [older, lowId]));
    }

    [Fact]
    public void Winner_ignores_kind_incompatible_with_sign()
    {
        var income = Rule("ASSINATURA FICTICIA", CategoryKind.Income);
        var expense = Rule("ASSINATURA", CategoryKind.Expense);
        // valor negativo: Income é ignorada mesmo sendo mais longa
        Assert.Equal(expense.Id, CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", -5m, [income, expense]));
        // estorno (positivo): Expense é ignorada
        Assert.Equal(income.Id, CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", 5m, [income, expense]));
        Assert.Null(CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", 5m, [expense]));
        Assert.Null(CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", -5m, [income]));
    }

    [Fact]
    public void Winner_zero_amount_matches_nothing()
    {
        Assert.Null(CategoryRuleMatcher.Winner("ASSINATURA FICTICIA", 0m,
            [Rule("ASSINATURA"), Rule("ASSINATURA", CategoryKind.Income)]));
    }

    [Theory]
    [InlineData("AB", null)]
    [InlineData("ABC", "ABC")]
    [InlineData("  supermercado   exemplo ", "SUPERMERCADO EXEMPLO")]
    [InlineData("Padaria Açaí", "PADARIA ACAI")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizePattern_bounds(string? raw, string? expected) =>
        Assert.Equal(expected, CategoryRuleMatcher.NormalizePattern(raw));

    [Fact]
    public void NormalizePattern_upper_bound()
    {
        Assert.NotNull(CategoryRuleMatcher.NormalizePattern(new string('A', 100)));
        Assert.Null(CategoryRuleMatcher.NormalizePattern(new string('A', 101)));
    }

    [Theory]
    [InlineData("Pix FULANO FICTICIO ***.123.456-** 12/05", "PIX FULANO FICTICIO")]
    [InlineData("Compra Supermercado Exemplo 12/05/2026", "COMPRA SUPERMERCADO EXEMPLO")]
    [InlineData("Assinatura Ficticia 03 11", "ASSINATURA FICTICIA")]
    [InlineData("Loja Exemplo 12345", "LOJA EXEMPLO")]
    [InlineData("Padaria S.A. - Centro", "PADARIA S.A. - CENTRO")]
    [InlineData("IFOOD *LOJA EXEMPLO", "IFOOD *LOJA EXEMPLO")]
    [InlineData("Padaria 24 Horas", "PADARIA 24 HORAS")]
    [InlineData("AB 12", null)]
    public void SuggestPattern_strips_masked_documents_dates_and_trailing_numbers(string description, string? expected) =>
        Assert.Equal(expected, CategoryRuleMatcher.SuggestPattern(description));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(1234)]
    public void Property_winner_is_deterministic_under_rule_order(int seed)
    {
        var rng = new Random(seed);
        var patterns = new[] { "SUPER", "SUPERMERCADO", "SUPERMERCADO EXEMPLO", "EXEMPLO", "MERCADO", "SUPERMERCADO EXEMPLO" };
        var rules = patterns
            .Select((p, i) => Rule(p, i % 2 == 0 ? CategoryKind.Expense : CategoryKind.Income,
                T0.AddDays(rng.Next(0, 3)), new Guid(rng.Next(), 0, 0, new byte[8])))
            .ToList();
        var expected = CategoryRuleMatcher.Winner("Compra Supermercado Exemplo 9", -10m, rules);
        for (var i = 0; i < 50; i++)
        {
            var shuffled = rules.OrderBy(_ => rng.Next()).ToList();
            Assert.Equal(expected, CategoryRuleMatcher.Winner("Compra Supermercado Exemplo 9", -10m, shuffled));
        }
    }
}
