using System.Globalization;
using System.Net;
using System.Text;
using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>
/// Tolerant OFX 1.02 (SGML-style) reader for Nubank exports. Does not use an XML parser:
/// scans tags, with or without closing tags. Never includes file content in exceptions.
/// </summary>
public static class NubankOfxReader
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxFieldChars = 2000;
    private const int MaxTagChars = 64;
    private const int MaxDepth = 64;

    static NubankOfxReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static ParsedStatement Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxBytes) throw new ImportFormatException("too_large", null);
        if (content.IsEmpty) throw new ImportFormatException("empty", null);

        var text = Decode(content);
        if (string.IsNullOrWhiteSpace(text)) throw new ImportFormatException("empty", null);

        var dtd = text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
        var ent = text.IndexOf("<!ENTITY", StringComparison.OrdinalIgnoreCase);
        var bad = dtd < 0 ? ent : ent < 0 ? dtd : Math.Min(dtd, ent);
        if (bad >= 0) throw new ImportFormatException("forbidden_dtd", LineAt(text, bad));

        if (text.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase) < 0
            || text.IndexOf("OFXHEADER", StringComparison.OrdinalIgnoreCase) < 0)
            throw new ImportFormatException("not_ofx", null);

        return Scan(text);
    }

    private static string Decode(ReadOnlySpan<byte> content)
    {
        var offset = 0;
        var bom = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
        if (bom) offset = 3;

        // Header is ASCII-compatible: read it as Latin-1 up to the first "<".
        var head = new StringBuilder();
        for (var i = offset; i < content.Length && i < offset + 2048 && content[i] != (byte)'<'; i++)
            head.Append((char)content[i]);
        var (charset, charsetLine) = HeaderValue(head.ToString(), "CHARSET");
        var (encoding, encodingLine) = HeaderValue(head.ToString(), "ENCODING");
        var cs = Classify(charset, charsetLine);
        var en = Classify(encoding, encodingLine);

        Encoding enc;
        if (bom || en == Kind.Utf8) enc = new UTF8Encoding(false);
        else if (cs == Kind.Cp1252 || en == Kind.Cp1252) enc = Encoding.GetEncoding(1252);
        else enc = new UTF8Encoding(false);
        return enc.GetString(content[offset..]);
    }

    private enum Kind { Defer, Utf8, Cp1252 }

    private static Kind Classify(string? value, int line)
    {
        if (string.IsNullOrEmpty(value)) return Kind.Defer;
        switch (value.ToLowerInvariant())
        {
            case "none" or "usascii":
                return Kind.Defer;
            case "utf-8" or "utf8":
                return Kind.Utf8;
            case "1252" or "windows-1252" or "cp1252" or "iso-8859-1" or "latin1" or "8859-1":
                return Kind.Cp1252;
            default:
                throw new ImportFormatException("malformed", line);
        }
    }

    private static (string? Value, int Line) HeaderValue(string head, string key)
    {
        var n = 0;
        foreach (var raw in head.Split((char)10))
        {
            n++;
            var line = raw.Trim();
            if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                return (line[(key.Length + 1)..].Trim(), n);
        }
        return (null, 0);
    }

    private static int LineAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private sealed class TrnBuilder(int line)
    {
        public int Line { get; } = line;
        public string? FitId, Posted, Amount, Memo;
    }

    private static ParsedStatement Scan(string text)
    {
        var stack = new List<string>();
        var line = 1;
        var statements = 0;
        var source = (ImportSource?)null;
        string? acctId = null, balAmt = null, balDate = null, dtStart = null, dtEnd = null;
        int balLine = 0;
        var lines = new List<ParsedLine>();
        TrnBuilder? trn = null;

        void FinishTrn()
        {
            if (trn is null) return;
            var t = trn;
            trn = null;
            if (string.IsNullOrEmpty(t.FitId) || t.Posted is null || t.Amount is null)
                throw new ImportFormatException("malformed", t.Line);
            lines.Add(new ParsedLine(t.FitId, ParseDate(t.Posted, t.Line), ParseAmount(t.Amount, t.Line), t.Memo ?? ""));
        }

        var pos = text.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        line = LineAt(text, pos);
        while (pos < text.Length)
        {
            var lt = text.IndexOf('<', pos);
            if (lt < 0) break;
            for (var i = pos; i < lt; i++) if (text[i] == '\n') line++;
            var gt = text.IndexOf('>', lt + 1);
            if (gt < 0 || gt - lt - 1 > MaxTagChars) throw new ImportFormatException("malformed", line);
            var tagLine = line;
            var tag = text.Substring(lt + 1, gt - lt - 1).Trim();
            var next = text.IndexOf('<', gt + 1);
            var valueEnd = next < 0 ? text.Length : next;
            var rawValue = text.Substring(gt + 1, valueEnd - gt - 1);
            for (var i = lt; i < valueEnd; i++) if (text[i] == '\n' && i >= gt + 1) line++;
            pos = valueEnd;

            if (tag.Length == 0 || tag[0] == '!' || tag[0] == '?') throw new ImportFormatException("malformed", tagLine);

            if (tag[0] == '/')
            {
                var name = tag[1..].ToUpperInvariant();
                if (stack.Count > 0 && stack[^1] == name)
                {
                    stack.RemoveAt(stack.Count - 1);
                    if (name == "STMTTRN") FinishTrn();
                    if (name == "BANKTRANLIST") FinishTrn();
                }
                continue;
            }

            var upper = tag.ToUpperInvariant();
            var value = rawValue.Trim();
            if (value.Length > MaxFieldChars) throw new ImportFormatException("malformed", tagLine);
            foreach (var ch in value)
                if (ch < 0x20 && ch != '	') throw new ImportFormatException("malformed", tagLine);

            if (value.Length == 0)
            {
                // aggregate start
                if (stack.Count >= MaxDepth) throw new ImportFormatException("malformed", tagLine);
                if (upper == "STMTTRN") { FinishTrn(); trn = new TrnBuilder(tagLine); }
                else if (upper == "BANKTRANLIST" || upper == "LEDGERBAL") FinishTrn();
                if (upper == "STMTRS" || upper == "CCSTMTRS")
                {
                    statements++;
                    if (statements > 1) throw new ImportFormatException("unsupported_multiple_statements", tagLine);
                    source = upper == "STMTRS" ? ImportSource.NubankChecking : ImportSource.NubankCard;
                }
                stack.Add(upper);
                continue;
            }

            // leaf with value (closing tag, if any, is ignored when it does not match stack top)
            value = WebUtility.HtmlDecode(value);
            foreach (var ch in value) // entities such as &#0; decode to control characters
                if (ch < 0x20 && ch != '	') throw new ImportFormatException("malformed", tagLine);
            var top = stack.Count > 0 ? stack[^1] : "";
            switch (upper)
            {
                case "ACCTID" when top is "BANKACCTFROM" or "CCACCTFROM": acctId = value; break;
                case "DTSTART" when top == "BANKTRANLIST": dtStart = value; break;
                case "DTEND" when top == "BANKTRANLIST": dtEnd = value; break;
                case "BALAMT" when top == "LEDGERBAL": balAmt = value; balLine = tagLine; break;
                case "DTASOF" when top == "LEDGERBAL": balDate = value; balLine = tagLine; break;
                case "FITID" when trn is not null && top == "STMTTRN": trn.FitId = value; break;
                case "DTPOSTED" when trn is not null && top == "STMTTRN": trn.Posted = value; break;
                case "TRNAMT" when trn is not null && top == "STMTTRN": trn.Amount = value; break;
                case "MEMO" when trn is not null && top == "STMTTRN": trn.Memo = value; break;
            }
        }
        FinishTrn();

        if (source is null) throw new ImportFormatException("malformed", null);

        ParsedBalance? ledger = null;
        if (balAmt is not null && balDate is not null)
            ledger = new ParsedBalance(ParseAmount(balAmt, balLine), ParseDate(balDate, balLine));

        return new ParsedStatement(
            source.Value,
            acctId,
            dtStart is null ? null : ParseDate(dtStart, null),
            dtEnd is null ? null : ParseDate(dtEnd, null),
            lines,
            ledger);
    }

    private static DateOnly ParseDate(string value, int? line)
    {
        if (value.Length < 8) throw new ImportFormatException("malformed", line);
        for (var i = 0; i < 8; i++)
            if (value[i] is < '0' or > '9') throw new ImportFormatException("malformed", line);
        if (!DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new ImportFormatException("malformed", line);
        return d;
    }

    private static decimal ParseAmount(string value, int? line)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var d))
            throw new ImportFormatException("malformed", line);
        return d;
    }
}
