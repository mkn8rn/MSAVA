using System;
using System.Collections.Generic;

namespace MSAVA_Shared.Models
{
    public class SaveFileFromUrlDTO : IFileCreationMetadataRequest
    {
        public required string FileUrl { get; set; }
        public required string FileName { get; set; }
        public required string FileExtension { get; set; }
        public required Guid AccessGroupId { get; set; }
        public List<string>? Tags { get; set; } = new();
        public List<string>? Categories { get; set; } = new();
        public string? Description { get; set; } = string.Empty;
        public bool PublicViewing { get; set; } = false;
        public bool PublicDownload { get; set; } = false;
    }
}
