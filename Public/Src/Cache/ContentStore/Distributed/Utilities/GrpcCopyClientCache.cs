// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
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
        /// <param name="clientConfig">Configuration for created clients.</param>
        /// <param name="maxClientCount">Maximum number of clients to cache.</param>
        /// <param name="maxClientAgeMinutes">Maximum age of cached clients.</param>
        /// <param name="waitBetweenCleanupMinutes">Minutes to wait between cache purges.</param>
        public GrpcCopyClientCache(Context context, GrpcCopyClient.Configuration clientConfig, int maxClientCount = 512, int maxClientAgeMinutes = 55, int waitBetweenCleanupMinutes = 17)
            : base(context, maxClientCount, maxClientAgeMinutes, waitBetweenCleanupMinutes, (key) => new GrpcCopyClient(key, clientConfig))
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
