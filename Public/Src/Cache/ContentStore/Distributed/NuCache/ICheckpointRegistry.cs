// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interface that represents a central store (currently backed by Redis).
    /// </summary>
    public interface ICheckpointRegistry
    {
        /// <summary>
        /// Register a checkpoint with the given <paramref name="checkpointId"/> and <paramref name="sequencePoint"/>.
        /// </summary>
        Task<BoolResult> RegisterCheckpointAsync(OperationContext context, string checkpointId, EventSequencePoint sequencePoint);

        /// <summary>
        /// Gets the most recent checkpoint state.
        /// </summary>
        Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context);
    }
}
