using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MSAVA_BLL.Utils;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Text and data file metadata extraction.
/// Supports: TXT, LOG, INI, YAML, JSON, CSV, XML, HTML, MD
/// </summary>
public static partial class TextMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // Plain text formats
        map["txt"] = (ExtractText, "text/plain");
        map["log"] = (ExtractLog, "text/plain");
        map["ini"] = (ExtractIni, "text/plain");
        map["yaml"] = (ExtractYaml, "application/x-yaml");
        map["yml"] = (ExtractYaml, "application/x-yaml");

        // Structured data formats
        map["json"] = (ExtractJson, "application/json");
        map["csv"] = (ExtractCsv, "text/csv");
        map["tsv"] = (ExtractTsv, "text/tab-separated-values");
        map["xml"] = (ExtractXml, "application/xml");

        // Markup formats
        map["html"] = (ExtractHtml, "text/html");
        map["htm"] = (ExtractHtml, "text/html");
        map["md"] = (ExtractMarkdown, "text/markdown");
        map["markdown"] = (ExtractMarkdown, "text/markdown");
    }

    #region Plain Text

    private static JsonDocument ExtractText(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;

        var lines = content.Split('\n');
        var words = content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        var encoding = text.Encoding.WebName;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Text",
            Valid = true,
            LineCount = lines.Length,
            WordCount = words.Length,
            CharacterCount = content.Length,
            Encoding = encoding,
            HasBom = text.Encoding.GetPreamble().Length > 0,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    private static JsonDocument ExtractLog(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;
        var lines = content.Split('\n');

        var hasTimestamps = lines.Take(10).Count(l => TimestampRegex().IsMatch(l)) > 3;
        var hasLogLevels = lines.Take(10).Count(l => LogLevelRegex().IsMatch(l)) > 3;

        var errorCount = lines.Count(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
        var warnCount = lines.Count(l => l.Contains("WARN", StringComparison.OrdinalIgnoreCase));
        var infoCount = lines.Count(l => l.Contains("INFO", StringComparison.OrdinalIgnoreCase));

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "LogFile",
            Valid = true,
            LineCount = lines.Length,
            HasTimestamps = hasTimestamps,
            HasLogLevels = hasLogLevels,
            ErrorCount = errorCount,
            WarningCount = warnCount,
            InfoCount = infoCount,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}|\d{2}:\d{2}:\d{2}", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\b(ERROR|WARN|INFO|DEBUG|TRACE|FATAL)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LogLevelRegex();

    private static JsonDocument ExtractIni(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;
        var lines = content.Split('\n');

        var sections = new List<string>();
        var keyCount = 0;
        var commentCount = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                sections.Add(trimmed[1..^1]);
            else if (trimmed.Contains('='))
                keyCount++;
            else if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                commentCount++;
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "INI",
            SectionCount = sections.Count,
            Sections = sections.Take(20).ToList(),
            KeyCount = keyCount,
            CommentCount = commentCount,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    private static JsonDocument ExtractYaml(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;
        var lines = content.Split('\n');

        var documentCount = lines.Count(l => l.Trim() == "---");
        var keyCount = lines.Count(l => l.Contains(':') && !l.TrimStart().StartsWith('#'));
        var listItemCount = lines.Count(l => l.TrimStart().StartsWith('-'));
        var commentCount = lines.Count(l => l.TrimStart().StartsWith('#'));

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "YAML",
            DocumentCount = Math.Max(1, documentCount),
            KeyCount = keyCount,
            ListItemCount = listItemCount,
            CommentCount = commentCount,
            LineCount = lines.Length,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    #endregion

    #region Structured Data

    private static JsonDocument ExtractJson(Stream stream, long size)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "JSON",
            RootType = root.ValueKind.ToString(),
            PropertyCount = root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().Count() : (int?)null,
            ArrayLength = root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : (int?)null,
            MaxDepth = CalculateJsonDepth(root),
            TotalElements = CountJsonElements(root),
            Size = size
        });
    }

    private static int CalculateJsonDepth(JsonElement element, int depth = 0)
    {
        var maxDepth = depth;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                maxDepth = Math.Max(maxDepth, CalculateJsonDepth(prop.Value, depth + 1));
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                maxDepth = Math.Max(maxDepth, CalculateJsonDepth(item, depth + 1));
        }

        return maxDepth;
    }

    private static int CountJsonElements(JsonElement element)
    {
        var count = 1;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                count += CountJsonElements(prop.Value);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                count += CountJsonElements(item);
        }

        return count;
    }

    private static JsonDocument ExtractCsv(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var lines = ReadAnalyzedLines(text.Content);

        var firstLine = lines.FirstOrDefault();
        string[] headers = firstLine?.Split(',') ?? [];
        var columnCount = headers.Length;

        var hasHeader = headers.Length > 0 &&
            headers.All(header => !double.TryParse(
                header.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _));

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "CSV",
            RowCount = lines.Count,
            ColumnCount = columnCount,
            HasHeader = hasHeader,
            Headers = hasHeader ? headers.Take(20).Select(h => h.Trim()).ToList() : null,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    private static JsonDocument ExtractTsv(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var lines = ReadAnalyzedLines(text.Content);

        var firstLine = lines.FirstOrDefault();
        string[] headers = firstLine?.Split('\t') ?? [];
        var columnCount = headers.Length;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "TSV",
            RowCount = lines.Count,
            ColumnCount = columnCount,
            Headers = headers.Take(20).Select(h => h.Trim()).ToList(),
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    private static List<string> ReadAnalyzedLines(string content)
    {
        var lines = new List<string>();
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
            lines.Add(line);

        return lines;
    }

    private static JsonDocument ExtractXml(Stream stream, long size)
    {
        var doc = SafeXmlDocumentLoader.Load(stream);

        var root = doc.DocumentElement;
        var elementCount = CountXmlElements(root);
        var attributeCount = CountXmlAttributes(root);

        // Get namespaces
        var namespaces = new List<string>();
        if (!string.IsNullOrEmpty(root?.NamespaceURI))
            namespaces.Add(root.NamespaceURI);

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "XML",
            RootElement = root?.Name,
            ElementCount = elementCount,
            AttributeCount = attributeCount,
            HasNamespaces = namespaces.Count > 0,
            Namespaces = namespaces.Count > 0 ? namespaces : null,
            Size = size
        });
    }

    private static int CountXmlElements(XmlNode? node)
    {
        if (node == null) return 0;
        var count = node.NodeType == XmlNodeType.Element ? 1 : 0;
        foreach (XmlNode child in node.ChildNodes)
            count += CountXmlElements(child);
        return count;
    }

    private static int CountXmlAttributes(XmlNode? node)
    {
        if (node == null) return 0;
        var count = node.Attributes?.Count ?? 0;
        foreach (XmlNode child in node.ChildNodes)
            count += CountXmlAttributes(child);
        return count;
    }

    #endregion

    #region Markup

    private static JsonDocument ExtractHtml(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;

        // Extract title
        var titleMatch = TitleRegex().Match(content);
        var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : null;

        // Extract meta description
        var descMatch = MetaDescriptionRegex().Match(content);
        var description = descMatch.Success ? descMatch.Groups[1].Value : null;

        // Count elements
        var linkCount = Regex.Matches(content, @"<a\s", RegexOptions.IgnoreCase).Count;
        var imageCount = Regex.Matches(content, @"<img\s", RegexOptions.IgnoreCase).Count;
        var scriptCount = Regex.Matches(content, @"<script", RegexOptions.IgnoreCase).Count;
        var styleCount = Regex.Matches(content, @"<style", RegexOptions.IgnoreCase).Count;
        var formCount = Regex.Matches(content, @"<form", RegexOptions.IgnoreCase).Count;

        // Detect doctype
        var hasHtml5Doctype = content.TrimStart().StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase);

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "HTML",
            Title = title,
            Description = description,
            IsHtml5 = hasHtml5Doctype,
            LinkCount = linkCount,
            ImageCount = imageCount,
            ScriptCount = scriptCount,
            StyleCount = styleCount,
            FormCount = formCount,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    [GeneratedRegex(@"<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<meta\s+name=[""']description[""']\s+content=[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetaDescriptionRegex();

    private static JsonDocument ExtractMarkdown(Stream stream, long size)
    {
        var text = BoundedMetadataTextReader.Read(stream);
        var content = text.Content;
        var lines = content.Split('\n');

        // Count headings by level
        var h1Count = lines.Count(l => l.TrimStart().StartsWith("# ") && !l.TrimStart().StartsWith("##"));
        var h2Count = lines.Count(l => l.TrimStart().StartsWith("## ") && !l.TrimStart().StartsWith("###"));
        var h3Count = lines.Count(l => l.TrimStart().StartsWith("### "));

        // Count other elements
        var linkCount = LinkRegex().Matches(content).Count;
        var imageCount = ImageRegex().Matches(content).Count;
        var codeBlockCount = CodeBlockRegex().Matches(content).Count;
        var inlineCodeCount = InlineCodeRegex().Matches(content).Count;
        var listItemCount = lines.Count(l => l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* ") || ListNumberRegex().IsMatch(l.TrimStart()));
        var blockquoteCount = lines.Count(l => l.TrimStart().StartsWith('>'));

        // Word count (excluding code)
        var textContent = CodeBlockRegex().Replace(content, "");
        var wordCount = textContent.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Markdown",
            LineCount = lines.Length,
            WordCount = wordCount,
            Headings = new { H1 = h1Count, H2 = h2Count, H3 = h3Count },
            LinkCount = linkCount,
            ImageCount = imageCount,
            CodeBlockCount = codeBlockCount,
            InlineCodeCount = inlineCodeCount,
            ListItemCount = listItemCount,
            BlockquoteCount = blockquoteCount,
            AnalysisTruncated = text.Truncated,
            Size = size
        });
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Compiled)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`[^`]+`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^\d+\.\s", RegexOptions.Compiled)]
    private static partial Regex ListNumberRegex();

    #endregion
}
