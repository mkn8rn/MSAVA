using System;
using System.Collections.Generic;

namespace MSAVA_Shared.Models
{
    public class AccessGroupDTO
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<AccessGroupDTO> SubGroups { get; set; } = [];
    }
}
