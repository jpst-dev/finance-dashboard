using System.Text;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Synthetic Nubank-like OFX builder. Only fictional data.</summary>
public sealed class OfxTestFile
{
    public sealed record Trn(string Type, string Posted, string Amount, string FitId, string Memo);

    private readonly List<Trn> _lines = new();
    private bool _card;
    private string _charset = "1252";
    private string _encodingHeader = "USASCII";
    private bool _bom;
    private bool _closedLeaves = true;
    private int _statements = 1;
    private string? _ledger = "100.50|20261031000000[-3:BRT]";
    private string _acctId = "ACC-0001";
    private string? _extraBeforeOfx;

    public static OfxTestFile Checking() => new();
    public static OfxTestFile Card() => new() { _card = true };

    public OfxTestFile Charset(string charset) { _charset = charset; return this; }
    public OfxTestFile Encoding(string encoding) { _encodingHeader = encoding; return this; }
    public OfxTestFile WithBom() { _bom = true; return this; }
    public OfxTestFile Unclosed() { _closedLeaves = false; return this; }
    public OfxTestFile Statements(int n) { _statements = n; return this; }
    public OfxTestFile AccountId(string id) { _acctId = id; return this; }
    public OfxTestFile NoLedger() { _ledger = null; return this; }
    public OfxTestFile Ledger(string amount, string asOf) { _ledger = amount + "|" + asOf; return this; }
    public OfxTestFile BeforeOfx(string text) { _extraBeforeOfx = text; return this; }

    public OfxTestFile Line(string posted, string amount, string memo, string? fitId = null, string type = "OTHER")
    {
        _lines.Add(new Trn(type, posted, amount, fitId ?? Guid.NewGuid().ToString(), memo));
        return this;
    }

    private string Leaf(string tag, string value) =>
        _closedLeaves ? $"<{tag}>{value}</{tag}>" : $"<{tag}>{value}";

    public string BuildText()
    {
        const string nl = "\r\n";
        var sb = new StringBuilder();
        sb.Append("OFXHEADER:100").Append(nl).Append("DATA:OFXSGML").Append(nl).Append("VERSION:102").Append(nl)
          .Append("SECURITY:NONE").Append(nl).Append("ENCODING:").Append(_encodingHeader).Append(nl)
          .Append("CHARSET:").Append(_charset).Append(nl).Append("COMPRESSION:NONE").Append(nl)
          .Append("OLDFILEUID:NONE").Append(nl).Append("NEWFILEUID:NONE").Append(nl).Append(nl);
        if (_extraBeforeOfx is not null) sb.Append(_extraBeforeOfx).Append(nl);
        sb.Append("<OFX>").Append(nl);
        sb.Append("<SIGNONMSGSRSV1><SONRS><STATUS>").Append(Leaf("CODE", "0")).Append(Leaf("SEVERITY", "INFO"))
          .Append("</STATUS>").Append(Leaf("LANGUAGE", "POR")).Append("</SONRS></SIGNONMSGSRSV1>").Append(nl);
        var (msgs, trnrs, rs, from) = _card
            ? ("CREDITCARDMSGSRSV1", "CCSTMTTRNRS", "CCSTMTRS", "CCACCTFROM")
            : ("BANKMSGSRSV1", "STMTTRNRS", "STMTRS", "BANKACCTFROM");
        sb.Append('<').Append(msgs).Append('>').Append(nl);
        for (var s = 0; s < _statements; s++)
        {
            sb.Append('<').Append(trnrs).Append('>').Append(Leaf("TRNUID", "1")).Append('<').Append(rs).Append('>')
              .Append(Leaf("CURDEF", "BRL")).Append(nl);
            sb.Append('<').Append(from).Append('>');
            if (!_card)
                sb.Append(Leaf("BANKID", "0260")).Append(Leaf("BRANCHID", "0001"));
            sb.Append(Leaf("ACCTID", _acctId));
            if (!_card) sb.Append(Leaf("ACCTTYPE", "CHECKING"));
            sb.Append("</").Append(from).Append('>').Append(nl);
            sb.Append("<BANKTRANLIST>").Append(Leaf("DTSTART", "20261001000000[-3:BRT]"))
              .Append(Leaf("DTEND", "20261031000000[-3:BRT]")).Append(nl);
            foreach (var t in _lines)
            {
                sb.Append("<STMTTRN>").Append(Leaf("TRNTYPE", t.Type)).Append(Leaf("DTPOSTED", t.Posted))
                  .Append(Leaf("TRNAMT", t.Amount)).Append(Leaf("FITID", t.FitId)).Append(Leaf("MEMO", t.Memo))
                  .Append("</STMTTRN>").Append(nl);
            }
            sb.Append("</BANKTRANLIST>").Append(nl);
            if (_ledger is not null)
            {
                var p = _ledger.Split('|');
                sb.Append("<LEDGERBAL>").Append(Leaf("BALAMT", p[0])).Append(Leaf("DTASOF", p[1])).Append("</LEDGERBAL>").Append(nl);
            }
            sb.Append("</").Append(rs).Append("></").Append(trnrs).Append('>').Append(nl);
        }
        sb.Append("</").Append(msgs).Append('>').Append(nl).Append("</OFX>").Append(nl);
        return sb.ToString();
    }

    public byte[] Build()
    {
        var text = BuildText();
        var enc = _charset == "1252"
            ? CodePagesEncodingProvider.Instance.GetEncoding(1252)!
            : new UTF8Encoding(false);
        var body = enc.GetBytes(text);
        if (!_bom) return body;
        return [0xEF, 0xBB, 0xBF, .. body];
    }
}
