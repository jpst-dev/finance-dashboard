using System.Text.RegularExpressions;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public sealed class ImportWarningTests
{
    [Fact]
    public void Legacy_warning_json_without_overridable_reads_as_true()
    {
        var w = StoredPreview.DeserializeWarnings("[{\"code\":\"AccountIdentifierMismatch\",\"severity\":\"blocking\",\"message\":\"x\"}]");
        Assert.True(w[0].Overridable);
    }

    [Fact]
    public void Missing_lines_warning_is_blocking_and_not_overridable()
    {
        var w = ImportWarning.InvoiceHasMissingLines(3);
        Assert.Equal(ImportWarning.Blocking, w.Severity);
        Assert.False(w.Overridable);
        Assert.True(ImportWarning.InvoiceTotalMismatch().Overridable);
    }

    [Fact]
    public void New_warning_messages_carry_no_content()
    {
        var messages = new[]
        {
            ImportWarning.InvoiceTotalMismatch().Message,
            ImportWarning.InvoiceHasMissingLines(7).Message,
            ImportWarning.LedgerBalanceBeforeLastLine().Message,
            ImportWarning.PossibleDuplicate(7).Message,
            ImportWarning.UnknownLineKinds(7).Message,
            ImportWarning.UnknownCardSuffix(7).Message,
        };
        foreach (var m in messages) Assert.Matches(new Regex("^[A-Za-z0-9 ().,';-]+$"), m);
    }
}
