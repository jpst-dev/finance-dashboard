using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FinanceDashboard.Infrastructure.Importing.Xlsx;

/// <summary>
/// Pre-validation of an uploaded xlsx before any spreadsheet parser touches it (hostile-upload defense).
/// Throws <see cref="ImportFormatException"/> carrying only the kind (never entry names or content):
/// <c>not_xlsx</c>, <c>encrypted_xlsx</c>, <c>xlsx_limits_exceeded</c>, <c>xlsx_unsafe_content</c>.
/// </summary>
/// <remarks>
/// Sizes are measured by inflating each entry's raw deflate data (located through our own reading of the
/// central directory and local headers) into a 64 KiB sink with a counter; declared lengths are never trusted.
/// That central-directory view must agree with <see cref="ZipArchive"/>'s (count and compressed sizes), so the
/// parts validated here are the parts later readers get. Control parts (<c>[Content_Types].xml</c>,
/// <c>xl/workbook.xml</c>, every <c>*.rels</c>) are parsed with <see cref="XmlReader"/> (DTD prohibited, no
/// resolver); other XML parts are only scanned for <c>&lt;!DOCTYPE</c> (and their prolog checked), and
/// <c>xl/media/*</c> is only counted.
/// </remarks>
public static class XlsxSafety
{
    public const int MaxEntries = 200;
    public const long MaxTotalUncompressed = 50L * 1024 * 1024;
    public const int MaxRatio = 100;
    public const long RatioMinBytes = 64 * 1024;

    /// <summary>Largest control part (content types, workbook, relationships) we accept to parse.</summary>
    public const int MaxControlPartBytes = 1024 * 1024;

    private const int BlockSize = 64 * 1024;
    private const string NotXlsx = "not_xlsx";
    private const string Encrypted = "encrypted_xlsx";
    private const string Limits = "xlsx_limits_exceeded";
    private const string Unsafe = "xlsx_unsafe_content";

    private static readonly byte[] CfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EncryptedPackageName = Encoding.Unicode.GetBytes("EncryptedPackage");

    private static readonly string[] ForbiddenPrefixes =
        ["xl/externalLinks/", "xl/embeddings/", "xl/activeX/", "xl/macrosheets/"];

    private static readonly string[] ForbiddenContentTypes = ["macroEnabled", "vbaProject", "macrosheet"];

    private static readonly XmlReaderSettings ControlPartSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxControlPartBytes,
        MaxCharactersFromEntities = 1024,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    private sealed record RawEntry(ushort Method, long CompressedSize, long DataStart);

