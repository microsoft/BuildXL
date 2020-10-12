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
    public interface IGlobalLocationStore : ICheckpointRegistry, IStartupShutdownSlim
    {
        /// <summary>
        /// The cluster state containing global and machine-specific information registered in the global cluster state
        /// </summary>
        ClusterState ClusterState { get; }

        /// <summary>
        /// Calls a central store and updates <paramref name="clusterState"/> based on the result.
        /// </summary>
        Task<BoolResult> UpdateClusterStateAsync(OperationContext context, ClusterState clusterState, MachineState machineState = MachineState.Open);

        /// <summary>
        /// Notifies a central store that another machine should be selected as a master.
        /// </summary>
        /// <returns>Returns a new role.</returns>
        Task<Role?> ReleaseRoleIfNecessaryAsync(OperationContext context);

        /// <summary>
        /// Notifies a central store that the current machine (and all associated machine ids) is about to be repaired and will be inactive.
        /// </summary>
        Task<Result<MachineState>> SetLocalMachineStateAsync(OperationContext context, MachineState state);

        /// <summary>
        /// Gets the list of <see cref="ContentLocationEntry"/> for every hash specified by <paramref name="contentHashes"/> from a central store.
        /// </summary>
        /// <remarks>
        /// The resulting collection (in success case) will have the same size as <paramref name="contentHashes"/>.
        /// </remarks>
        Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes);

        /// <summary>
        /// Notifies a central store that content represented by <paramref name="contentHashes"/> is available on a current machine.
        /// </summary>
        Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHashWithSize> contentHashes);

        /// <summary>
        /// Puts a blob into the content location store.
        /// </summary>
        Task<PutBlobResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob);

        /// <summary>
        /// Gets a blob from the content location store.
        /// </summary>
        Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash hash);

        /// <summary>
        /// Gets a value indicating whether the store supports storing and retrieving blobs.
        /// </summary>
        bool AreBlobsSupported { get; }

        /// <nodoc />
        CounterSet GetCounters(OperationContext context);

        /// <nodoc />
        CounterCollection<GlobalStoreCounters> Counters { get; }
    }
}
