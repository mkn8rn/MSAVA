using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MSAVA_Shared.Diagnostics;
using NPOI.HSSF.UserModel;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Document metadata extraction.
/// Supports: DOCX, XLSX, PPTX (Office Open XML), XLS (Legacy Excel), ODT, ODS, ODP, PDF, RTF
/// </summary>
public static partial class DocumentMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // Office Open XML formats
        map["docx"] = (ExtractOfficeXml, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        map["xlsx"] = (ExtractOfficeXml, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        map["pptx"] = (ExtractOfficeXml, "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        // Legacy Excel (NPOI supported)
        map["xls"] = (ExtractLegacyExcel, "application/vnd.ms-excel");

        // OpenDocument formats
        map["odt"] = (ExtractOpenDocument, "application/vnd.oasis.opendocument.text");
        map["ods"] = (ExtractOpenDocument, "application/vnd.oasis.opendocument.spreadsheet");
        map["odp"] = (ExtractOpenDocument, "application/vnd.oasis.opendocument.presentation");

        // Other document formats
        map["pdf"] = (ExtractPdf, "application/pdf");
        map["rtf"] = (ExtractRtf, "application/rtf");
        
        // Note: DOC and PPT are NOT registered - they will fall through to InvalidMetadata
    }

    #region Office Open XML (DOCX, XLSX, PPTX)

    private static JsonDocument ExtractOfficeXml(Stream stream, long size)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        string? title = null;
        string? author = null;
        string? subject = null;
        string? keywords = null;
        string? created = null;
        string? modified = null;
        string? lastModifiedBy = null;
        int? pageCount = null;
        int? slideCount = null;
        int? wordCount = null;

        // Extract core properties
        var coreEntry = archive.GetEntry("docProps/core.xml");
        if (coreEntry is not null)
        {
            using var xmlStream = coreEntry.Open();
            var doc = new XmlDocument();
            doc.Load(xmlStream);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            nsMgr.AddNamespace("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
            nsMgr.AddNamespace("dcterms", "http://purl.org/dc/terms/");

            title = doc.SelectSingleNode("//dc:title", nsMgr)?.InnerText;
            author = doc.SelectSingleNode("//dc:creator", nsMgr)?.InnerText;
            subject = doc.SelectSingleNode("//dc:subject", nsMgr)?.InnerText;
            keywords = doc.SelectSingleNode("//cp:keywords", nsMgr)?.InnerText;
            created = doc.SelectSingleNode("//dcterms:created", nsMgr)?.InnerText;
            modified = doc.SelectSingleNode("//dcterms:modified", nsMgr)?.InnerText;
            lastModifiedBy = doc.SelectSingleNode("//cp:lastModifiedBy", nsMgr)?.InnerText;
        }

        // Extract app properties
        var appEntry = archive.GetEntry("docProps/app.xml");
        if (appEntry is not null)
        {
            using var xmlStream = appEntry.Open();
            var doc = new XmlDocument();
            doc.Load(xmlStream);

            var pagesNode = doc.GetElementsByTagName("Pages");
            if (pagesNode.Count > 0 && int.TryParse(pagesNode[0]?.InnerText, out var pages))
                pageCount = pages;

            var slidesNode = doc.GetElementsByTagName("Slides");
            if (slidesNode.Count > 0 && int.TryParse(slidesNode[0]?.InnerText, out var slides))
                slideCount = slides;

            var wordsNode = doc.GetElementsByTagName("Words");
            if (wordsNode.Count > 0 && int.TryParse(wordsNode[0]?.InnerText, out var words))
                wordCount = words;
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "OfficeDocument",
            Valid = true,
            Format = "Office Open XML",
            Title = InvalidMetadata.OrInvalid(title),
            Author = InvalidMetadata.OrInvalid(author),
            Subject = InvalidMetadata.OrInvalid(subject),
            Keywords = InvalidMetadata.OrInvalid(keywords),
            Created = InvalidMetadata.OrInvalid(created),
            Modified = InvalidMetadata.OrInvalid(modified),
            LastModifiedBy = InvalidMetadata.OrInvalid(lastModifiedBy),
            PageCount = InvalidMetadata.OrInvalid(pageCount),
            SlideCount = InvalidMetadata.OrInvalid(slideCount),
            WordCount = InvalidMetadata.OrInvalid(wordCount),
            Size = size
        });
    }

    #endregion

    #region Legacy Office (XLS) using NPOI

    private static JsonDocument ExtractLegacyExcel(Stream stream, long size)
    {
        try
        {
            using var workbook = new HSSFWorkbook(stream);
            var summary = workbook.SummaryInformation;
            var docSummary = workbook.DocumentSummaryInformation;

            return MetadataExtractor.ToJsonDocument(new
            {
                Type = "OfficeDocument",
                Valid = true,
                Format = "Microsoft Excel 97-2003",
                Title = InvalidMetadata.OrInvalid(summary?.Title),
                Author = InvalidMetadata.OrInvalid(summary?.Author),
                Subject = InvalidMetadata.OrInvalid(summary?.Subject),
                Keywords = InvalidMetadata.OrInvalid(summary?.Keywords),
                Created = InvalidMetadata.OrInvalid(summary?.CreateDateTime?.ToString("o")),
                Modified = InvalidMetadata.OrInvalid(summary?.LastSaveDateTime?.ToString("o")),
                LastAuthor = InvalidMetadata.OrInvalid(summary?.LastAuthor),
                SheetCount = workbook.NumberOfSheets,
                SheetNames = Enumerable.Range(0, workbook.NumberOfSheets)
                    .Select(i => workbook.GetSheetName(i))
                    .ToList(),
                Company = InvalidMetadata.OrInvalid(docSummary?.Company),
                Size = size
            });
        }
        catch (Exception ex) when (IsRecoverableLegacyExcelFailure(ex))
        {
            return MetadataExtractor.CreateInvalidMetadata(size, "Failed to read Excel file");
        }
    }

    private static bool IsRecoverableLegacyExcelFailure(Exception exception)
    {
        if (CriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is IOException
            or InvalidDataException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            || exception.GetType().Namespace?.StartsWith("NPOI", StringComparison.Ordinal) == true;
    }

    #endregion

    #region OpenDocument (ODT, ODS, ODP)

    private static JsonDocument ExtractOpenDocument(Stream stream, long size)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        string? title = null;
        string? author = null;
        string? subject = null;
        string? created = null;
        int? pageCount = null;
        int? tableCount = null;
        int? imageCount = null;
        int? objectCount = null;

        var metaEntry = archive.GetEntry("meta.xml");
        if (metaEntry is not null)
        {
            using var xmlStream = metaEntry.Open();
            var doc = new XmlDocument();
            doc.Load(xmlStream);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            nsMgr.AddNamespace("meta", "urn:oasis:names:tc:opendocument:xmlns:meta:1.0");

            title = doc.SelectSingleNode("//dc:title", nsMgr)?.InnerText;
            author = doc.SelectSingleNode("//dc:creator", nsMgr)?.InnerText;
            subject = doc.SelectSingleNode("//dc:subject", nsMgr)?.InnerText;
            created = doc.SelectSingleNode("//meta:creation-date", nsMgr)?.InnerText;

            var statsNode = doc.SelectSingleNode("//meta:document-statistic", nsMgr);
            if (statsNode?.Attributes != null)
            {
                foreach (XmlAttribute attr in statsNode.Attributes)
                {
                    var localName = attr.LocalName;
                    if (localName == "page-count" && int.TryParse(attr.Value, out var p)) pageCount = p;
                    if (localName == "table-count" && int.TryParse(attr.Value, out var t)) tableCount = t;
                    if (localName == "image-count" && int.TryParse(attr.Value, out var i)) imageCount = i;
                    if (localName == "object-count" && int.TryParse(attr.Value, out var o)) objectCount = o;
                }
            }
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "OpenDocument",
            Valid = true,
            Title = InvalidMetadata.OrInvalid(title),
            Author = InvalidMetadata.OrInvalid(author),
            Subject = InvalidMetadata.OrInvalid(subject),
            Created = InvalidMetadata.OrInvalid(created),
            PageCount = InvalidMetadata.OrInvalid(pageCount),
            TableCount = InvalidMetadata.OrInvalid(tableCount),
            ImageCount = InvalidMetadata.OrInvalid(imageCount),
            ObjectCount = InvalidMetadata.OrInvalid(objectCount),
            Size = size
        });
    }

    #endregion

    #region PDF

    private static JsonDocument ExtractPdf(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false);
        var content = text.Content;

        // Count pages
        var pageMatches = PageCountRegex().Matches(content);
        var pageCount = pageMatches.Count;

        // Extract info dictionary
        var title = ExtractPdfInfo(content, "/Title");
        var author = ExtractPdfInfo(content, "/Author");
        var subject = ExtractPdfInfo(content, "/Subject");
        var creator = ExtractPdfInfo(content, "/Creator");
        var producer = ExtractPdfInfo(content, "/Producer");
        var creationDate = ExtractPdfInfo(content, "/CreationDate");

        // Detect PDF version
        var versionMatch = PdfVersionRegex().Match(content);
        var pdfVersion = versionMatch.Success ? versionMatch.Groups[1].Value : null;

        // Check for encryption
        var isEncrypted = content.Contains("/Encrypt");

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "PDF",
            Valid = true,
            Version = InvalidMetadata.OrInvalid(pdfVersion),
            PageCount = InvalidMetadata.OrInvalid(pageCount > 0 ? pageCount : (int?)null),
            Title = InvalidMetadata.OrInvalid(title),
            Author = InvalidMetadata.OrInvalid(author),
            Subject = InvalidMetadata.OrInvalid(subject),
            Creator = InvalidMetadata.OrInvalid(creator),
            Producer = InvalidMetadata.OrInvalid(producer),
            CreationDate = InvalidMetadata.OrInvalid(ParsePdfDate(creationDate)),
            IsEncrypted = isEncrypted,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    [GeneratedRegex(@"/Type\s*/Page[^s]", RegexOptions.Compiled)]
    private static partial Regex PageCountRegex();

    [GeneratedRegex(@"%PDF-(\d+\.\d+)", RegexOptions.Compiled)]
    private static partial Regex PdfVersionRegex();

    private static string? ExtractPdfInfo(string content, string key)
    {
        var pattern = $@"{Regex.Escape(key)}\s*\(([^)]*)\)";
        var match = Regex.Match(content, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ParsePdfDate(string? pdfDate)
    {
        if (string.IsNullOrEmpty(pdfDate) || !pdfDate.StartsWith("D:", StringComparison.Ordinal))
            return pdfDate;

        var date = pdfDate.AsSpan(2);
        if (date.Length < 8)
            return pdfDate;

        if (!TryParseAsciiDigits(date[..4], out int year) ||
            !TryParseAsciiDigits(date.Slice(4, 2), out int month) ||
            !TryParseAsciiDigits(date.Slice(6, 2), out int day))
            return pdfDate;

        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return pdfDate;

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
            return pdfDate;

        return new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool TryParseAsciiDigits(ReadOnlySpan<char> digits, out int value)
    {
        value = 0;

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
                return false;

            value = (value * 10) + digit - '0';
        }

        return true;
    }

    #endregion

    #region RTF

    private static JsonDocument ExtractRtf(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        var content = text.Content;

        var title = ExtractRtfInfo(content, "title");
        var author = ExtractRtfInfo(content, "author");
        var subject = ExtractRtfInfo(content, "subject");
        var company = ExtractRtfInfo(content, "company");
        var manager = ExtractRtfInfo(content, "manager");

        // Count page breaks
        var pageBreaks = Regex.Matches(content, @"\\page\b").Count;

        // Estimate word count (rough)
        var textContent = Regex.Replace(content, @"\\[a-z]+\d*\s?|\{|\}", "");
        var wordCount = textContent.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "RTF",
            Valid = true,
            Title = InvalidMetadata.OrInvalid(title),
            Author = InvalidMetadata.OrInvalid(author),
            Subject = InvalidMetadata.OrInvalid(subject),
            Company = InvalidMetadata.OrInvalid(company),
            Manager = InvalidMetadata.OrInvalid(manager),
            ApproximatePages = InvalidMetadata.OrInvalid(pageBreaks > 0 ? pageBreaks + 1 : (int?)null),
            ApproximateWordCount = InvalidMetadata.OrInvalid(wordCount > 0 ? wordCount : (int?)null),
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    private static string? ExtractRtfInfo(string content, string field)
    {
        var pattern = $@"\\{field}\s+([^}}]+)";
        var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var value = match.Groups[1].Value.Trim();
            value = Regex.Replace(value, @"\\'[0-9a-f]{2}", "");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    #endregion
}