    public static void Validate(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var span = content.AsSpan();
        if (span.StartsWith(CfbSignature))
            throw Fail(span.IndexOf(EncryptedPackageName) >= 0 ? Encrypted : NotXlsx);
        if (!span.StartsWith(ZipSignature)) throw Fail(NotXlsx);

        var raw = ReadCentralDirectory(content); // also enforces MaxEntries
        var buffer = new byte[BlockSize];

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            var entries = archive.Entries;
            if (entries.Count != raw.Count) throw Fail(NotXlsx);
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].CompressedLength != raw[i].CompressedSize) throw Fail(NotXlsx);

            CheckNames(entries);
            var sizes = CountUncompressed(content, raw, buffer);
            InspectXmlParts(entries, sizes, buffer);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            throw Fail(NotXlsx);
        }
    }

    private static ImportFormatException Fail(string kind) => new(kind, null);

    // ---- zip structure (our own view of the central directory) ----

    private static List<RawEntry> ReadCentralDirectory(byte[] c)
    {
        const int eocdSize = 22;
        var eocd = -1;
        for (var p = c.Length - eocdSize; p >= Math.Max(0, c.Length - eocdSize - ushort.MaxValue); p--)
        {
            if (U32(c, p) == 0x06054b50) { eocd = p; break; }
        }
        if (eocd < 0) throw Fail(NotXlsx);
        // A Zip64 end-of-central-directory locator right before the EOCD: never needed for a few-MB workbook.
        if (eocd >= 20 && U32(c, eocd - 20) == 0x07064b50) throw Fail(NotXlsx);

        var disk = U16(c, eocd + 4);
        var cdDisk = U16(c, eocd + 6);
        var entriesOnDisk = U16(c, eocd + 8);
        var total = U16(c, eocd + 10);
        long cdSize = U32(c, eocd + 12);
        long cdOffset = U32(c, eocd + 16);
        if (disk != 0 || cdDisk != 0 || entriesOnDisk != total) throw Fail(NotXlsx);
        if (total > MaxEntries) throw Fail(Limits);
        if (cdOffset + cdSize > eocd) throw Fail(NotXlsx);

        var list = new List<RawEntry>(total);
        var pos = cdOffset;
        var cdEnd = cdOffset + cdSize;
        for (var i = 0; i < total; i++)
        {
            if (pos + 46 > cdEnd || U32(c, pos) != 0x02014b50) throw Fail(NotXlsx);
            var flags = U16(c, pos + 8);
            var method = U16(c, pos + 10);
            long compressed = U32(c, pos + 20);
            long declared = U32(c, pos + 24);
            var nameLength = U16(c, pos + 28);
            var extraLength = U16(c, pos + 30);
            var commentLength = U16(c, pos + 32);
            long localOffset = U32(c, pos + 42);
            if (compressed == uint.MaxValue || declared == uint.MaxValue || localOffset == uint.MaxValue)
                throw Fail(NotXlsx); // Zip64
            if ((flags & 1) != 0) throw Fail(NotXlsx); // zip-level encryption
            pos += 46L + nameLength + extraLength + commentLength;
            if (pos > cdEnd) throw Fail(NotXlsx);

            if (localOffset + 30 > c.Length || U32(c, localOffset) != 0x04034b50) throw Fail(NotXlsx);
            var dataStart = localOffset + 30 + U16(c, localOffset + 26) + U16(c, localOffset + 28);
            if (dataStart + compressed > c.Length) throw Fail(NotXlsx);
            list.Add(new RawEntry(method, compressed, dataStart));
        }
        return list;
    }

    private static ushort U16(byte[] c, long p) =>
        p + 2 <= c.Length ? BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan((int)p, 2)) : throw Fail(NotXlsx);

    private static uint U32(byte[] c, long p) =>
        p >= 0 && p + 4 <= c.Length ? BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan((int)p, 4)) : throw Fail(NotXlsx);

    // ---- names ----

    private static void CheckNames(IReadOnlyList<ZipArchiveEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasContentTypes = false, hasWorkbook = false;
        foreach (var entry in entries)
        {
            var name = entry.FullName;
            if (name.Length == 0 || name.Contains("..", StringComparison.Ordinal) || name[0] is '/' or '\\' ||
                name.Contains(':') || name.Any(char.IsControl))
                throw Fail(Unsafe);

            var normalized = name.Replace('\\', '/');
            if (!seen.Add(normalized)) throw Fail(Unsafe);
            if (IsForbiddenPart(normalized)) throw Fail(Unsafe);

            hasContentTypes |= normalized.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
            hasWorkbook |= normalized.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase);
        }
        if (!hasContentTypes || !hasWorkbook) throw Fail(NotXlsx);
    }

    private static bool IsForbiddenPart(string name)
    {
        var fileName = name[(name.LastIndexOf('/') + 1)..];
        return fileName.Equals("vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("xl/connections.xml", StringComparison.OrdinalIgnoreCase) ||
               ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // ---- sizes (counted, never declared) ----

    private static long[] CountUncompressed(byte[] content, List<RawEntry> raw, byte[] sink)
    {
        var sizes = new long[raw.Count];
        long total = 0;
        for (var i = 0; i < raw.Count; i++)
        {
            var e = raw[i];
            long size = 0;
            switch (e.Method)
            {
                case 0: // stored
                    size = e.CompressedSize;
                    total += size;
                    if (total > MaxTotalUncompressed) throw Fail(Limits);
                    break;
                case 8: // deflate: inflate the raw bytes ourselves, block by block, into the sink
                    using (var input = new MemoryStream(content, (int)e.DataStart, (int)e.CompressedSize, writable: false))
                    using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
                    {
                        int n;
                        while ((n = inflater.Read(sink, 0, sink.Length)) > 0)
                        {
                            size += n;
                            total += n;
                            if (total > MaxTotalUncompressed) throw Fail(Limits);
                            if (size >= RatioMinBytes && size > MaxRatio * Math.Max(e.CompressedSize, 1)) throw Fail(Limits);
                        }
                    }
                    break;
                default:
                    throw Fail(NotXlsx);
            }
            sizes[i] = size;
        }
        return sizes;
    }

    // ---- XML parts ----

    private static void InspectXmlParts(IReadOnlyList<ZipArchiveEntry> entries, long[] sizes, byte[] sink)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var name = entries[i].FullName.Replace('\\', '/');
            if (name.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase)) continue; // counted only

            var isRels = name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
            if (!isRels && !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            var isContentTypes = name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
            var isControl = isRels || isContentTypes || name.Equals("xl/workbook.xml", StringComparison.OrdinalIgnoreCase);
            var isDrawing = name.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase);
            if (isControl && sizes[i] > MaxControlPartBytes) throw Fail(Limits);

            using var scan = new DoctypeScanStream(entries[i].Open());
            try
            {
                if (isControl) InspectControlPart(scan, isRels, isContentTypes);
                else if (!isDrawing) CheckProlog(scan); // drawings: byte scan only, never parsed
                scan.Drain(sink);
            }
            catch (XmlException)
            {
                throw Fail(scan.SawDoctype ? Unsafe : NotXlsx);
            }
            if (scan.SawDoctype) throw Fail(Unsafe);
        }
    }

    private static void InspectControlPart(Stream stream, bool isRels, bool isContentTypes)
    {
        using var reader = XmlReader.Create(stream, ControlPartSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (isContentTypes)
            {
                var type = reader.GetAttribute("ContentType");
                if (type is not null && ForbiddenContentTypes.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    throw Fail(Unsafe);
            }
            if (isRels)
            {
                var mode = reader.GetAttribute("TargetMode");
                if (mode is not null && mode.Trim().Equals("External", StringComparison.OrdinalIgnoreCase))
                    throw Fail(Unsafe);
            }
        }
    }

    /// <summary>Reads up to the root element only: a DTD can only live in the prolog.</summary>
    private static void CheckProlog(Stream stream)
    {
        using var reader = XmlReader.Create(stream, ControlPartSettings);
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element) return;
    }

    /// <summary>Read-only pass-through that flags <c>&lt;!DOCTYPE</c> in UTF-8/ASCII or UTF-16 (LE/BE), across reads.</summary>
    private sealed class DoctypeScanStream(Stream inner) : Stream
    {
        private static readonly byte[][] Patterns =
        [
            Encoding.ASCII.GetBytes("<!DOCTYPE"),
            Encoding.Unicode.GetBytes("<!DOCTYPE"),
            Encoding.BigEndianUnicode.GetBytes("<!DOCTYPE"),
        ];

        private const int TailMax = 17; // longest pattern (18 bytes) minus one
        private readonly byte[] _tail = new byte[TailMax];
        private int _tailLength;
        private byte[] _window = new byte[4096 + TailMax];

        public bool SawDoctype { get; private set; }

        public void Drain(byte[] sink)
        {
            while (Read(sink, 0, sink.Length) > 0) { }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            if (n > 0 && !SawDoctype) Scan(buffer[..n]);
            return n;
        }

        private void Scan(ReadOnlySpan<byte> data)
        {
            var length = _tailLength + data.Length;
            if (_window.Length < length) _window = new byte[length];
            _tail.AsSpan(0, _tailLength).CopyTo(_window);
            data.CopyTo(_window.AsSpan(_tailLength));
            var window = _window.AsSpan(0, length);
            foreach (var pattern in Patterns)
            {
                if (window.IndexOf(pattern) >= 0) { SawDoctype = true; return; }
            }
            _tailLength = Math.Min(TailMax, length);
            window[^_tailLength..].CopyTo(_tail);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
