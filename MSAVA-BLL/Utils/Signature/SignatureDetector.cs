using System.IO.Compression;
using System.Text;
using System.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using TagLib;

namespace MSAVA_BLL.Utils.Signature;

/// <summary>
/// Detects MSAVA signatures in uploaded files.
/// </summary>
public static class SignatureDetector
{
    private static readonly Dictionary<string, Func<Stream, MsavaSignature?>> Detectors =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Images - ImageSharp
        ["png"] = DetectInImageSharp,
        ["jpg"] = DetectInImageSharp,
        ["jpeg"] = DetectInImageSharp,
        ["webp"] = DetectInImageSharp,
        ["gif"] = DetectInImageSharp,
        ["bmp"] = DetectInImageSharp,
        ["tiff"] = DetectInImageSharp,
        ["tga"] = DetectInImageSharp,
        ["pbm"] = DetectInImageSharp,
        ["pgm"] = DetectInImageSharp,
        ["ppm"] = DetectInImageSharp,
        ["dib"] = DetectInImageSharp,
        
        // Vector images
        ["svg"] = DetectInSvg,
        ["svgz"] = DetectInSvgz,
        
        // Audio - TagLib
        ["mp3"] = DetectInTagLib,
        ["flac"] = DetectInTagLib,
        ["m4a"] = DetectInTagLib,
        ["ogg"] = DetectInTagLib,
        ["wav"] = DetectInTagLib,
        ["aac"] = DetectInTagLib,
        ["wma"] = DetectInTagLib,
        ["aiff"] = DetectInTagLib,
        ["opus"] = DetectInTagLib,
        ["ape"] = DetectInTagLib,
        ["mpc"] = DetectInTagLib,
        ["wv"] = DetectInTagLib,
        ["dsf"] = DetectInTagLib,
        ["au"] = DetectInTagLib,
        
        // Video - TagLib
        ["mp4"] = DetectInTagLib,
        ["mkv"] = DetectInTagLib,
        ["avi"] = DetectInTagLib,
        ["mov"] = DetectInTagLib,
        ["webm"] = DetectInTagLib,
        ["wmv"] = DetectInTagLib,
        ["flv"] = DetectInTagLib,
        ["mpg"] = DetectInTagLib,
        ["mpeg"] = DetectInTagLib,
        ["3gp"] = DetectInTagLib,
        ["3g2"] = DetectInTagLib,
        ["ogv"] = DetectInTagLib,
        ["asf"] = DetectInTagLib,
        ["m4v"] = DetectInTagLib,
        ["f4v"] = DetectInTagLib,
        
        // Documents - Office Open XML
        ["docx"] = DetectInOfficeXml,
        ["xlsx"] = DetectInOfficeXml,
        ["pptx"] = DetectInOfficeXml,
        
        // Documents - OpenDocument
        ["odt"] = DetectInOpenDocument,
        ["ods"] = DetectInOpenDocument,
        ["odp"] = DetectInOpenDocument,
        
