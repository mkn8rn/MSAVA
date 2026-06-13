using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_Shared.Models
{
    public class StreamReturnFileDTO
    {
        public Guid Id { get; set; }
        public required string FileName { get; set; }
        public required string FileExtension { get; set; }
        public required Stream FileStream { get; set; }

        public string DownloadFileName
        {
            get
            {
                var extension = FileExtension.Trim().TrimStart('.');
                if (string.IsNullOrWhiteSpace(extension))
                    return FileName;

                var expectedSuffix = $".{extension}";
                return FileName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
                    ? FileName
                    : $"{FileName}{expectedSuffix}";
            }
        }
    }
}
