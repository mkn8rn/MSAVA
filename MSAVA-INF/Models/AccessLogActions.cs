using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public enum AccessLogActions : short
    {
        Unknown = 0,

        // File creation and modification
        NewFileCreated = 1,
        NewReferenceAddedToExistingFile = 2,
        FileDeleted = 3,
        FileReferenceDeleted = 4,

        // File access
        AccessViaFileStream = 100,
        AccessViaPhysicalFile = 101,
    }
}