        // PDF
        ["pdf"] = DetectInPdf,
    };

    /// <summary>
    /// Checks if signature detection is supported for this extension.
    /// </summary>
    public static bool IsSupported(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Detectors.ContainsKey(ext);
    }

    /// <summary>
    /// Tries to detect an MSAVA signature in a file stream.
    /// Returns null if no signature found or detection not supported.
    /// </summary>
    public static MsavaSignature? Detect(Stream inputStream, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        
        if (!Detectors.TryGetValue(ext, out var detector))
            return null;

        if (inputStream.CanSeek)
            inputStream.Position = 0;

        try
        {
            var signature = detector(inputStream);
            
            // Reset stream for further processing
            if (inputStream.CanSeek)
                inputStream.Position = 0;
                
            return signature;
        }
        catch
        {
            // Reset stream on failure
            if (inputStream.CanSeek)
                inputStream.Position = 0;
            return null;
        }
    }

    /// <summary>
    /// Result of signature detection with additional context.
    /// </summary>
    public record DetectionResult(
        bool Found,
        MsavaSignature? Signature,
        string? ContentHash,
        long? FileId)
    {
        public static DetectionResult NotFound => new(false, null, null, null);
        
        public static DetectionResult FromSignature(MsavaSignature sig) =>
            new(true, sig, sig.ContentHash, sig.FileId);
    }

    /// <summary>
    /// Detects signature and returns structured result.
    /// </summary>
    public static DetectionResult TryDetect(Stream inputStream, string extension)
    {
        var signature = Detect(inputStream, extension);
        return signature != null 
            ? DetectionResult.FromSignature(signature) 
            : DetectionResult.NotFound;
    }

    #region Image Detectors

    private static MsavaSignature? DetectInImageSharp(Stream input)
    {
        using var image = Image.Load(input);
        return DetectInExif(image.Metadata.ExifProfile);
    }

    private static MsavaSignature? DetectInExif(ExifProfile? exif)
    {
        if (exif == null)
            return null;

        // Check ImageDescription (primary location)
        if (exif.TryGetValue(ExifTag.ImageDescription, out var desc))
        {
            if (MsavaSignature.TryParse(desc.Value, out var sig))
                return sig;
        }

        // Check UserComment as fallback
        if (exif.TryGetValue(ExifTag.UserComment, out var userComment))
        {
            if (MsavaSignature.TryParse(userComment.Value.Text, out var sig))
                return sig;
        }

        return null;
    }

    #endregion

    #region Vector Detectors

    private static MsavaSignature? DetectInSvg(Stream input)
    {
        var doc = new XmlDocument();
        doc.Load(input);

        // Look for signature in metadata comments
        var comments = FindXmlComments(doc.DocumentElement);
        foreach (var comment in comments)
        {
            if (MsavaSignature.TryParse(comment, out var sig))
                return sig;
        }

        return null;
    }

    private static List<string> FindXmlComments(XmlNode? node)
    {
        var comments = new List<string>();
        if (node == null) return comments;

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlComment comment)
                comments.Add(comment.Value ?? "");
            else
                comments.AddRange(FindXmlComments(child));
        }

        return comments;
    }

    private static MsavaSignature? DetectInSvgz(Stream input)
    {
        using var gzipIn = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var decompressed = new MemoryStream();
        gzipIn.CopyTo(decompressed);
        decompressed.Position = 0;

        return DetectInSvg(decompressed);
    }

    #endregion

    #region Audio/Video Detectors

    private static MsavaSignature? DetectInTagLib(Stream input)
    {
        using var tagFile = TagLib.File.Create(new StreamFileAbstraction("media", input));

        if (!string.IsNullOrEmpty(tagFile.Tag.Comment))
        {
            if (MsavaSignature.TryParse(tagFile.Tag.Comment, out var sig))
                return sig;
        }

        return null;
    }

    #endregion

    #region Document Detectors

    private static MsavaSignature? DetectInOfficeXml(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("docProps/custom.xml");

        if (entry != null)
        {
            using var stream = entry.Open();
            var doc = new XmlDocument();
            doc.Load(stream);

            // Look for our property
            var nodes = doc.GetElementsByTagName("property");
            foreach (XmlNode node in nodes)
            {
                var nameAttr = node.Attributes?["name"];
                if (nameAttr?.Value == MsavaSignature.MetadataKey)
                {
                    var value = node.InnerText;
                    if (MsavaSignature.TryParse(value, out var sig))
                        return sig;
                }
            }
        }

        return null;
    }

    private static MsavaSignature? DetectInOpenDocument(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("meta.xml");

        if (entry != null)
        {
            using var stream = entry.Open();
            var doc = new XmlDocument();
            doc.Load(stream);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("meta", "urn:oasis:names:tc:opendocument:xmlns:meta:1.0");

            // Look for user-defined metadata with our key
            var nodes = doc.SelectNodes("//meta:user-defined", nsMgr);
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    var nameAttr = node.Attributes?["meta:name"];
                    if (nameAttr?.Value == MsavaSignature.MetadataKey)
                    {
                        if (MsavaSignature.TryParse(node.InnerText, out var sig))
                            return sig;
                    }
                }
            }
        }

        return null;
    }

    private static MsavaSignature? DetectInPdf(Stream input)
    {
        // Read the last few KB to find our appended signature
        if (!input.CanSeek)
        {
            var ms = new MemoryStream();
            input.CopyTo(ms);
            ms.Position = 0;
            input = ms;
        }

        var tailSize = Math.Min(4096, input.Length);
        var buffer = new byte[tailSize];
        
        input.Position = input.Length - tailSize;
        var bytesRead = input.Read(buffer, 0, buffer.Length);
        
        var tail = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        
        if (MsavaSignature.TryParse(tail, out var sig))
            return sig;

        return null;
    }

    #endregion

    /// <summary>
    /// Stream abstraction for TagLib (read-only).
    /// </summary>
    private sealed class StreamFileAbstraction : TagLib.File.IFileAbstraction
    {
        public string Name { get; }
        public Stream ReadStream { get; }
        public Stream WriteStream => ReadStream; // Not used for reading

        public StreamFileAbstraction(string name, Stream stream)
        {
            Name = name;
            ReadStream = stream;
        }

        public void CloseStream(Stream stream) { /* Don't close - caller owns the stream */ }
    }
}
