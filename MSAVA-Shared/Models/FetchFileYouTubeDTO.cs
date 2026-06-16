using System;
using System.Collections.Generic;
using System.IO;

namespace MSAVA_Shared.Models
{
    public class FetchFileYouTubeDTO
    {
        public required string YouTubeUrl { get; set; }
        public required Guid AccessGroupId { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? Categories { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool PublicViewing { get; set; } = false;
        public bool PublicDownload { get; set; } = false;
        public bool DownloadVideo { get; set; } = false;
        public bool DownloadAudio { get; set; } = false;
        public string? VideoQuality { get; set; } // e.g. "1080p", "720p", "480p"
        public string? AudioQuality { get; set; } // e.g. "128kbps"
    }
}
