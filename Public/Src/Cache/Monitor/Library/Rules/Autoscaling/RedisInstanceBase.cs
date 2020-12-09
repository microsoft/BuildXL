// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    internal abstract class RedisInstanceBase : IRedisInstance
    {
        protected readonly IAzure Azure;
        protected readonly string ResourceId;
        protected IRedisCache RedisCache;

        public string Id => RedisCache.Id;

        public string Name => RedisCache.Name;

        public string State => RedisCache.ProvisioningState;

        public bool IsFailed => RedisCache.ProvisioningState == "Failed";

        public bool IsReadyToScale => RedisCache.ProvisioningState == "Succeeded";

        public RedisClusterSize ClusterSize { get; private set; }

        internal RedisInstanceBase(IAzure azure, string resourceId, IRedisCache redisCache, RedisClusterSize clusterSize)
        {
            Contract.RequiresNotNullOrEmpty(resourceId);

            Azure = azure;
            ResourceId = resourceId;
            RedisCache = redisCache;
            ClusterSize = clusterSize;
        }

        public async Task<BoolResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            return (await GenerateInstanceMetadataAsync(Azure, ResourceId, cancellationToken))
                .Select(result =>
                {
                    RedisCache = result.Cache;
                    ClusterSize = result.Size;
                    return BoolResult.Success;
                });
        }

        protected static async Task<Result<(IRedisCache Cache, RedisClusterSize Size)>> GenerateInstanceMetadataAsync(IAzure azure, string resourceId, CancellationToken cancellationToken = default)
        {
            // TODO: error handling
            var redisCache = await azure.RedisCaches.GetByIdAsync(resourceId, cancellationToken);
            var clusterSize = RedisClusterSize.FromAzureCache(redisCache).ThrowIfFailure();

            Contract.AssertNotNull(clusterSize);
            return new Result<(IRedisCache Cache, RedisClusterSize Size)>((redisCache, clusterSize));
        }

        public abstract Task<BoolResult> ScaleAsync(IReadOnlyList<RedisClusterSize> scalePath, CancellationToken cancellationToken = default);
    }
}
