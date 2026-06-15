using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_Shared.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace MSAVA_BLL.Services.Import;

public class YouTubeImportService : IFileImportService<FetchFileYouTubeDTO>
{
    private readonly FilePersistenceService _persistenceService;
    private readonly ServiceLogger _serviceLogger;
    private readonly IYouTubeDownloadClient _youtubeClient;
    private readonly ILogger<YouTubeImportService> _logger;

    public YouTubeImportService(
        FilePersistenceService persistenceService,
        ServiceLogger serviceLogger,
        ILogger<YouTubeImportService> logger)
        : this(persistenceService, serviceLogger, logger, new YoutubeExplodeDownloadClient())
    {
    }

    internal YouTubeImportService(
        FilePersistenceService persistenceService,
        ServiceLogger serviceLogger,
        ILogger<YouTubeImportService> logger,
        IYouTubeDownloadClient youtubeClient)
    {
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _serviceLogger = serviceLogger ?? throw new ArgumentNullException(nameof(serviceLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        await _persistenceService.AuthorizeCreateInAccessGroupAsync(dto.AccessGroupId, cancellationToken);

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
                    fileExtension = ProviderFileType.RequireSupportedExtension("YouTube", streamInfo.ContainerName);
                    await CopyYouTubeStreamToFileAsync(streamInfo, tempFilePath, cancellationToken);
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
                fileExtension = ProviderFileType.RequireSupportedExtension("YouTube", videoStream.ContainerName);
                await CopyYouTubeStreamToFileAsync(videoStream, tempFilePath, cancellationToken);
            }
            else if (dto.DownloadAudio)
            {
                var audioStream = GetBestAudioStream(downloadManifest.AudioStreams, dto.AudioQuality);
                fileExtension = ProviderFileType.RequireSupportedExtension("YouTube", audioStream.ContainerName);
                await CopyYouTubeStreamToFileAsync(audioStream, tempFilePath, cancellationToken);
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
            TemporaryFileCleanup.DeleteIfPresent(tempFilePath, _logger);
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
            await CopyYouTubeStreamToFileAsync(youtube, videoStream, videoTemp, cancellationToken);

            _serviceLogger.LogInformation($"Downloading audio to {audioTemp}");
            await CopyYouTubeStreamToFileAsync(youtube, audioStream, audioTemp, cancellationToken);

            var psi = CreateFfmpegStartInfo(
                videoFormat,
                videoTemp,
                audioFormat,
                audioTemp,
                outputPath,
                _persistenceService.MaximumFileSizeBytes);
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
                KillProcessAfterTimeout(process);
                throw new TimeoutException("FFmpeg process exceeded 15 seconds and was terminated.");
            }

            string errorOutput = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg failed to mux video and audio: {errorOutput}");

            FileSizePolicy.EnsureWithinMaximum(
                new FileInfo(outputPath).Length,
                _persistenceService.MaximumFileSizeBytes);
        }
        finally
        {
            TemporaryFileCleanup.DeleteIfPresent(videoTemp, _logger);
            TemporaryFileCleanup.DeleteIfPresent(audioTemp, _logger);
        }
    }

    internal static ProcessStartInfo CreateFfmpegStartInfo(
        string videoFormat,
        string videoTemp,
        string audioFormat,
        string audioTemp,
        string outputPath,
        long maximumOutputSizeBytes)
    {
        maximumOutputSizeBytes = FileSizePolicy.RequireValidMaximum(maximumOutputSizeBytes);
        long ffmpegOutputSizeLimit = maximumOutputSizeBytes == long.MaxValue
            ? long.MaxValue
            : maximumOutputSizeBytes + 1;

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
        processStartInfo.ArgumentList.Add("-fs");
        processStartInfo.ArgumentList.Add(ffmpegOutputSizeLimit.ToString(CultureInfo.InvariantCulture));
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

    private void KillProcessAfterTimeout(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill FFmpeg process after timeout");
        }
    }

    private Task CopyYouTubeStreamToFileAsync(
        YouTubeStreamInfo streamInfo,
        string filePath,
        CancellationToken cancellationToken)
    {
        return CopyYouTubeStreamToFileAsync(_youtubeClient, streamInfo, filePath, cancellationToken);
    }

    private async Task CopyYouTubeStreamToFileAsync(
        IYouTubeDownloadClient youtube,
        YouTubeStreamInfo streamInfo,
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        var boundedFileStream = new FileSizeLimitedWriteStream(fileStream, _persistenceService.MaximumFileSizeBytes);
        await youtube.CopyToAsync(streamInfo, boundedFileStream, cancellationToken);
    }
}
