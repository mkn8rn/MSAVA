using MSAVA_INF.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Utils
{
    public static class FileExtensionUtils
    {
        public static string GetFileExtension(SavedFileReferenceDB db)
        {
            return db.FileExtension.ToString().TrimStart('_').ToLowerInvariant();
        }
    }
}
