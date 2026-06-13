using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_Shared.Models;
using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MSAVA_BLL.Services.Import;

public class YouTubeImportService
{
    private readonly FilePersistenceService _persistenceService;
    private readonly ServiceLogger _serviceLogger;

    public YouTubeImportService(FilePersistenceService persistenceService, ServiceLogger serviceLogger)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
    }

    public async Task<Guid> ImportAsync(FetchFileYouTubeDTO dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.YouTubeUrl))
            throw new ArgumentException("YouTubeUrl must be provided.", nameof(dto));
        if (dto.AccessGroupId == Guid.Empty)
            throw new ArgumentException("AccessGroupId must be provided.", nameof(dto));
        if (!dto.DownloadVideo && !dto.DownloadAudio)
            throw new ArgumentException("At least one YouTube stream type must be selected.", nameof(dto));

        var youtube = new YoutubeClient();
        var videoId = VideoId.Parse(dto.YouTubeUrl);
        var video = await youtube.Videos.GetAsync(videoId, cancellationToken);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);

        var muxedStreams = streamManifest.GetMuxedStreams().ToList();
        var videoOnlyStreams = streamManifest.GetVideoOnlyStreams().ToList();
        var audioOnlyStreams = streamManifest.GetAudioOnlyStreams().ToList();

        string fileName = string.IsNullOrWhiteSpace(video.Title) ? "YouTube Video" : video.Title;
        string fileExtension = "mp4";
        string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if (dto.DownloadVideo && dto.DownloadAudio)
        {
            MuxedStreamInfo? streamInfo = null;
            if (!string.IsNullOrWhiteSpace(dto.VideoQuality))
            {
                streamInfo = muxedStreams
                    .Where(s => s.VideoQuality.Label.Equals(dto.VideoQuality, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault();
            }

            streamInfo ??= muxedStreams
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (streamInfo != null)
            {
                fileExtension = streamInfo.Container.Name;
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await youtube.Videos.Streams.CopyToAsync(streamInfo, fileStream, null, cancellationToken);
            }
            else
            {
                await MuxVideoAndAudio(youtube, videoOnlyStreams, audioOnlyStreams, dto, tempFilePath, cancellationToken);
            }
        }
        else if (dto.DownloadVideo)
        {
            var videoStream = GetBestVideoStream(videoOnlyStreams, dto.VideoQuality);
            fileExtension = videoStream.Container.Name;
            await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await youtube.Videos.Streams.CopyToAsync(videoStream, fileStream, null, cancellationToken);
        }
        else if (dto.DownloadAudio)
        {
            var audioStream = GetBestAudioStream(audioOnlyStreams, dto.AudioQuality);
            fileExtension = audioStream.Container.Name;
            await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await youtube.Videos.Streams.CopyToAsync(audioStream, fileStream, null, cancellationToken);
        }
        var fetchDto = new SaveFileFromFetchDTO
        {
            FileName = fileName,
            FileExtension = fileExtension,
            TempFilePath = tempFilePath,
            AccessGroupId = dto.AccessGroupId,
            Tags = dto.Tags ?? [],
            Categories = dto.Categories ?? [],
            Description = dto.Description ?? string.Empty,
            PublicViewing = dto.PublicViewing,
            PublicDownload = dto.PublicDownload
        };

        return await _persistenceService.CreateFileFromTempFileAsync(fetchDto, cancellationToken);
    }

    private async Task MuxVideoAndAudio(
        YoutubeClient youtube,
        IReadOnlyList<IVideoStreamInfo> videoStreams,
        IReadOnlyList<IAudioStreamInfo> audioStreams,
        FetchFileYouTubeDTO dto,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var videoStream = GetBestVideoStream(videoStreams, dto.VideoQuality);
        var audioStream = GetBestAudioStream(audioStreams, dto.AudioQuality);

        string videoFormat = videoStream.Container.Name;
        string audioFormat = audioStream.Container.Name;

        string videoTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            _serviceLogger.LogInformation($"Downloading video to {videoTemp}");
            await using (var vfs = new FileStream(videoTemp, FileMode.Create, FileAccess.Write, FileShare.None))
                await youtube.Videos.Streams.CopyToAsync(videoStream, vfs, null, cancellationToken);

            _serviceLogger.LogInformation($"Downloading audio to {audioTemp}");
            await using (var afs = new FileStream(audioTemp, FileMode.Create, FileAccess.Write, FileShare.None))
                await youtube.Videos.Streams.CopyToAsync(audioStream, afs, null, cancellationToken);

            var psi = CreateFfmpegStartInfo(videoFormat, videoTemp, audioFormat, audioTemp, outputPath);
            _serviceLogger.LogInformation($"Starting FFmpeg mux: {string.Join(' ', psi.ArgumentList)}");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start FFmpeg process.");

            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                throw new TimeoutException("FFmpeg process exceeded 15 seconds and was terminated.");
            }

            string errorOutput = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg failed to mux video and audio: {errorOutput}");
        }
        finally
        {
            try { if (File.Exists(videoTemp)) File.Delete(videoTemp); } catch { }
            try { if (File.Exists(audioTemp)) File.Delete(audioTemp); } catch { }
        }
    }

    internal static ProcessStartInfo CreateFfmpegStartInfo(
        string videoFormat,
        string videoTemp,
        string audioFormat,
        string audioTemp,
        string outputPath)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("-y");
        processStartInfo.ArgumentList.Add("-f");
        processStartInfo.ArgumentList.Add(videoFormat);
        processStartInfo.ArgumentList.Add("-i");
        processStartInfo.ArgumentList.Add(videoTemp);
        processStartInfo.ArgumentList.Add("-f");
        processStartInfo.ArgumentList.Add(audioFormat);
        processStartInfo.ArgumentList.Add("-i");
        processStartInfo.ArgumentList.Add(audioTemp);
        processStartInfo.ArgumentList.Add("-c:v");
        processStartInfo.ArgumentList.Add("copy");
        processStartInfo.ArgumentList.Add("-c:a");
        processStartInfo.ArgumentList.Add("aac");
        processStartInfo.ArgumentList.Add("-shortest");
        processStartInfo.ArgumentList.Add("-f");
        processStartInfo.ArgumentList.Add("mp4");
        processStartInfo.ArgumentList.Add(outputPath);

        return processStartInfo;
    }

    private static IVideoStreamInfo GetBestVideoStream(IEnumerable<IVideoStreamInfo> streams, string? preferredQuality)
    {
        IVideoStreamInfo? stream = null;

        if (!string.IsNullOrWhiteSpace(preferredQuality))
        {
            stream = streams
                .Where(s => s.VideoQuality.Label.Equals(preferredQuality, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();
        }

        return stream ?? streams
            .OrderByDescending(s => s.VideoQuality.MaxHeight)
            .ThenByDescending(s => s.Bitrate)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No suitable video stream found.");
    }

    private static IAudioStreamInfo GetBestAudioStream(IEnumerable<IAudioStreamInfo> streams, string? preferredQuality)
    {
        IAudioStreamInfo? stream = null;

        if (!string.IsNullOrWhiteSpace(preferredQuality))
        {
            stream = streams
                .Where(s => (s.Bitrate.KiloBitsPerSecond + "kbps").Equals(preferredQuality, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();
        }

        return stream ?? streams
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No suitable audio stream found.");
    }
}
