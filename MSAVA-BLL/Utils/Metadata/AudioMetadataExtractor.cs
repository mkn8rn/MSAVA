using System.Text.Json;
using TagLib;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Audio metadata extraction using TagLibSharp.
/// Supports: MP3, FLAC, WAV, OGG, M4A, AAC, WMA, AIFF, OPUS, APE, MPC, WV, DSF, AU
/// </summary>
public static class AudioMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // TagLib supported formats only
        map["mp3"] = (Extract, "audio/mpeg");
        map["flac"] = (Extract, "audio/flac");
        map["wav"] = (Extract, "audio/wav");
        map["ogg"] = (Extract, "audio/ogg");
        map["m4a"] = (Extract, "audio/mp4");
        map["aac"] = (Extract, "audio/aac");
        map["wma"] = (Extract, "audio/x-ms-wma");
        map["aiff"] = (Extract, "audio/aiff");
        map["opus"] = (Extract, "audio/opus");
        map["ape"] = (Extract, "audio/ape");
        map["mpc"] = (Extract, "audio/mpc");
        map["wv"] = (Extract, "audio/wavpack");
        map["dsf"] = (Extract, "audio/dsf");
        map["au"] = (Extract, "audio/basic");
        
        // Note: AMR, TTA, RA, GSM, VOX are NOT registered
        // They will fall through to InvalidMetadata
    }

    private static JsonDocument Extract(Stream stream, long size)
    {
        using var tagFile = TagLib.File.Create(new StreamFileAbstraction("audio", stream));

        var props = tagFile.Properties;
        var tag = tagFile.Tag;

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Audio",
            Valid = true,
            DurationSeconds = InvalidMetadata.OrInvalid(props.Duration.TotalSeconds),
            DurationFormatted = FormatDuration(props.Duration),
            Bitrate = InvalidMetadata.OrInvalid(props.AudioBitrate),
            SampleRate = InvalidMetadata.OrInvalid(props.AudioSampleRate),
            Channels = InvalidMetadata.OrInvalid(props.AudioChannels),
            BitsPerSample = InvalidMetadata.OrInvalid(props.BitsPerSample),
            Codec = InvalidMetadata.OrInvalid(props.Description),
            
            // Metadata tags
            Title = InvalidMetadata.OrInvalid(tag.Title),
            Artist = InvalidMetadata.OrInvalid(tag.FirstPerformer),
            AlbumArtist = InvalidMetadata.OrInvalid(tag.FirstAlbumArtist),
            Album = InvalidMetadata.OrInvalid(tag.Album),
            Year = InvalidMetadata.OrInvalid(tag.Year > 0 ? (int?)tag.Year : null),
            Genre = InvalidMetadata.OrInvalid(tag.FirstGenre),
            TrackNumber = InvalidMetadata.OrInvalid(tag.Track > 0 ? (int?)tag.Track : null),
            TrackCount = InvalidMetadata.OrInvalid(tag.TrackCount > 0 ? (int?)tag.TrackCount : null),
            DiscNumber = InvalidMetadata.OrInvalid(tag.Disc > 0 ? (int?)tag.Disc : null),
            DiscCount = InvalidMetadata.OrInvalid(tag.DiscCount > 0 ? (int?)tag.DiscCount : null),
            Comment = InvalidMetadata.OrInvalid(tag.Comment),
            Composer = InvalidMetadata.OrInvalid(tag.FirstComposer),
            HasAlbumArt = tag.Pictures?.Length > 0,
            
            Size = size
        });
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0)
            return InvalidMetadata.String;
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    /// <summary>
    /// Stream abstraction for TagLib to read from a Stream.
    /// </summary>
    internal sealed class StreamFileAbstraction : TagLib.File.IFileAbstraction
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
