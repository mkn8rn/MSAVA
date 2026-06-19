using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_Shared.Models
{
    public class SaveFileFromStreamDTO : IFileCreationMetadataRequest
    {
        public required string FileName { get; set; }
        public required string FileExtension { get; set; }
        public required Stream Stream { get; set; }
        public required Guid AccessGroupId { get; set; }
        public List<string>? Tags { get; set; }
        public List<string>? Categories { get; set; }
        public string? Description { get; set; }
        public bool PublicViewing { get; set; } = false;
        public bool PublicDownload { get; set; } = false;
    }
}
