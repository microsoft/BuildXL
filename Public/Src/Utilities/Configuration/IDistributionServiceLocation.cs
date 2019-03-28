// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
