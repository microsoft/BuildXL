// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Information about the host machine/stamp that cache drive cache construction/configuration
    /// </summary>
    public class HostInfo
    {
        public string StampId { get; }
        public string RingId { get; }
        public IEnumerable<string> Capabilities { get; }

        public HostInfo(string stampId, string ringId, IEnumerable<string> capabilities)
        {
            StampId = stampId;
            RingId = ringId;
            Capabilities = new List<string>(capabilities);
        }
    }
}
