using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public class ErrorLogDB : IIdentifiableDB
    {
        public Guid Id { get; set; }
        public required int StatusCode { get; set; }
        public required DateTime Timestamp { get; set; }
        public required Guid? UserId { get; set; }
        public UserDB? User { get; set; }
    }
}
