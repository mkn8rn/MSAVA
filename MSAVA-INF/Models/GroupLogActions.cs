using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public enum GroupLogActions : short
    {
        Unknown = 0,

        // Access group
        AccessGroupCreated = 1,
        AccessGroupDeleted = 2,
        AccessGroupUpdated = 3,
        AccessGroupOwnerUpdated = 4,

        // User management
        AccessGroupUserAdded = 100,
        AccessGroupUserRemoved = 101,
        AccessGroupUserRoleUpdated = 102,

        // Subgroup management
        AccessGroupSubGroupAdded = 200,
        AccessGroupSubGroupRemoved = 201,
        AccessGroupSubGroupRoleUpdated = 202,
    }
}
