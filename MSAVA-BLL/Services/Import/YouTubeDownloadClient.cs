using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MSAVA_BLL.Services.Import;

internal interface IYouTubeDownloadClient
{
    Task<YouTubeDownloadManifest> GetDownloadManifestAsync(string youtubeUrl, CancellationToken cancellationToken);

    Task CopyToAsync(YouTubeStreamInfo streamInfo, Stream destination, CancellationToken cancellationToken);
}

internal sealed record YouTubeDownloadManifest(
    string Title,
    IReadOnlyList<YouTubeStreamInfo> MuxedStreams,
    IReadOnlyList<YouTubeStreamInfo> VideoStreams,
    IReadOnlyList<YouTubeStreamInfo> AudioStreams);

internal sealed record YouTubeStreamInfo(
    object Source,
    string ContainerName,
    string? VideoQualityLabel,
    int VideoMaxHeight,
    double BitrateKiloBitsPerSecond);

internal sealed class YoutubeExplodeDownloadClient : IYouTubeDownloadClient
{
    private readonly YoutubeClient _youtube = new();

    public async Task<YouTubeDownloadManifest> GetDownloadManifestAsync(
        string youtubeUrl,
        CancellationToken cancellationToken)
    {
        var videoId = VideoId.Parse(youtubeUrl);
        var video = await _youtube.Videos.GetAsync(videoId, cancellationToken);
        var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);

        return new YouTubeDownloadManifest(
            video.Title,
            streamManifest.GetMuxedStreams().Select(MapVideoStream).ToList(),
            streamManifest.GetVideoOnlyStreams().Select(MapVideoStream).ToList(),
            streamManifest.GetAudioOnlyStreams().Select(MapAudioStream).ToList());
    }

    public async Task CopyToAsync(
        YouTubeStreamInfo streamInfo,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (streamInfo.Source is not IStreamInfo source)
            throw new InvalidOperationException("YouTube stream source is not supported by YoutubeExplode.");

        await _youtube.Videos.Streams.CopyToAsync(source, destination, null, cancellationToken);
    }

    private static YouTubeStreamInfo MapVideoStream(IVideoStreamInfo stream)
    {
        return new YouTubeStreamInfo(
            stream,
            stream.Container.Name,
            stream.VideoQuality.Label,
            stream.VideoQuality.MaxHeight,
            stream.Bitrate.KiloBitsPerSecond);
    }

    private static YouTubeStreamInfo MapAudioStream(IAudioStreamInfo stream)
    {
        return new YouTubeStreamInfo(
            stream,
            stream.Container.Name,
            null,
            0,
            stream.Bitrate.KiloBitsPerSecond);
    }
}
