using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public sealed class ImportSourcesTests
{
    [Theory]
    [InlineData(ImportSource.NubankChecking, AccountType.Checking)]
    [InlineData(ImportSource.NubankCard, AccountType.CreditCard)]
    [InlineData(ImportSource.ItauChecking, AccountType.Checking)]
    [InlineData(ImportSource.ItauCard, AccountType.CreditCard)]
    public void Expected_account_type_per_source(ImportSource source, AccountType expected) =>
        Assert.Equal(expected, ImportSources.ExpectedAccountType(source));

    [Fact]
    public void Only_itau_sources_use_content_keys()
    {
        Assert.False(ImportSources.UsesContentKeys(ImportSource.NubankChecking));
        Assert.False(ImportSources.UsesContentKeys(ImportSource.NubankCard));
        Assert.True(ImportSources.UsesContentKeys(ImportSource.ItauChecking));
        Assert.True(ImportSources.UsesContentKeys(ImportSource.ItauCard));
    }

    [Fact]
    public void Itau_account_id_ignores_non_digits_and_leading_zeros()
    {
        Assert.True(ImportSources.AccountIdMatches(ImportSource.ItauChecking, "0012345", "12.345"));
        Assert.False(ImportSources.AccountIdMatches(ImportSource.ItauChecking, "12345", "12346"));
        Assert.True(ImportSources.AccountIdMatches(ImportSource.ItauCard, "abc", "12346"));
    }

    [Fact]
    public void Nubank_account_id_is_trimmed_and_case_sensitive()
    {
        Assert.True(ImportSources.AccountIdMatches(ImportSource.NubankChecking, " ACC-1 ", "ACC-1"));
        Assert.False(ImportSources.AccountIdMatches(ImportSource.NubankChecking, "acc-1", "ACC-1"));
    }
}
