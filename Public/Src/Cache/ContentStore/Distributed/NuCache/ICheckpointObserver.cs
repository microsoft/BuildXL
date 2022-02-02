// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Observes restore checkpoint operations
    /// </summary>
    public interface ICheckpointObserver : IStartupShutdownSlim
    {
        /// <summary>
        /// Call immediately prior to restoring the given checkpoint
        /// </summary>
        Task<BoolResult> OnChangeCheckpointAsync(OperationContext context, CheckpointState initialState, CheckpointManifest manifest);
    }
}
