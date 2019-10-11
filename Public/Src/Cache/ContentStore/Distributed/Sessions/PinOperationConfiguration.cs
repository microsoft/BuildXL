using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    public class PinOperationConfiguration
    {
        /// <summary>
        /// Pin checks for global existence for content, fire and forget the default pin action, and return the global existence result.
        /// </summary>
        public bool ReturnGlobalExistenceFast { get; set; }

        public static PinOperationConfiguration Default()
        {
            return new PinOperationConfiguration()
            {
                ReturnGlobalExistenceFast = false
            };
        }
    }
}
