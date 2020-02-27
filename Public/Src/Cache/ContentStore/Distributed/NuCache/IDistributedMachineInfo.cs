// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Information about a machine CAS instance
    /// </summary>
    public interface IDistributedMachineInfo
    {
        /// <summary>
        /// The local content store
        /// </summary>
        ILocalContentStore LocalContentStore { get; }

        /// <summary>
        /// The local machine id
        /// </summary>
        MachineId LocalMachineId { get; }
    }
}
