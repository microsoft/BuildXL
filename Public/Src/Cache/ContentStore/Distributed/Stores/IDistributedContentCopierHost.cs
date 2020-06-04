// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Handles copies from remote locations to a local store
    /// </summary>
    public interface IDistributedContentCopierHost
    {
        /// <summary>
        /// The staging folder where copies are placed before putting into CAS
        /// </summary>
        AbsolutePath WorkingFolder { get; }

        /// <summary>
        /// Reports about new reputation for a given location.
        /// </summary>
        void ReportReputation(MachineLocation location, MachineReputation reputation);
    }
}
