using System.Text.Json;
using TagLib;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Video metadata extraction using TagLibSharp.
/// Supports: MP4, MKV, AVI, MOV, WebM, WMV, FLV, MPG, MPEG, 3GP, 3G2, OGV, ASF, M4V, F4V, TS, MTS, M2TS
/// </summary>
public static class VideoMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // TagLib supported video formats
        map["mp4"] = (Extract, "video/mp4");
        map["mkv"] = (Extract, "video/x-matroska");
        map["avi"] = (Extract, "video/x-msvideo");
        map["mov"] = (Extract, "video/quicktime");
        map["webm"] = (Extract, "video/webm");
        map["wmv"] = (Extract, "video/x-ms-wmv");
        map["flv"] = (Extract, "video/x-flv");
        map["mpg"] = (Extract, "video/mpeg");
        map["mpeg"] = (Extract, "video/mpeg");
        map["3gp"] = (Extract, "video/3gpp");
        map["3g2"] = (Extract, "video/3gpp2");
        map["ogv"] = (Extract, "video/ogg");
        map["asf"] = (Extract, "video/x-ms-asf");
        map["m4v"] = (Extract, "video/x-m4v");
        map["f4v"] = (Extract, "video/x-f4v");

        // Transport streams - basic detection
        map["ts"] = (ExtractTransportStream, "video/mp2t");
        map["mts"] = (ExtractTransportStream, "video/mp2t");
        map["m2ts"] = (ExtractTransportStream, "video/mp2t");
        
        // Note: RM, VOB are NOT registered - they will fall through to InvalidMetadata
    }

    private static JsonDocument Extract(Stream stream, long size)
    {
        using var tagFile = TagLib.File.Create(new AudioMetadataExtractor.StreamFileAbstraction("video", stream));

        var props = tagFile.Properties;
        var tag = tagFile.Tag;

        // Calculate aspect ratio
        string aspectRatio = InvalidMetadata.String;
        if (props.VideoWidth > 0 && props.VideoHeight > 0)
        {
            var gcd = Gcd(props.VideoWidth, props.VideoHeight);
            aspectRatio = $"{props.VideoWidth / gcd}:{props.VideoHeight / gcd}";
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Video",
            Valid = true,
            DurationSeconds = InvalidMetadata.OrInvalid(props.Duration.TotalSeconds),
            DurationFormatted = FormatDuration(props.Duration),
            
            // Video properties
            Width = InvalidMetadata.OrInvalid(props.VideoWidth),
            Height = InvalidMetadata.OrInvalid(props.VideoHeight),
            AspectRatio = aspectRatio,
            
            // Audio properties
            AudioBitrate = InvalidMetadata.OrInvalid(props.AudioBitrate),
            AudioSampleRate = InvalidMetadata.OrInvalid(props.AudioSampleRate),
            AudioChannels = InvalidMetadata.OrInvalid(props.AudioChannels),
            
            Codec = InvalidMetadata.OrInvalid(props.Description),
            
            // Metadata
            Title = InvalidMetadata.OrInvalid(tag.Title),
            Year = InvalidMetadata.OrInvalid(tag.Year > 0 ? (int?)tag.Year : null),
            Comment = InvalidMetadata.OrInvalid(tag.Comment),
            HasThumbnail = tag.Pictures?.Length > 0,
            
            Size = size
        });
    }

    private static JsonDocument ExtractTransportStream(Stream stream, long size)
    {
        Span<byte> header = stackalloc byte[188 * 3];
        var bytesRead = stream.Read(header);

        var isValidTs = false;
        var packetCount = 0;

        for (int i = 0; i < bytesRead - 188; i += 188)
        {
            if (header[i] == 0x47) // TS sync byte
            {
                isValidTs = true;
                packetCount++;
            }
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Video",
            Valid = isValidTs,
            Format = "MPEG Transport Stream",
            ValidTransportStream = isValidTs,
            Size = size,
            Note = isValidTs ? InvalidMetadata.String : "Invalid transport stream"
        });
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }
        return a;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0)
            return InvalidMetadata.String;
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}
