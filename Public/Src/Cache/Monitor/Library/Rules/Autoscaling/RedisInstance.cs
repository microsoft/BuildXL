// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    internal sealed class RedisInstance : RedisInstanceBase
    {
        internal RedisInstance(IAzure azure, string resourceId, IRedisCache redisCache, RedisClusterSize clusterSize)
            : base(azure, resourceId, redisCache, clusterSize)
        {
        }

        public override async Task<BoolResult> ScaleAsync(IReadOnlyList<RedisClusterSize> scalePath, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < scalePath.Count; i++)
            {
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

            return await SubmitScaleRequestAsync(targetClusterSize, cancellationToken);
        }

        private async Task<BoolResult> SubmitScaleRequestAsync(RedisClusterSize targetClusterSize, CancellationToken cancellationToken)
        {
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

        public static async Task<Result<IRedisInstance>> FromAzureAsync(IAzure azure, string resourceId, bool readOnly, CancellationToken cancellationToken = default)
        {
            return (await GenerateInstanceMetadataAsync(azure, resourceId, cancellationToken))
                .Select(result =>
                {
                    if (readOnly)
                    {
                        return (IRedisInstance)new ReadOnlyRedisInstance(azure, resourceId, result.Cache, result.Size);
                    }
                    else
                    {
                        return (IRedisInstance)new RedisInstance(azure, resourceId, result.Cache, result.Size);
                    }
                });
        }

        public static Result<IRedisInstance> FromPreloaded(IAzure azure, IRedisCache redisCache, bool readOnly)
        {
            return RedisClusterSize
                .FromAzureCache(redisCache)
                .Select(clusterSize =>
                {
                    if (readOnly)
                    {
                        return (IRedisInstance)new ReadOnlyRedisInstance(azure, redisCache.Id, redisCache, clusterSize);
                    }
                    else
                    {
                        return (IRedisInstance)new RedisInstance(azure, redisCache.Id, redisCache, clusterSize);
                    }
                });
        }
    }
}
