// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Server, port pair.
    /// </summary>
    public partial interface IDistributionServiceLocation
    {
        /// <summary>
        /// IpAddress of the worker
        /// </summary>
        string IpAddress { get; }

        /// <summary>
        /// Port of the worker
        /// </summary>
        int BuildServicePort { get; }
    }
}
