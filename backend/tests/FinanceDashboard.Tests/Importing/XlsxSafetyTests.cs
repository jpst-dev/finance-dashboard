using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Importing.Xlsx;

namespace FinanceDashboard.Tests.Importing;

public sealed class XlsxSafetyTests
{
    private const string Marker = "SEGREDO_MARCADOR";

    private static XlsxTestFile Valid() =>
        XlsxTestFile.Invoice(total: "150.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00", "Parcela 2 de 10")
            .Line("2026-10-05", "PADARIA FICTICIA", "30.00", cardKind: "Virtual", cardNumber: "****9876");

    private static string KindOf(byte[] content) =>
        Assert.Throws<ImportFormatException>(() => XlsxSafety.Validate(content)).Kind;

    private static byte[] Xml(string root = "<x/>") =>
        Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + root);

    // ---- valid files ----

    [Fact]
    public void Valid_synthetic_invoice_with_logo_passes()
    {
        var bytes = Valid().WithLogo().Build();
        Assert.Contains(XlsxTestFile.ReadEntries(bytes), e => e.Name == "xl/media/image1.png");
        XlsxSafety.Validate(bytes);
    }

    [Fact]
    public void Every_builder_variant_passes()
    {
        var variants = new[]
        {
            Valid().FooterFormula(),
            Valid().Date1904(),
            Valid().HiddenFirstSheet(),
            Valid().InlineStrings(),
            Valid().FormulaInLine(0).AmountAsText(1, "R$ 30,00").DateAsText(0, "03/10/2026"),
            Valid().BlankRowBefore(1).Header("Valor", "Data", "Lançamento").SecondSummaryBlock(),
            Valid().WithoutSummary(),
            Valid().Relationship("xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", external: false),
            Valid().ContentType("xl/custom.xml", "application/xml").Entry("xl/custom.xml", Xml()),
            Valid().WithLogo().FooterFormula().Date1904().HiddenFirstSheet().InlineStrings(),
        };
        foreach (var v in variants) XlsxSafety.Validate(v.Build());
    }

    // ---- container / signature ----

    [Fact]
    public void Cfb_encrypted_is_encrypted_xlsx() =>
        Assert.Equal("encrypted_xlsx", KindOf(XlsxTestFile.EncryptedCfb()));

    [Fact]
    public void Legacy_xls_is_not_xlsx() =>
        Assert.Equal("not_xlsx", KindOf(XlsxTestFile.LegacyXls()));

    [Fact]
    public void Random_bytes_are_not_xlsx()
    {
        var bytes = new byte[4096];
        new Random(7).NextBytes(bytes);
        bytes[0] = 0x00;
        Assert.Equal("not_xlsx", KindOf(bytes));
        Assert.Equal("not_xlsx", KindOf([]));
        Assert.Equal("not_xlsx", KindOf("PK\u0003\u0004"u8.ToArray()));
    }

    [Fact]
    public void Truncated_zip_is_not_xlsx()
    {
        var bytes = Valid().WithLogo().Build();
        Assert.Equal("not_xlsx", KindOf(bytes[..(bytes.Length / 2)]));
        Assert.Equal("not_xlsx", KindOf(bytes[..^30]));
    }

    [Fact]
    public void Missing_workbook_is_not_xlsx()
    {
        var parts = XlsxTestFile.ReadEntries(Valid().Build()).Where(p => p.Name != "xl/workbook.xml").ToArray();
        Assert.Equal("not_xlsx", KindOf(XlsxTestFile.Raw(parts)));
    }

    [Fact]
    public void Missing_content_types_is_not_xlsx()
    {
        var parts = XlsxTestFile.ReadEntries(Valid().Build()).Where(p => p.Name != "[Content_Types].xml").ToArray();
        Assert.Equal("not_xlsx", KindOf(XlsxTestFile.Raw(parts)));
    }

    // ---- limits ----

    [Fact]
    public void Too_many_entries_is_limits_exceeded()
    {
        var parts = XlsxTestFile.ReadEntries(Valid().Build());
        var filler = Enumerable.Range(0, XlsxSafety.MaxEntries + 1 - parts.Count)
            .Select(i => ($"xl/media/f{i}.bin", new byte[] { 1 }));
        var all = parts.Concat(filler).ToArray();
        Assert.Equal(201, all.Length);
        Assert.Equal("xlsx_limits_exceeded", KindOf(XlsxTestFile.Raw(all)));

        XlsxSafety.Validate(XlsxTestFile.Raw(all[..^1])); // exactly 200 entries is fine
    }

    [Fact]
    public void Zip_bomb_is_limits_exceeded()
    {
        const long size = 60L * 1024 * 1024;
        var bomb = XlsxTestFile.ZipBomb(size);
        // Payload ratio is below MaxRatio, so the total counter (not the ratio rule) has to stop it.
        Assert.True(bomb.Length * (long)XlsxSafety.MaxRatio > size);
        using (var zip = new ZipArchive(new MemoryStream(bomb), ZipArchiveMode.Read))
            Assert.Equal(size, zip.GetEntry("xl/media/image2.bin")!.Length);

        AssertStopsFastWithoutAllocating(bomb);
    }

    [Fact]
    public void Lying_declared_size_is_caught_by_counting()
    {
        var bomb = XlsxTestFile.LyingSizeBomb();
        using (var zip = new ZipArchive(new MemoryStream(bomb), ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry("xl/media/image2.bin")!;
            Assert.Equal(1024, entry.Length); // what the headers claim
            using var s = entry.Open();
            Assert.Equal(1024, Drain(s)); // what ZipArchive yields: the lie is invisible through it
        }

        AssertStopsFastWithoutAllocating(bomb);
    }

    [Fact]
    public void High_ratio_entry_is_limits_exceeded() =>
        Assert.Equal("xlsx_limits_exceeded", KindOf(Valid().Entry("xl/media/image2.bin", new byte[1024 * 1024]).Build()));

    [Fact]
    public void Small_repetitive_part_below_ratio_threshold_passes() =>
        XlsxSafety.Validate(Valid().Entry("xl/media/image2.bin", new byte[XlsxSafety.RatioMinBytes - 1]).Build());

    // ---- unsafe content ----

    [Theory]
    [InlineData("../x.xml")]
    [InlineData("/x.xml")]
    [InlineData("xl\\..\\x")]
    [InlineData("C:x")]
    [InlineData("\\x.xml")]
    public void Path_traversal_and_absolute_names_are_unsafe(string name) =>
        Assert.Equal("xlsx_unsafe_content", KindOf(Valid().Entry(name, Xml()).Build()));

    [Theory]
    [InlineData("xl/vbaProject.bin", null)]
    [InlineData("XL/VBAPROJECT.BIN", null)]
    [InlineData(null, "application/vnd.ms-excel.sheet.macroEnabled.main+xml")]
    [InlineData(null, "application/vnd.ms-office.vbaProject")]
    [InlineData("xl/macrosheets/sheet1.xml", null)]
    public void Macro_parts_are_unsafe(string? entry, string? workbookContentType)
    {
        var f = Valid();
        if (entry is not null) f.Entry(entry, [1, 2, 3]);
        if (workbookContentType is not null) f.ContentType("xl/workbook.xml", workbookContentType);
        Assert.Equal("xlsx_unsafe_content", KindOf(f.Build()));
    }

    [Theory]
    [InlineData("xl/externalLinks/externalLink1.xml")]
    [InlineData("xl/ExternalLinks/externalLink1.xml")]
    [InlineData("xl/connections.xml")]
    [InlineData("xl/embeddings/oleObject1.bin")]
    [InlineData("xl/activeX/activeX1.xml")]
    [InlineData("xl\\activeX\\activeX1.xml")]
    public void External_links_connections_embeddings_activex_are_unsafe(string name) =>
        Assert.Equal("xlsx_unsafe_content", KindOf(Valid().Entry(name, Xml()).Build()));

    [Theory]
    [InlineData("_rels/.rels")]
    [InlineData("xl/_rels/workbook.xml.rels")]
    [InlineData("xl/worksheets/_rels/sheet1.xml.rels")]
    [InlineData("xl/drawings/_rels/drawing1.xml.rels")]
    public void External_relationship_is_unsafe(string relsPart) =>
        Assert.Equal("xlsx_unsafe_content",
            KindOf(Valid().WithLogo().Relationship(relsPart, "https://example.invalid/x", external: true).Build()));

    [Fact]
    public void Duplicate_entry_names_are_unsafe()
    {
        var parts = XlsxTestFile.ReadEntries(Valid().Build());
        var workbook = parts.Single(p => p.Name == "xl/workbook.xml");
        Assert.Equal("xlsx_unsafe_content", KindOf(XlsxTestFile.Raw([.. parts, ("XL/Workbook.xml", workbook.Data)])));
    }

    [Theory]
    [InlineData("[Content_Types].xml", "utf-8")]
    [InlineData("_rels/.rels", "utf-8")]
    [InlineData("xl/workbook.xml", "utf-8")]
    [InlineData("xl/_rels/workbook.xml.rels", "utf-8")]
    [InlineData("xl/worksheets/sheet1.xml", "utf-8")]
    [InlineData("xl/sharedStrings.xml", "utf-8")]
    [InlineData("xl/styles.xml", "utf-8")]
    [InlineData("xl/drawings/drawing1.xml", "utf-8")]
    [InlineData("xl/workbook.xml", "utf-16")]
    [InlineData("xl/worksheets/sheet1.xml", "utf-16")]
    [InlineData("xl/worksheets/sheet1.xml", "utf-16BE")]
    public void Doctype_in_any_xml_part_is_unsafe(string part, string encoding)
    {
        var f = Valid().WithLogo();
        f.Entry(part, WithDoctype(XlsxTestFile.ReadPart(f.Build(), part), encoding));
        Assert.Equal("xlsx_unsafe_content", KindOf(f.Build()));
    }

    [Theory]
    [InlineData("xl/worksheets/sheet9.dat", "utf-8")]
    [InlineData("xl/worksheets/sheet9.dat", "utf-16")]
    [InlineData("xl/media/x.xml", "utf-8")]
    [InlineData("xl/media/image3.png", "utf-16BE")]
    [InlineData("customXml/item1.bin", "utf-8")]
    public void Doctype_in_any_entry_regardless_of_name_is_unsafe(string name, string encoding)
    {
        // Packaging reaches parts by relationship + content type, not by extension or folder.
        var f = Valid()
            .Entry(name, WithDoctype("<worksheet/>", encoding))
            .ContentType(name, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Assert.Equal("xlsx_unsafe_content", KindOf(f.Build()));
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")]
    [InlineData("application/vnd.ms-office.activeX+xml")]
    [InlineData("application/vnd.openxmlformats-officedocument.oleObject")]
    [InlineData("application/vnd.openxmlformats-officedocument.package")]
    public void Renamed_part_with_forbidden_content_type_is_unsafe(string type) =>
        Assert.Equal("xlsx_unsafe_content", KindOf(Valid().Entry("xl/c.xml", Xml()).ContentType("xl/c.xml", type).Build()));

    [Theory]
    [InlineData("package")]
    [InlineData("oleObject")]
    [InlineData("externalLink")]
    [InlineData("connections")]
    [InlineData("control")]
    public void Relationship_to_forbidden_part_type_is_unsafe(string relType) =>
        Assert.Equal("xlsx_unsafe_content", KindOf(Valid()
            .Relationship("xl/_rels/workbook.xml.rels", "c.xml", external: false,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + relType)
            .Build()));

    [Fact]
    public void Raw_writer_control_case_passes() => XlsxSafety.Validate(XlsxTestFile.Tampered(XlsxTestFile.ZipTamper.None));

    [Theory]
    [InlineData(XlsxTestFile.ZipTamper.LocalCompressedSizeMismatch)]
    [InlineData(XlsxTestFile.ZipTamper.Method9)]
    [InlineData(XlsxTestFile.ZipTamper.EncryptionFlag)]
    [InlineData(XlsxTestFile.ZipTamper.Zip64Size)]
    public void Tampered_zip_headers_are_not_xlsx(XlsxTestFile.ZipTamper tamper) =>
        Assert.Equal("not_xlsx", KindOf(XlsxTestFile.Tampered(tamper)));

    [Fact]
    public void Media_is_never_parsed_as_xml()
    {
        // Garbage that is not XML at all, named like XML, inside xl/media: counted only, so it passes.
        XlsxSafety.Validate(Valid().Entry("xl/media/image9.xml", [0x3C, 0xFF, 0x00, 0x26]).Build());
    }

    [Fact]
    public void Malformed_control_part_is_not_xlsx() =>
        Assert.Equal("not_xlsx", KindOf(Valid().Entry("xl/_rels/workbook.xml.rels", Xml("<Relationships>")).Build()));

    [Fact]
    public void Exception_messages_are_kind_only()
    {
        var hostile = new List<byte[]>
        {
            Valid().Entry($"../{Marker}.xml", Xml()).Build(),
            Valid().Entry($"xl/externalLinks/{Marker}.xml", Xml()).Build(),
            Valid().Entry($"xl/media/{Marker}.bin", new byte[1024 * 1024]).Build(),
            Valid().Relationship("xl/_rels/workbook.xml.rels", $"https://{Marker}.invalid/", external: true).Build(),
            Valid().ContentType($"xl/{Marker}.xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml").Build(),
            Valid().Entry("xl/workbook.xml", WithDoctype($"<{Marker}/>", "utf-8")).Build(),
            Valid().Entry("xl/workbook.xml", Xml($"<{Marker}>")).Build(),
            XlsxTestFile.Raw(($"{Marker}.xml", Xml())),
            XlsxTestFile.EncryptedCfb(),
            XlsxTestFile.LyingSizeBomb(),
        };
        foreach (var bytes in hostile)
        {
            var ex = Assert.Throws<ImportFormatException>(() => XlsxSafety.Validate(bytes));
            Assert.Equal($"Invalid import file: {ex.Kind}", ex.Message);
            Assert.DoesNotContain(Marker, ex.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Null(ex.InnerException);
        }
    }

    // ---- helpers ----

    private static void AssertStopsFastWithoutAllocating(byte[] bomb)
    {
        XlsxSafety.Validate(Valid().Build()); // warm-up (JIT) so the timing measures the bomb only
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var ex = Assert.Throws<ImportFormatException>(() => XlsxSafety.Validate(bomb));
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal("xlsx_limits_exceeded", ex.Kind);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.ElapsedMilliseconds} ms");
        Assert.True(allocated < 8L * 1024 * 1024, $"allocated {allocated} bytes");
    }

    private static long Drain(Stream s)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int n;
        while ((n = s.Read(buffer)) > 0) total += n;
        return total;
    }

    private static byte[] WithDoctype(string xml, string encoding)
    {
        var body = xml.StartsWith("<?xml", StringComparison.Ordinal) ? xml[(xml.IndexOf("?>", StringComparison.Ordinal) + 2)..] : xml;
        var doctype = "<!DOCTYPE r [<!ENTITY e SYSTEM \"file:///etc/hostname\">]>";
        var enc = encoding switch
        {
            "utf-16" => (Encoding)new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf-16BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            _ => new UTF8Encoding(false),
        };
        var decl = $"<?xml version=\"1.0\" encoding=\"{(encoding == "utf-8" ? "UTF-8" : "UTF-16")}\" standalone=\"yes\"?>";
        return [.. enc.GetPreamble(), .. enc.GetBytes(decl + doctype + body)];
    }

    [Fact]
    public void Builder_parts_are_well_formed_xml()
    {
        var bytes = Valid().WithLogo().HiddenFirstSheet().FooterFormula().Build();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        foreach (var (name, data) in XlsxTestFile.ReadEntries(bytes))
        {
            if (!name.EndsWith(".xml", StringComparison.Ordinal) && !name.EndsWith(".rels", StringComparison.Ordinal)) continue;
            using var reader = XmlReader.Create(new MemoryStream(data), settings);
            while (reader.Read()) { }
        }
    }
}
