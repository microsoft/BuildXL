// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    public class RedisClusterSize : IEquatable<RedisClusterSize>
    {
        public static IReadOnlyList<RedisClusterSize> Instances =
            RedisTier.Instances.SelectMany(tier => Enumerable
                .Range(1, 10)
                .Select(shards => new RedisClusterSize(tier, shards))).ToList();

        public RedisTier Tier { get; }

        /// <summary>
        /// From 1 through 10
        /// </summary>
        public int Shards { get; }

        public int ClusterMemorySizeMb => Tier.Properties.MemorySizeMb * Shards;

        public double MonthlyCostUsd => Tier.Properties.MonthlyCostPerShardUsd * Shards;

        public int? AvailableBandwidthMb => Tier.Properties.AvailableBandwidthMb * Shards;

        public int? EstimatedRequestsPerSecond => Tier.Properties.EstimatedRequestsPerSecond * Shards;

        private readonly Lazy<IReadOnlyList<RedisClusterSize>> _scaleEligibleSizes;

        public IReadOnlyList<RedisClusterSize> ScaleEligibleSizes => _scaleEligibleSizes.Value;

        public RedisClusterSize(RedisTier tier, int shards)
        {
            Contract.Requires(1 <= shards && shards <= 10, "Number of shards out of bounds, must be between 1 and 10");
            Tier = tier;
            Shards = shards;

            _scaleEligibleSizes = new Lazy<IReadOnlyList<RedisClusterSize>>(() => Instances.Where(to => RedisScalingUtilities.CanScale(this, to)).ToList(), System.Threading.LazyThreadSafetyMode.PublicationOnly);
        }

        public static RedisClusterSize Parse(string clusterSize)
        {
            return RedisClusterSize.TryParse(clusterSize).ThrowIfFailure();
        }

        public static Result<RedisClusterSize> TryParse(string clusterSize)
        {
            if (string.IsNullOrEmpty(clusterSize))
            {
                return new Result<RedisClusterSize>(errorMessage: $"Empty string can't be parsed into {nameof(RedisClusterSize)}");
            }

            var parts = clusterSize.Split(new string[] { "/" }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                return new Result<RedisClusterSize>(errorMessage: $"Failed to split {nameof(RedisClusterSize)} from `{clusterSize}`: {string.Join(", ", parts)}");
            }

            var redisTierResult = RedisTier.TryParse(parts[0]);
            if (!redisTierResult.Succeeded)
            {
                return new Result<RedisClusterSize>(redisTierResult);
            }

            var redisTier = redisTierResult.Value;
            Contract.AssertNotNull(redisTier);
            if (!int.TryParse(parts[1], out var shards)) {
                return new Result<RedisClusterSize>(errorMessage: $"Failed to obtain number of shards from `{parts[1]}`");
            }

            return new RedisClusterSize(redisTier, shards);
        }

        public static Result<RedisClusterSize> FromAzureCache(IRedisCache redisCache)
        {
            var sku = redisCache.Sku;

            if (!Enum.TryParse<RedisPlan>(sku.Name, out var plan))
            {
                return new Result<RedisClusterSize>(errorMessage: $"Failed to parse `{nameof(RedisPlan)}` from value `{sku.Name}`");
            }

            try
            {
                return new RedisClusterSize(new RedisTier(plan, sku.Capacity), redisCache.ShardCount);
            }
            catch (Exception exception)
            {
                return new Result<RedisClusterSize>(exception);
            }
        }

        public override string ToString() => $"{Tier}/{Shards}";

        public override bool Equals(object? obj)
        {
            return obj is RedisClusterSize item && Equals(item);
        }

        public bool Equals(RedisClusterSize? redisClusterSize)
        {
            return redisClusterSize != null && Tier.Equals(redisClusterSize.Tier) && Shards == redisClusterSize.Shards;
        }

        public override int GetHashCode() => (Tier.GetHashCode(), Shards).GetHashCode();
    }
}
