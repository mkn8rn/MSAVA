using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;
using System.Diagnostics;

namespace MSAVA_BLL.Services.Import;

public class YouTubeImportService : IFileImportService<FetchFileYouTubeDTO>
{
    private readonly FilePersistenceService _persistenceService;
    private readonly ServiceLogger _serviceLogger;
    private readonly IYouTubeDownloadClient _youtubeClient;

    public YouTubeImportService(FilePersistenceService persistenceService, ServiceLogger serviceLogger)
        : this(persistenceService, serviceLogger, new YoutubeExplodeDownloadClient())
    {
    }

    internal YouTubeImportService(
        FilePersistenceService persistenceService,
        ServiceLogger serviceLogger,
        IYouTubeDownloadClient youtubeClient)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _youtubeClient = youtubeClient ?? throw new ArgumentNullException(nameof(youtubeClient));
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

        var downloadManifest = await _youtubeClient.GetDownloadManifestAsync(dto.YouTubeUrl, cancellationToken);
        string fileName = string.IsNullOrWhiteSpace(downloadManifest.Title) ? "YouTube Video" : downloadManifest.Title;
        string fileExtension = "mp4";
        string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        _serviceLogger.LogInformation($"Downloading YouTube content to temp path {tempFilePath}");

        try
        {
            if (dto.DownloadVideo && dto.DownloadAudio)
            {
                YouTubeStreamInfo? streamInfo = null;
                if (!string.IsNullOrWhiteSpace(dto.VideoQuality))
                {
                    streamInfo = downloadManifest.MuxedStreams
                        .Where(s => string.Equals(s.VideoQualityLabel, dto.VideoQuality, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.BitrateKiloBitsPerSecond)
                        .FirstOrDefault();
                }

                streamInfo ??= downloadManifest.MuxedStreams
                    .OrderByDescending(s => s.VideoMaxHeight)
                    .ThenByDescending(s => s.BitrateKiloBitsPerSecond)
                    .FirstOrDefault();

                if (streamInfo != null)
                {
                    fileExtension = streamInfo.ContainerName;
                    await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await _youtubeClient.CopyToAsync(streamInfo, fileStream, cancellationToken);
                }
                else
                {
                    await MuxVideoAndAudio(
                        _youtubeClient,
                        downloadManifest.VideoStreams,
                        downloadManifest.AudioStreams,
                        dto,
                        tempFilePath,
                        cancellationToken);
                }
            }
            else if (dto.DownloadVideo)
            {
                var videoStream = GetBestVideoStream(downloadManifest.VideoStreams, dto.VideoQuality);
                fileExtension = videoStream.ContainerName;
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _youtubeClient.CopyToAsync(videoStream, fileStream, cancellationToken);
            }
            else if (dto.DownloadAudio)
            {
                var audioStream = GetBestAudioStream(downloadManifest.AudioStreams, dto.AudioQuality);
                fileExtension = audioStream.ContainerName;
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _youtubeClient.CopyToAsync(audioStream, fileStream, cancellationToken);
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
        finally
        {
            DeleteTempFileIfPresent(tempFilePath);
        }
    }

    private async Task MuxVideoAndAudio(
        IYouTubeDownloadClient youtube,
        IReadOnlyList<YouTubeStreamInfo> videoStreams,
        IReadOnlyList<YouTubeStreamInfo> audioStreams,
        FetchFileYouTubeDTO dto,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var videoStream = GetBestVideoStream(videoStreams, dto.VideoQuality);
        var audioStream = GetBestAudioStream(audioStreams, dto.AudioQuality);

        string videoFormat = videoStream.ContainerName;
        string audioFormat = audioStream.ContainerName;

        string videoTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string audioTemp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            _serviceLogger.LogInformation($"Downloading video to {videoTemp}");
            await using (var vfs = new FileStream(videoTemp, FileMode.Create, FileAccess.Write, FileShare.None))
                await youtube.CopyToAsync(videoStream, vfs, cancellationToken);

            _serviceLogger.LogInformation($"Downloading audio to {audioTemp}");
            await using (var afs = new FileStream(audioTemp, FileMode.Create, FileAccess.Write, FileShare.None))
                await youtube.CopyToAsync(audioStream, afs, cancellationToken);

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

    private static YouTubeStreamInfo GetBestVideoStream(IEnumerable<YouTubeStreamInfo> streams, string? preferredQuality)
    {
        YouTubeStreamInfo? stream = null;

        if (!string.IsNullOrWhiteSpace(preferredQuality))
        {
            stream = streams
                .Where(s => string.Equals(s.VideoQualityLabel, preferredQuality, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.BitrateKiloBitsPerSecond)
                .FirstOrDefault();
        }

        return stream ?? streams
            .OrderByDescending(s => s.VideoMaxHeight)
            .ThenByDescending(s => s.BitrateKiloBitsPerSecond)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No suitable video stream found.");
    }

    private static YouTubeStreamInfo GetBestAudioStream(IEnumerable<YouTubeStreamInfo> streams, string? preferredQuality)
    {
        YouTubeStreamInfo? stream = null;

        if (!string.IsNullOrWhiteSpace(preferredQuality))
        {
            stream = streams
                .Where(s => (s.BitrateKiloBitsPerSecond + "kbps").Equals(preferredQuality, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.BitrateKiloBitsPerSecond)
                .FirstOrDefault();
        }

        return stream ?? streams
            .OrderByDescending(s => s.BitrateKiloBitsPerSecond)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No suitable audio stream found.");
    }

    private static void DeleteTempFileIfPresent(string tempFilePath)
    {
        if (!File.Exists(tempFilePath))
            return;

        try { File.Delete(tempFilePath); } catch { /* best-effort temp cleanup */ }
    }
}
