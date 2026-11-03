namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>
/// Entry point for import files: validates size and picks the reader by content, never by file name. A zip
/// (<c>PK</c>) or an OLE/CFB container (encrypted xlsx, legacy xls) goes to the Itaú invoice reader, which
/// validates the package and throws <c>not_xlsx</c>/<c>encrypted_xlsx</c>/…; anything else is read as OFX
/// (<c>not_ofx</c> when it is not one) and handed to its bank profile.
/// </summary>
public static class StatementReader
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];
    private static ReadOnlySpan<byte> CfbSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static ParsedStatement Read(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxBytes) throw new ImportFormatException("too_large", null);
        if (content.IsEmpty) throw new ImportFormatException("empty", null);

        if (content.StartsWith(ZipSignature) || content.StartsWith(CfbSignature))
            return ItauInvoiceReader.Read(content.ToArray());

        var document = OfxReader.Parse(content);
        return OfxProfiles.For(document).Build(document);
    }
}
