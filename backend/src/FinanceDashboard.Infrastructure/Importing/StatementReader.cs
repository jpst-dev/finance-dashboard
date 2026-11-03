namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Entry point for import files: validates size, reads the format and applies the bank profile.</summary>
public static class StatementReader
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static ParsedStatement Read(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxBytes) throw new ImportFormatException("too_large", null);
        if (content.IsEmpty) throw new ImportFormatException("empty", null);

        var document = OfxReader.Parse(content);
        return OfxProfiles.For(document).Build(document);
    }
}
