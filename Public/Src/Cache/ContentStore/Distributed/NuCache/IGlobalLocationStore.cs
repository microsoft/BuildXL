// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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

    /// <nodoc />
    public class PutBlobResult : BoolResult
    {
        /// <nodoc />
        public ContentHash Hash { get; }

        /// <nodoc />
        public long BlobSize { get; }

        /// <nodoc />
        public bool AlreadyInRedis { get; }

        /// <nodoc />
        public long? NewCapacityInRedis { get; }

        /// <nodoc />
        public string? RedisKey { get; }

        /// <nodoc />
        public PutBlobResult(ContentHash hash, long blobSize, bool alreadyInRedis = false, long? newCapacity = null, string? redisKey = null)
            : base(succeeded: true)
        {
            Hash = hash;
            BlobSize = blobSize;
            AlreadyInRedis = alreadyInRedis;
            NewCapacityInRedis = newCapacity;
            RedisKey = redisKey;
        }

        /// <nodoc />
        public PutBlobResult(ContentHash hash, long blobSize, string errorMessage)
            : base(errorMessage)
        {
            Hash = hash;
            BlobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, string message, ContentHash hash, long blobSize)
            : base(other, message)
        {
            Hash = hash;
            BlobSize = blobSize;
        }

        /// <nodoc />
        public PutBlobResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string baseResult = $"Hash=[{Hash.ToShortString()}], BlobSize=[{BlobSize}]";
            if (Succeeded)
            {
                if (AlreadyInRedis)
                {
                    return $"{baseResult}, AlreadyInRedis=[{AlreadyInRedis}]";
                }

                return $"{baseResult}. AlreadyInRedis=[False], RedisKey=[{RedisKey}], NewCapacity=[{NewCapacityInRedis}].";
            }

            return $"{baseResult}. {ErrorMessage}";
        }
    }

    /// <nodoc />
    public class GetBlobResult : BoolResult
    {
        /// <nodoc />
        public ContentHash Hash { get; }

        /// <nodoc />
        public byte[]? Blob { get; }

        /// <nodoc />
        public GetBlobResult(ContentHash hash, byte[]? blob)
            : base(succeeded: true)
        {
            Hash = hash;
            Blob = blob;
        }

        /// <nodoc />
        public GetBlobResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {

        }

        /// <nodoc />
        public GetBlobResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <nodoc />
        public GetBlobResult(ResultBase other, string message, ContentHash hash)
            : base(other, message)
        {
            Hash = hash;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (Succeeded)
            {
                return $"Hash=[{Hash.ToShortString()}] Size=[{Blob?.Length ?? -1}]";
            }

            return base.ToString();
        }
    }
}
