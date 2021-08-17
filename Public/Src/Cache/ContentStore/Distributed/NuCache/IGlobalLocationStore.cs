// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a global location store (currently backed by Redis).
    /// </summary>
    public interface IGlobalLocationStore : ICheckpointRegistry, IMasterElectionMechanism, IStartupShutdownSlim
    {
        /// <summary>
        /// The cluster state containing global and machine-specific information registered in the global cluster state
        /// </summary>
        ClusterState ClusterState { get; }

        /// <nodoc />
        CounterSet GetCounters(OperationContext context);

        /// <nodoc />
        CounterCollection<GlobalStoreCounters> Counters { get; }
    }
}
