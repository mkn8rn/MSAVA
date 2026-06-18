using System.Collections.Frozen;
using System.IO.Compression;
using System.Text;
using System.Xml;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Utils;
using MSAVA_Shared.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using TagLib;

namespace MSAVA_BLL.Utils.Signature;

/// <summary>
/// Embeds MSAVA signatures into files on download.
/// </summary>
public static class SignatureEmbedder
{
    private const int CopyBufferSize = 81920;
    private const long MaximumBufferedStreamBytes = FileSizePolicy.MaximumFileSizeBytes;

    private static readonly Dictionary<string, Func<Stream, MsavaSignature, Stream>> Embedders =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Images - ImageSharp
        ["png"] = EmbedInImageSharp,
        ["jpg"] = EmbedInImageSharp,
        ["jpeg"] = EmbedInImageSharp,
        ["webp"] = EmbedInImageSharp,
        ["gif"] = EmbedInImageSharp,
        ["bmp"] = EmbedInImageSharp,
        ["tiff"] = EmbedInImageSharp,
        ["tga"] = EmbedInImageSharp,
        ["pbm"] = EmbedInImageSharp,
        ["pgm"] = EmbedInImageSharp,
        ["ppm"] = EmbedInImageSharp,
        ["ico"] = EmbedPassthrough, // ICO doesn't support metadata
        ["dib"] = EmbedInImageSharp,
        
        // Vector images
        ["svg"] = EmbedInSvg,
        ["svgz"] = EmbedInSvgz,
        
        // Audio - TagLib
        ["mp3"] = EmbedInTagLib,
        ["flac"] = EmbedInTagLib,
        ["m4a"] = EmbedInTagLib,
        ["ogg"] = EmbedInTagLib,
        ["wav"] = EmbedInTagLib,
        ["aac"] = EmbedInTagLib,
        ["wma"] = EmbedInTagLib,
        ["aiff"] = EmbedInTagLib,
        ["opus"] = EmbedInTagLib,
        ["ape"] = EmbedInTagLib,
        ["mpc"] = EmbedInTagLib,
        ["wv"] = EmbedInTagLib,
        ["dsf"] = EmbedInTagLib,
        ["au"] = EmbedInTagLib,
        
        // Video - TagLib
        ["mp4"] = EmbedInTagLib,
        ["mkv"] = EmbedInTagLib,
        ["avi"] = EmbedInTagLib,
        ["mov"] = EmbedInTagLib,
        ["webm"] = EmbedInTagLib,
        ["wmv"] = EmbedInTagLib,
        ["flv"] = EmbedInTagLib,
        ["mpg"] = EmbedInTagLib,
        ["mpeg"] = EmbedInTagLib,
        ["3gp"] = EmbedInTagLib,
        ["3g2"] = EmbedInTagLib,
        ["ogv"] = EmbedInTagLib,
        ["asf"] = EmbedInTagLib,
        ["m4v"] = EmbedInTagLib,
        ["f4v"] = EmbedInTagLib,
        
        // Documents - Office Open XML
        ["docx"] = EmbedInOfficeXml,
        ["xlsx"] = EmbedInOfficeXml,
        ["pptx"] = EmbedInOfficeXml,
        
        // Documents - OpenDocument
        ["odt"] = EmbedInOpenDocument,
        ["ods"] = EmbedInOpenDocument,
        ["odp"] = EmbedInOpenDocument,
        
