using System.Xml;

namespace MSAVA_BLL.Utils;

internal static class SafeXmlDocumentLoader
{
    internal const long MaximumXmlCharacters = 16L * 1024L * 1024L;

    internal static XmlDocument Load(Stream stream, bool preserveWhitespace = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters
        };

        using var reader = XmlReader.Create(stream, settings);
        var document = new XmlDocument
        {
            PreserveWhitespace = preserveWhitespace,
            XmlResolver = null
        };

        document.Load(reader);
        return document;
    }
}
