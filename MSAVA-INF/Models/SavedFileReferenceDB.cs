using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class SavedFileReferenceDB : IIdentifiableDB
    {
        public Guid Id { get; set; }

        [MaxLength(32)]
        public required byte[] FileHash { get; set; }

        public required FileExtensionType FileExtension { get; set; }

        // permissions
        public required bool PublicDownload { get; set; } = false;
        public AccessGroupDB? AccessGroup { get; set; }
        public required Guid AccessGroupId { get; set; }
    }
}
