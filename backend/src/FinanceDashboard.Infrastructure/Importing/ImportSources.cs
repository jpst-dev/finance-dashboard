using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Per-source rules shared by the importers and the import service.</summary>
public static class ImportSources
{
    public static AccountType ExpectedAccountType(ImportSource source) => source switch
    {
        ImportSource.NubankCard or ImportSource.ItauCard => AccountType.CreditCard,
        _ => AccountType.Checking,
    };

    /// <summary>Itaú files carry no stable line ids, so their source keys are derived from line content.</summary>
    public static bool UsesContentKeys(ImportSource source) =>
        source is ImportSource.ItauChecking or ImportSource.ItauCard;

    public static bool AccountIdMatches(ImportSource source, string accountExternalIdentifier, string fileAccountId)
    {
        if (!UsesContentKeys(source))
            return string.Equals(accountExternalIdentifier.Trim(), fileAccountId.Trim(), StringComparison.Ordinal);

        var a = Normalize(accountExternalIdentifier);
        var f = Normalize(fileAccountId);
        return a.Length == 0 || f.Length == 0 || string.Equals(a, f, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
}
