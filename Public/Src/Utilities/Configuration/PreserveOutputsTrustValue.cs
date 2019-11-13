using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Details of Perserve outputs trust value range
    /// </summary>
    public enum PreserveOutputsTrustValue : int
    {
        /// <summary>
        /// Lowest preserving outputs trust value
        /// </summary>
        Lowest = 1,

        /// <summary>
        /// Highest preserving outputs trust value
        /// </summary>
        Hightest = int.MaxValue
    }
}