        // PDF
        ["pdf"] = EmbedInPdf,
    };

    public static IReadOnlySet<string> SupportedExtensions { get; } =
        Embedders.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if signature embedding is supported for this extension.
    /// </summary>
    public static bool IsSupported(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Embeds an MSAVA signature into a file stream.
    /// Returns a new stream with the signature embedded.
    /// </summary>
    public static Stream Embed(Stream inputStream, string extension, MsavaSignature signature)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        
        if (!Embedders.TryGetValue(ext, out var embedder))
            throw new NotSupportedException($"Signature embedding not supported for .{ext}");

        if (inputStream.CanSeek)
            inputStream.Position = 0;

        EnsureRemainingInputWithinMaximum(inputStream);
        return embedder(inputStream, signature);
    }

    private static void EnsureRemainingInputWithinMaximum(Stream input)
    {
        if (!input.CanSeek)
            return;

        long remainingBytes = input.Length - input.Position;
        if (remainingBytes < 0)
            throw new InvalidDataException("Input stream position is beyond the declared stream length.");

        FileSizePolicy.EnsureWithinMaximum(remainingBytes, MaximumBufferedStreamBytes);
    }

    private static MemoryStream CopyInputToMemory(Stream input)
    {
        EnsureRemainingInputWithinMaximum(input);

        var output = new MemoryStream();
        CopyToBoundedOutput(input, output);
        output.Position = 0;
        return output;
    }

    private static void CopyToBoundedOutput(Stream source, Stream destination)
    {
        using var boundedOutput = new FileSizeLimitedWriteStream(destination, MaximumBufferedStreamBytes);
        source.CopyTo(boundedOutput, CopyBufferSize);
    }

    private static void SaveXmlToBoundedOutput(XmlDocument doc, MemoryStream output)
    {
        using var boundedOutput = new FileSizeLimitedWriteStream(output, MaximumBufferedStreamBytes);
        doc.Save(boundedOutput);
    }

    private static void EnsureBufferedOutputWithinMaximum(MemoryStream output)
    {
        FileSizePolicy.EnsureWithinMaximum(output.Length, MaximumBufferedStreamBytes);
    }

    private static void AppendToBoundedOutput(MemoryStream output, ReadOnlySpan<byte> bytes)
    {
        FileSizePolicy.EnsureChunkWithinMaximum(output.Length, bytes.Length, MaximumBufferedStreamBytes);
        output.Write(bytes);
    }

    #region Image Embedders

    private static Stream EmbedInImageSharp(Stream input, MsavaSignature signature)
    {
        using var image = Image.Load(input);
        
        // Use EXIF for all ImageSharp-supported formats
        var exif = image.Metadata.ExifProfile ?? new ExifProfile();
        exif.SetValue(ExifTag.ImageDescription, signature.ToString());
        image.Metadata.ExifProfile = exif;
        var imageFormat = image.Metadata.DecodedImageFormat
            ?? throw new InvalidDataException("Image format could not be decoded.");

        var output = new MemoryStream();
        using (var boundedOutput = new FileSizeLimitedWriteStream(output, MaximumBufferedStreamBytes))
        {
            image.Save(boundedOutput, imageFormat);
        }
        output.Position = 0;
        return output;
    }

    private static Stream EmbedPassthrough(Stream input, MsavaSignature signature)
    {
        // Format doesn't support metadata, return copy of original
        return CopyInputToMemory(input);
    }

    #endregion

    #region Vector Embedders

    private static Stream EmbedInSvg(Stream input, MsavaSignature signature)
    {
        var doc = SafeXmlDocumentLoader.Load(input, preserveWhitespace: true);

        // Add metadata element to SVG
        var svgNs = "http://www.w3.org/2000/svg";
        var root = doc.DocumentElement;
        
        if (root != null)
        {
            // Check if metadata element exists
            var metadataNode = root.SelectSingleNode("svg:metadata", CreateSvgNamespaceManager(doc)) 
                              ?? root.SelectSingleNode("metadata");
            
            if (metadataNode == null)
            {
                metadataNode = doc.CreateElement("metadata", svgNs);
                root.PrependChild(metadataNode);
            }

            // Add MSAVA signature as a comment inside metadata
            var signatureComment = doc.CreateComment($" {signature} ");
            metadataNode.AppendChild(signatureComment);
        }

        var output = new MemoryStream();
        SaveXmlToBoundedOutput(doc, output);
        output.Position = 0;
        return output;
    }

    private static XmlNamespaceManager CreateSvgNamespaceManager(XmlDocument doc)
    {
        var nsMgr = new XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
        return nsMgr;
    }

    private static Stream EmbedInSvgz(Stream input, MsavaSignature signature)
    {
        // Decompress, embed, recompress
        using var gzipIn = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var embedded = EmbedInSvg(gzipIn, signature);
        
        var output = new MemoryStream();
        using (var boundedOutput = new FileSizeLimitedWriteStream(output, MaximumBufferedStreamBytes))
        using (var gzipOut = new GZipStream(boundedOutput, CompressionLevel.Optimal, leaveOpen: true))
        {
            embedded.CopyTo(gzipOut, CopyBufferSize);
        }
        output.Position = 0;
        return output;
    }

    #endregion

    #region Audio/Video Embedders

    private static Stream EmbedInTagLib(Stream input, MsavaSignature signature)
    {
        var memoryStream = CopyInputToMemory(input);

        using var tagFile = TagLib.File.Create(new StreamFileAbstraction("media", memoryStream));
        tagFile.Tag.Comment = signature.ToString();
        tagFile.Save();
        EnsureBufferedOutputWithinMaximum(memoryStream);

        memoryStream.Position = 0;
        return memoryStream;
    }

    #endregion

    #region Document Embedders

    private static Stream EmbedInOfficeXml(Stream input, MsavaSignature signature)
    {
        var output = CopyInputToMemory(input);

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Add or update custom.xml with our signature
            var customPropsPath = "docProps/custom.xml";
            var entry = archive.GetEntry(customPropsPath);
            
            var customXml = CreateCustomPropertiesXml(signature, entry);
            
            // Remove existing and add new
            entry?.Delete();
            var newEntry = archive.CreateEntry(customPropsPath);
            using var entryStream = newEntry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(customXml);
        }

        EnsureBufferedOutputWithinMaximum(output);
        output.Position = 0;
        return output;
    }

    private static string CreateCustomPropertiesXml(MsavaSignature signature, ZipArchiveEntry? existingEntry)
    {
        var ns = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        var vtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        if (existingEntry != null)
        {
            // Parse existing and add our property
            try
            {
                using var stream = existingEntry.Open();
                var doc = SafeXmlDocumentLoader.Load(stream);
                
                var root = doc.DocumentElement;
                if (root != null)
                {
                    var prop = doc.CreateElement("property", ns);
                    prop.SetAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");
                    prop.SetAttribute("pid", (root.ChildNodes.Count + 2).ToString());
                    prop.SetAttribute("name", MsavaSignature.MetadataKey);
                    
                    var lpwstr = doc.CreateElement("vt:lpwstr", vtNs);
                    lpwstr.InnerText = signature.ToString();
                    prop.AppendChild(lpwstr);
                    
                    root.AppendChild(prop);
                    
                    using var sw = new StringWriter();
                    doc.Save(sw);
                    return sw.ToString();
                }
            }
            catch (Exception ex) when (IsExistingCustomPropertiesReadFailure(ex))
            {
                return CreateNewCustomPropertiesXml(signature, ns, vtNs);
            }
        }

        // Create new custom properties
        return CreateNewCustomPropertiesXml(signature, ns, vtNs);
    }

    private static bool IsExistingCustomPropertiesReadFailure(Exception exception)
    {
        if (CriticalExceptionPolicy.ContainsCriticalException(exception))
            return false;

        return exception is XmlException or IOException or InvalidDataException;
    }

    private static string CreateNewCustomPropertiesXml(MsavaSignature signature, string ns, string vtNs)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Properties xmlns=""{ns}"" xmlns:vt=""{vtNs}"">
  <property fmtid=""{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}"" pid=""2"" name=""{MsavaSignature.MetadataKey}"">
    <vt:lpwstr>{signature}</vt:lpwstr>
  </property>
