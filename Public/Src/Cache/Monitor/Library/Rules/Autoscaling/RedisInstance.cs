// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    internal sealed class RedisInstance
    {
        private readonly IAzure _azure;
        private readonly string _resourceId;

        public IRedisCache RedisCache { get; private set; }

        public RedisClusterSize ClusterSize { get; private set; }

        public string Name => RedisCache.Name;

        public bool IsReadyToScale => RedisCache.ProvisioningState == "Succeeded";

        private RedisInstance(IAzure azure, string resourceId, IRedisCache redisCache, RedisClusterSize clusterSize)
        {
            Contract.RequiresNotNullOrEmpty(resourceId);

            _azure = azure;
            _resourceId = resourceId;
            RedisCache = redisCache;
            ClusterSize = clusterSize;
        }

        public async Task<BoolResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            return (await GenerateInstanceMetadataAsync(_azure, _resourceId, cancellationToken))
                .Select(result =>
                {
                    RedisCache = result.Cache;
                    ClusterSize = result.Size;
                    return BoolResult.Success;
                });
        }

        public static async Task<Result<(IRedisCache Cache, RedisClusterSize Size)>> GenerateInstanceMetadataAsync(IAzure azure, string resourceId, CancellationToken cancellationToken = default)
        {
            // TODO: error handling
            var redisCache = await azure.RedisCaches.GetByIdAsync(resourceId, cancellationToken);
            var clusterSize = RedisClusterSize.FromAzureCache(redisCache).ThrowIfFailure();

            Contract.AssertNotNull(clusterSize);
            return new Result<(IRedisCache Cache, RedisClusterSize Size)>((redisCache, clusterSize));
        }

        public static async Task<Result<RedisInstance>> FromAzureAsync(IAzure azure, string resourceId, CancellationToken cancellationToken = default)
        {
            return (await GenerateInstanceMetadataAsync(azure, resourceId, cancellationToken))
                .Select(result => new RedisInstance(azure, resourceId, result.Cache, result.Size));
        }

        public static Result<RedisInstance> FromPreloaded(IAzure azure, IRedisCache redisCache)
        {
            return RedisClusterSize
                .FromAzureCache(redisCache)
                .Select(clusterSize => new RedisInstance(azure, redisCache.Id, redisCache, clusterSize));
        }

        public async Task<BoolResult> ScaleAsync(IReadOnlyList<RedisClusterSize> scalePath, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < scalePath.Count; i++) {
                await RefreshAsync(cancellationToken).ThrowIfFailureAsync();
                if (i > 0)
                {
                    // Validate that the Redis cluster's state is the one we expected
                    var previous = scalePath[i - 1];
                    if (!ClusterSize.Equals(previous))
                    {
                        return new BoolResult(errorMessage: $"Expected instance `{Name}` to have size `{previous}` but found `{ClusterSize}`");
                    }
                }

                var target = scalePath[i];
                await RequestScaleAsync(target, cancellationToken).ThrowIfFailureAsync();
            }

            return BoolResult.Success;
        }

        private async Task<BoolResult> RequestScaleAsync(RedisClusterSize targetClusterSize, CancellationToken cancellationToken = default)
        {
            if (ClusterSize.Equals(targetClusterSize))
            {
                return new BoolResult(errorMessage: $"No-op scale request attempted (`{ClusterSize}` -> `{targetClusterSize}`) on instance `{Name}`");
            }

            if (!RedisScalingUtilities.CanScale(ClusterSize, targetClusterSize))
            {
                return new BoolResult(errorMessage: $"Scale request `{ClusterSize}` -> `{targetClusterSize}` on instance `{Name}` is disallowed by Azure Cache for Redis");
            }

            if (!IsReadyToScale)
            {
                return new BoolResult(errorMessage: $"Redis instance `{Name}` is not ready to scale, current provisioning state is `{RedisCache.ProvisioningState}`");
            }

            var instance = RedisCache.Update();

            if (!ClusterSize.Tier.Equals(targetClusterSize.Tier))
            {
                switch (targetClusterSize.Tier.Plan)
                {
                    case RedisPlan.Basic:
                        instance = instance.WithBasicSku(targetClusterSize.Tier.Capacity);
                        break;
                    case RedisPlan.Standard:
                        instance = instance.WithStandardSku(targetClusterSize.Tier.Capacity);
                        break;
                    case RedisPlan.Premium:
                        instance = instance.WithPremiumSku(targetClusterSize.Tier.Capacity);
                        break;
                }
            }

            if (ClusterSize.Shards != targetClusterSize.Shards)
            {
                instance = instance.WithShardCount(targetClusterSize.Shards);
            }

            await instance.ApplyAsync(cancellationToken);

            return BoolResult.Success;
        }
    }
}
