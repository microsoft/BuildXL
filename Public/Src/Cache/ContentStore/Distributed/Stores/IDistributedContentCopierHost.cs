// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

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

        /// <summary>
        /// Returns the designated locations for a particular hash.
        /// </summary>
        Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash);

        /// <summary>
        /// Returns the machine location of the master.
        /// </summary>
        Result<MachineLocation> GetMasterLocation();
    }
}
