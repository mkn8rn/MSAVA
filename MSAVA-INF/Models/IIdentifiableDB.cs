using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public interface IIdentifiableDB
    {
        public Guid Id { get; set; }
    }
}
