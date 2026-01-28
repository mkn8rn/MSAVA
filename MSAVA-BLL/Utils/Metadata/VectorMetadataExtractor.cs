using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Vector image metadata extraction.
/// Supports: SVG, SVGZ, EPS, AI, DXF, SWF
/// </summary>
public static partial class VectorMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // Supported vector formats only
        map["svg"] = (ExtractSvg, "image/svg+xml");
        map["svgz"] = (ExtractSvgz, "image/svg+xml");
        map["eps"] = (ExtractEps, "application/postscript");
        map["ai"] = (ExtractEps, "application/postscript"); // AI files are EPS-based
        map["dxf"] = (ExtractDxf, "image/vnd.dxf");
        map["swf"] = (ExtractSwf, "application/x-shockwave-flash");
        
        // Note: WMF, EMF, CDR, CGM, DWG, SKETCH, FIG, DRW, VSD, FLA, SAI, HPGL, PLT
        // are NOT registered - they will fall through to InvalidMetadata
    }

    private static JsonDocument ExtractSvg(Stream stream, long size)
    {
        var doc = new XmlDocument();
        doc.Load(stream);

        var svgNode = doc.DocumentElement;
        var width = svgNode?.GetAttribute("width");
        var height = svgNode?.GetAttribute("height");
        var viewBox = svgNode?.GetAttribute("viewBox");

        // Parse viewBox for dimensions if width/height not specified
        int? parsedWidth = null, parsedHeight = null;
        if (!string.IsNullOrEmpty(viewBox))
        {
            var parts = viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                if (float.TryParse(parts[2], out var w)) parsedWidth = (int)w;
                if (float.TryParse(parts[3], out var h)) parsedHeight = (int)h;
            }
        }

        var elementCount = CountElements(svgNode);

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "VectorImage",
            Valid = true,
            Format = "SVG",
            Width = InvalidMetadata.OrInvalid(width ?? parsedWidth?.ToString()),
            Height = InvalidMetadata.OrInvalid(height ?? parsedHeight?.ToString()),
            ViewBox = InvalidMetadata.OrInvalid(viewBox),
            ElementCount = elementCount,
            Size = size
        });
    }

    private static int CountElements(XmlNode? node)
    {
        if (node == null) return 0;
        var count = node.ChildNodes.Count;
        foreach (XmlNode child in node.ChildNodes)
            count += CountElements(child);
        return count;
    }

    private static JsonDocument ExtractSvgz(Stream stream, long size)
    {
        using var gzipStream = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var memoryStream = new MemoryStream();
        gzipStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        return ExtractSvg(memoryStream, size);
    }

    private static JsonDocument ExtractEps(Stream stream, long size)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var headerLines = new List<string>();
        string? line;
        var lineCount = 0;
        
        while ((line = reader.ReadLine()) != null && lineCount < 100)
        {
            headerLines.Add(line);
            if (line.StartsWith("%%EndComments"))
                break;
            lineCount++;
        }

        var header = string.Join("\n", headerLines);

        var title = ExtractDscComment(header, "Title");
        var creator = ExtractDscComment(header, "Creator");
        var creationDate = ExtractDscComment(header, "CreationDate");
        var boundingBox = ExtractDscComment(header, "BoundingBox");
        var languageLevel = ExtractDscComment(header, "LanguageLevel");

        int? width = null, height = null;
        if (boundingBox != null)
        {
            var parts = boundingBox.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 &&
                int.TryParse(parts[0], out var x1) &&
                int.TryParse(parts[1], out var y1) &&
                int.TryParse(parts[2], out var x2) &&
                int.TryParse(parts[3], out var y2))
            {
                width = x2 - x1;
                height = y2 - y1;
            }
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "VectorImage",
            Valid = true,
            Format = "EPS",
            Title = InvalidMetadata.OrInvalid(title),
            Creator = InvalidMetadata.OrInvalid(creator),
            CreationDate = InvalidMetadata.OrInvalid(creationDate),
            Width = InvalidMetadata.OrInvalid(width),
            Height = InvalidMetadata.OrInvalid(height),
            LanguageLevel = InvalidMetadata.OrInvalid(languageLevel),
            Size = size
        });
    }

    private static string? ExtractDscComment(string header, string field)
    {
        var pattern = $@"%%{field}:\s*(.+)";
        var match = Regex.Match(header, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var value = match.Groups[1].Value.Trim();
            if (value.StartsWith('(') && value.EndsWith(')'))
                value = value[1..^1];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    private static JsonDocument ExtractDxf(Stream stream, long size)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var headerValues = new Dictionary<string, string>();
        string? line;
        var inHeader = false;
        string? currentVar = null;
        var lineCount = 0;

        while ((line = reader.ReadLine()) != null && lineCount < 500)
        {
            line = line.Trim();
            lineCount++;

            if (line == "HEADER") inHeader = true;
            if (line == "ENDSEC" && inHeader) break;

            if (inHeader && line.StartsWith('$'))
            {
                currentVar = line;
            }
            else if (currentVar != null && !string.IsNullOrEmpty(line))
            {
                var valueLine = reader.ReadLine()?.Trim();
                if (valueLine != null)
                {
                    headerValues[currentVar] = valueLine;
                    lineCount++;
                }
                currentVar = null;
            }
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "CADDrawing",
            Valid = true,
            Format = "DXF",
            AcadVersion = InvalidMetadata.OrInvalid(headerValues.GetValueOrDefault("$ACADVER")),
            DrawingUnits = InvalidMetadata.OrInvalid(headerValues.GetValueOrDefault("$INSUNITS")),
            LastSavedBy = InvalidMetadata.OrInvalid(headerValues.GetValueOrDefault("$LASTSAVEDBY")),
            Size = size
        });
    }

    private static JsonDocument ExtractSwf(Stream stream, long size)
    {
        Span<byte> header = stackalloc byte[8];
        var bytesRead = stream.Read(header);

        if (bytesRead < 8)
            return MetadataExtractor.CreateInvalidMetadata(size, "Invalid SWF header");

        var signature = Encoding.ASCII.GetString(header[..3]);
        var isCompressed = signature is "CWS" or "ZWS";
        var version = header[3];
        var fileLength = BitConverter.ToInt32(header[4..8]);

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Animation",
            Valid = true,
            Format = "SWF",
            Version = (int)version,
            Compressed = isCompressed,
            FileLength = fileLength,
            Size = size
        });
    }
}
