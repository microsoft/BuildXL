// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Cache for <see cref="GrpcCopyClient"/>.
    /// </summary>
    public sealed class GrpcCopyClientCache : ResourcePool<GrpcCopyClientKey, GrpcCopyClient>
    {
        /// <summary>
        /// Cache for <see cref="GrpcCopyClient"/>.
        /// </summary>
        /// <param name="context">Content.</param>
        /// <param name="maxClientCount">Maximum number of clients to cache.</param>
        /// <param name="maxClientAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="waitBetweenCleanupMinutes">Minutes to wait between cache purges.</param>
        /// <param name="bufferSize">Buffer size used to read files from disk.</param>
        public GrpcCopyClientCache(Context context, int maxClientCount = 512, int maxClientAgeMinutes = 55, int waitBetweenCleanupMinutes = 17, int? bufferSize = null)
            : base(context, maxClientCount, maxClientAgeMinutes, waitBetweenCleanupMinutes, (key) => new GrpcCopyClient(key, bufferSize))
        {
        }

        /// <summary>
        /// Use an existing <see cref="GrpcCopyClient"/> if possible, else create a new one.
        /// </summary>
        public Task<ResourceWrapper<GrpcCopyClient>> CreateAsync(string host, int grpcPort, bool useCompression)
        {
            return base.CreateAsync(new GrpcCopyClientKey(host, grpcPort, useCompression));
        }
    }
}
