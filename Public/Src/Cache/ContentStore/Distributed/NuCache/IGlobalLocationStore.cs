// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a global location store (currently backed by Redis).
    /// </summary>
    public interface IGlobalLocationStore : ICheckpointRegistry, IStartupShutdownSlim
    {
        /// <summary>
        /// Machine id for the current machine as represented in the global cluster state.
        /// </summary>
        MachineId LocalMachineId { get; }

        /// <summary>
        /// Machine location for the current machine as represented in the global cluster state.
        /// </summary>
        MachineLocation LocalMachineLocation { get; }

        /// <summary>
        /// Calls a central store and updates <paramref name="state"/> based on the result.
        /// </summary>
        Task<BoolResult> UpdateClusterStateAsync(OperationContext context, ClusterState state);

        /// <summary>
        /// Notifies a central store that another machine should be selected as a master.
        /// </summary>
        /// <returns>Returns a new role.</returns>
        Task<Role?> ReleaseRoleIfNecessaryAsync(OperationContext context);

        /// <summary>
        /// Notifies a central store that the current machine is about to be repaired and will be inactive.
        /// </summary>
        Task<BoolResult> InvalidateLocalMachineAsync(OperationContext context);

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
        Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentHashes);

        /// <summary>
        /// Puts a blob into the content location store.
        /// </summary>
        Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob);

        /// <summary>
        /// Gets a blob from the content location store.
        /// </summary>
        Task<Result<byte[]>> GetBlobAsync(OperationContext context, ContentHash hash);

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
