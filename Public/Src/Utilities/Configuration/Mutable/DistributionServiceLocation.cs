// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