</Properties>";
    }

    private static Stream EmbedInOpenDocument(Stream input, MsavaSignature signature)
    {
        var output = CopyInputToMemory(input);

        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var metaPath = "meta.xml";
            var entry = archive.GetEntry(metaPath);
            
            if (entry != null)
            {
                string updatedXml;
                using (var stream = entry.Open())
                {
                    var doc = SafeXmlDocumentLoader.Load(stream);
                    
                    var nsMgr = new XmlNamespaceManager(doc.NameTable);
                    nsMgr.AddNamespace("office", "urn:oasis:names:tc:opendocument:xmlns:office:1.0");
                    nsMgr.AddNamespace("meta", "urn:oasis:names:tc:opendocument:xmlns:meta:1.0");
                    
                    var metaNode = doc.SelectSingleNode("//office:meta", nsMgr);
                    if (metaNode != null)
                    {
                        // Add user-defined metadata
                        var userDefined = doc.CreateElement("meta", "user-defined", "urn:oasis:names:tc:opendocument:xmlns:meta:1.0");
                        var nameAttr = doc.CreateAttribute("meta", "name", "urn:oasis:names:tc:opendocument:xmlns:meta:1.0");
                        nameAttr.Value = MsavaSignature.MetadataKey;
                        userDefined.Attributes.Append(nameAttr);
                        userDefined.InnerText = signature.ToString();
                        metaNode.AppendChild(userDefined);
                    }
                    
                    using var sw = new StringWriter();
                    doc.Save(sw);
                    updatedXml = sw.ToString();
                }
                
                entry.Delete();
                var newEntry = archive.CreateEntry(metaPath);
                using var entryStream = newEntry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                writer.Write(updatedXml);
            }
        }

        EnsureBufferedOutputWithinMaximum(output);
        output.Position = 0;
        return output;
    }

    private static Stream EmbedInPdf(Stream input, MsavaSignature signature)
    {
        // PDF metadata embedding is complex without a library
        // We'll add a comment at the end of the PDF (after %%EOF)
        // This is a simple approach that preserves the PDF
        
        var output = CopyInputToMemory(input);
        
        // Append signature as PDF comment
        var signatureBytes = Encoding.ASCII.GetBytes($"\n% {signature}\n");
        AppendToBoundedOutput(output, signatureBytes);
        
        output.Position = 0;
        return output;
    }

    #endregion

    /// <summary>
    /// Stream abstraction for TagLib.
    /// </summary>
    private sealed class StreamFileAbstraction : TagLib.File.IFileAbstraction
    {
        public string Name { get; }
        public Stream ReadStream { get; }
        public Stream WriteStream { get; }

        public StreamFileAbstraction(string name, Stream stream)
        {
            Name = name;
            ReadStream = stream;
            WriteStream = stream;
        }

        public void CloseStream(Stream stream) { /* Don't close - caller owns the stream */ }
    }
}
