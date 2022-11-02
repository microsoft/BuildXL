// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a central store.
    /// </summary>
    public interface ICheckpointRegistry: IStartupShutdownSlim
    {
        /// <summary>
        /// Register a checkpoint with the given <paramref name="checkpointState"/>.
        /// </summary>
        Task<BoolResult> RegisterCheckpointAsync(OperationContext context, CheckpointState checkpointState);

        /// <summary>
        /// Gets the most recent checkpoint state.
        /// </summary>
        Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context);

        /// <summary>
        /// Deletes all existing checkpoints from the registry
        /// </summary>
        Task<BoolResult> ClearCheckpointsAsync(OperationContext context);
    }
}
