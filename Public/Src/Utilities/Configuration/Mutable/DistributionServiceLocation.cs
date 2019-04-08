// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DistributionServiceLocation : IDistributionServiceLocation
    {
        /// <nodoc />
        public DistributionServiceLocation()
        {
        }

        /// <nodoc />
        public DistributionServiceLocation(IDistributionServiceLocation template)
        {
            IpAddress = template.IpAddress;
            BuildServicePort = template.BuildServicePort;
        }

        /// <inheritdoc />
        public string IpAddress { get; set; }

        /// <inheritdoc />
        public int BuildServicePort { get; set; }
    }
}
