// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Microsoft.Azure.Management.Monitor.Models;
using System.Diagnostics.ContractsLight;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.Monitor.Library.Az;

namespace BuildXL.Cache.Monitor.Library.Rules.Autoscaling
{
    internal class ModelContext
    {
        public double MinimumAllowedClusterMemoryMb { get; internal set; }

        public double MinimumAllowedClusterRps { get; internal set; }

        public long? MaximumAllowedClusterMemoryMb { get; internal set; }
    }

    internal class ModelOutput
    {
        public RedisClusterSize TargetClusterSize { get; set; }

        public ModelContext ModelContext { get; set; }

        public double Cost { get; set; }

        public IReadOnlyList<RedisClusterSize> ScalePath { get; set; }

        public ModelOutput(RedisClusterSize targetClusterSize, ModelContext modelContext, double cost, IReadOnlyList<RedisClusterSize> scalePath)
        {
            TargetClusterSize = targetClusterSize;
            ModelContext = modelContext;
            Cost = cost;
            ScalePath = scalePath;
        }
    }

    internal class RedisAutoscalingAgent
    {
        public class Configuration
        {
            public TimeSpan UsedMemoryLookback { get; set; } = TimeSpan.FromDays(7);

            public TimeSpan UsedMemoryAggregationInterval { get; set; } = TimeSpan.FromMinutes(5);

            public double MinimumExtraMemoryAvailable { get; set; } = 0.3;

            public TimeSpan WorkloadLookback { get; set; } = TimeSpan.FromDays(2);

            public TimeSpan WorkloadAggregationInterval { get; set; } = TimeSpan.FromMinutes(5);

            public double MinimumWorkloadExtraPct { get; set; } = 0.3;

            public long? MaximumClusterMemoryAllowedMb { get; set; } = null;

            /// <summary>
            /// The minimum amount of money a downscale needs to save in order to be allowed to happen
            /// </summary>
            /// <remarks>
            /// This is just set based on looking at what worthless autoscales the monitor does. Concretely, we're
            /// looking to avoid autoscales that save like 10 USD.
            /// </remarks>
            public double? MinimumCostSavingForDownScaling { get; set; } = 100;
        }

        private readonly Configuration _configuration;
        private readonly IAzureMetricsClient _azureMetricsClient;

        public RedisAutoscalingAgent(Configuration configuration, IAzureMetricsClient azureMetricsClient)
        {
            Contract.Requires(configuration.UsedMemoryAggregationInterval < configuration.UsedMemoryLookback);
            Contract.Requires(configuration.UsedMemoryLookback <= TimeSpan.FromDays(30));
            Contract.Requires(configuration.MinimumExtraMemoryAvailable > 0);

            _configuration = configuration;
            _azureMetricsClient = azureMetricsClient;
        }

        public async Task<Result<ModelOutput>> EstimateBestClusterSizeAsync(OperationContext context, IRedisInstance redisInstance)
        {
            var now = DateTime.UtcNow;

            var redisAzureId = redisInstance.Id;
            var currentClusterSize = redisInstance.ClusterSize;
            var modelContext = await ComputeFeaturesAsync(context, now, redisAzureId);
            return Predict(currentClusterSize, modelContext);
        }

        private Result<ModelOutput> Predict(RedisClusterSize currentClusterSize, ModelContext modelContext)
        {
            // TODO: autoscaler should consider the server load percentage as well. If a shard had a very high load
            // percentage, it means that it is for some reason receiving an uneven load. Hence, adding shards helps in
            // this situation. There is no easy way to add that to the current model. Ideas:
            //  - If any server reached a load >70% at any time in the period analyzed, we need to guarantee that
            //    there's at least as many shards as there were before (i.e. no downscales are allowed).
            var shortestPaths = ComputeAllowedPaths(currentClusterSize, modelContext);

            var eligibleClusterSizes = shortestPaths
                .Select(kvp => (Size: kvp.Key, Node: kvp.Value))
                // Find all plans that we can reach from the current one via scaling operations, and that we allow scaling to
                .Where(entry => entry.Node.ShortestDistanceFromSource != double.PositiveInfinity && IsScalingAllowed(currentClusterSize, entry.Size, modelContext))
                // Compute the cost of taking the given route
                .Select(entry => (entry.Size, entry.Node, Cost: CostFunction(currentClusterSize, entry.Size, modelContext, shortestPaths)))
                .ToList();

            // Rank them by cost ascending
            var costSorted = eligibleClusterSizes
                .OrderBy(pair => pair.Cost)
                .ToList();

            if (costSorted.Count == 0)
            {
                return new Result<ModelOutput>(errorMessage: "No cluster size available for scaling");
            }

            return new ModelOutput(
                targetClusterSize: costSorted[0].Size,
                modelContext: modelContext,
                cost: costSorted[0].Cost,
                scalePath: RedisScalingUtilities.ComputeShortestPath(shortestPaths, currentClusterSize, costSorted[0].Size));
        }

        private static IReadOnlyDictionary<RedisClusterSize, RedisScalingUtilities.Node> ComputeAllowedPaths(RedisClusterSize currentClusterSize, ModelContext modelContext)
        {
            // We need to reach the target cluster size, but we can't do it in one shot because business rules won't
            // let us, so we need to compute a path to get to it. This is probably the most complex part of the
            // algorithm, there are several competing aspects we want to optimize for, in descending importance:
            //  - We want for memory to get to the target level ASAP
            //  - We want to keep the number of shards as stable as possible, given that changing them can cause build
            //    failures
            //  - We'd like to get there in the fewest amount of time possible
            //  - The route needs to be deterministic, so that if we are forced to stop and re-compute it we'll take
            //    the same route.
            //  - We'd like to minimize the cost of the route
            // Multi-constraint optimization over graphs is NP-complete and algorithms are hard to come up with, so we
            // do our best.

            Func<RedisClusterSize, IEnumerable<RedisClusterSize>> neighbors =
                currentClusterSize => currentClusterSize.ScaleEligibleSizes.Where(targetClusterSize =>
                {
                    // Constrain paths to downscale at most one shard at the time. This only makes paths longer, so it
                    // is safe. The reason behind this is that the service doesn't really tolerate big reductions.
                    if (targetClusterSize.Shards < currentClusterSize.Shards)
                    {
                        return targetClusterSize.Shards == currentClusterSize.Shards - 1;
                    }

                    return true;
                });

            Func<RedisClusterSize, RedisClusterSize, double> weight =
                (from, to) =>
                {
                    // This factor is used to avoid transitioning to any kind of intermediate plan that may cause a
                    // production outage. If we don't have it, we may transition into a state in which we have less
                    // cluster memory available than we need. By adjusting the weight function, we guarantee that
                    // this only happens iff there is no better path; moreover, we will always choose the lesser of
                    // two evils if given no choice.
                    double clusterMemoryPenalization = 0;

                    var delta = to.ClusterMemorySizeMb - modelContext.MinimumAllowedClusterMemoryMb;
                    if (delta < 0)
                    {
                        // The amount of cluster memory is less than we need, so we penalize taking this path by
                        // adding the amount of memory that keeps us away from the target.
                        clusterMemoryPenalization = -delta;
                    }

                    // This needs to be at least one so we don't pick minimum paths that are arbitrarily long
                    return 1 + clusterMemoryPenalization;
                };


            return RedisScalingUtilities.ComputeOneToAllShortestPath(vertices: RedisClusterSize.Instances, neighbors: neighbors, weight: weight, from: currentClusterSize);
        }

        private async Task<ModelContext> ComputeFeaturesAsync(OperationContext context, DateTime now, string redisAzureId)
        {
            var modelContext = new ModelContext();

            await Task.WhenAll(
                    ComputeMemoryFeaturesAsync(context, now, redisAzureId, modelContext),
                    ComputeWorkloadFeaturesAsync(context, now, redisAzureId, modelContext)
                );

            return modelContext;
        }

        private async Task ComputeWorkloadFeaturesAsync(OperationContext context, DateTime now, string redisAzureId, ModelContext modelContext)
        {
            var groupedMetrics = await FetchOperationsPerSecondPerShardAsync(context, now, redisAzureId);

            if (groupedMetrics.Count == 0)
            {
                // If all metrics are missing, we won't constraint plans on having a certain minimum number of
                // operations. This is used to account for an Azure Monitor API bug whereby some metrics may not be
                // reported
                return;
            }

            // ops/s scales linearly with shards
            var expectedClusterRps = groupedMetrics.Max();
            modelContext.MinimumAllowedClusterRps = (1 + _configuration.MinimumWorkloadExtraPct) * expectedClusterRps;
        }

        protected virtual async Task<List<double>> FetchOperationsPerSecondPerShardAsync(OperationContext context, DateTime now, string redisAzureId)
        {
            var startTimeUtc = now - _configuration.WorkloadLookback;
            var endTimeUtc = now;

            var operationsPerSecond = await _azureMetricsClient.GetMetricsWithDimensionAsync(
                redisAzureId,
                new[] { AzureRedisShardMetric.OperationsPerSecond.ToMetricName() },
                "ShardId",
                startTimeUtc,
                endTimeUtc,
                _configuration.WorkloadAggregationInterval,
                aggregations: new[] { AggregationType.Maximum },
                context.Token);

            // NOTE: Measurement values may be null if we are querying for data that is not present (i.e. a shard that
            // has disappeared, or such).
            return operationsPerSecond
                .SelectMany(kvp => kvp.Value.Select((measurement, index) => (measurement, index)))
                .GroupBy(entry => entry.index)
                .OrderBy(group => group.Key)
                .Select(group => group.Sum(entry => entry.measurement.Maximum ?? 0))
                .ToList();
        }

        private async Task ComputeMemoryFeaturesAsync(OperationContext context, DateTime now, string redisAzureId, ModelContext modelContext)
        {
            var groupedMetrics = await FetchMemoryUsedPerShardAsync(context, now, redisAzureId);

            // Metric is reported in bytes, we use megabytes for everything
            var expectedClusterMemoryUsageMb = groupedMetrics.Max() / 1e+6;

            modelContext.MinimumAllowedClusterMemoryMb = (1 + _configuration.MinimumExtraMemoryAvailable) * expectedClusterMemoryUsageMb;

            modelContext.MaximumAllowedClusterMemoryMb = _configuration.MaximumClusterMemoryAllowedMb;
        }

        protected virtual async Task<List<double>> FetchMemoryUsedPerShardAsync(OperationContext context, DateTime now, string redisAzureId)
        {
            var startTimeUtc = now - _configuration.UsedMemoryLookback;
            var endTimeUtc = now;

            var usedMemoryBytes = await _azureMetricsClient.GetMetricsWithDimensionAsync(
                redisAzureId,
                new[] { AzureRedisShardMetric.UsedMemory.ToMetricName() },
                "ShardId",
                startTimeUtc,
                endTimeUtc,
                _configuration.UsedMemoryAggregationInterval,
                aggregations: new[] { AggregationType.Maximum },
                context.Token);

            // NOTE: Measurement values may be null if we are querying for data that is not present (i.e. a shard that
            // has disappeared, or such).
            return usedMemoryBytes
                .SelectMany(kvp => kvp.Value.Select((measurement, index) => (measurement, index)))
                .GroupBy(entry => entry.index)
                .OrderBy(group => group.Key)
                .Select(group => group.Sum(entry => entry.measurement.Maximum ?? 0))
                .ToList();
        }

        /// <summary>
        /// Decides whether a scaling move is allowed. At this point, we don't know if Azure Cache for Redis business
        /// rules allow scaling from the current to the target size. We just decide whether it is reasonable based on
        /// our knowledge of our production workload.
        ///
        /// The autoscaler will figure out how to reach the desired plan.
        /// </summary>
        private bool IsScalingAllowed(
            RedisClusterSize currentClusterSize,
            RedisClusterSize targetClusterSize,
            ModelContext modelContext)
        {
            // WARNING: order matters in the following if statements. Please be careful.

            // Cluster must be able to handle the amount of data we'll give it, with some overhead in case of
            // production issues. Notice we don't introduce a per-shard restriction; reason for this is that the shards
            // distribute keys evenly.
            if (targetClusterSize.ClusterMemorySizeMb < modelContext.MinimumAllowedClusterMemoryMb)
            {
                return false;
            }

            // Cluster must be able to handle the amount of operations needed. Notice we don't introduce a per-shard
            // restriction; reason for this is that the shards distribute keys evenly.
            if (targetClusterSize.EstimatedRequestsPerSecond < modelContext.MinimumAllowedClusterRps)
            {
                return false;
            }

            // Disallow going over the maximum allowed cluster memory
            // NOTE: we only constrain on the target not being over the allowed size, rather than all nodes in the
            // path. The reason for this is that our ability to reach all nodes is based on being able to scale above
            // any specific memory threshold.
            if (modelContext.MaximumAllowedClusterMemoryMb != null && targetClusterSize.ClusterMemorySizeMb > modelContext.MaximumAllowedClusterMemoryMb.Value)
            {
                return false;
            }

            // Always allow not doing anything if it's available.
            // NOTE: this is here because in downscale situations we always want to ensure we have the "status quo"
            // action available.
            if (currentClusterSize.Equals(targetClusterSize))
            {
                return true;
            }

            // Disallow downscales that don't improve cost significantly
            if (_configuration.MinimumCostSavingForDownScaling != null)
            {
                var monthlyCostDelta = targetClusterSize.MonthlyCostUsd - currentClusterSize.MonthlyCostUsd;
                if (RedisScalingUtilities.IsDownScale(currentClusterSize, targetClusterSize) && monthlyCostDelta <= 0 && -monthlyCostDelta < _configuration.MinimumCostSavingForDownScaling)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// This function embodies the concept of "how much does it cost to switch from
        /// <paramref name="current"/> to <paramref name="target"/>". At this point, we can assume that:
        ///     - The two input sizes are valid states to be in
        ///     - We can reach the target from current via some amount of autoscaling operations
        /// Hence, we're just ranking amonst the many potential states.
        /// </summary>
        private static double CostFunction(RedisClusterSize current, RedisClusterSize target, ModelContext modelContext, IReadOnlyDictionary<RedisClusterSize, RedisScalingUtilities.Node> shortestPaths)
        {
            // Switching to the same size (i.e. no op) is free
            if (current.Equals(target))
            {
                return 0;
            }

            var shortestPath = RedisScalingUtilities.ComputeShortestPath(shortestPaths, current, target);
            Contract.Assert(shortestPath.Count > 0);

            // Positive if we are spending more money, negative if we are saving
            return (double)(target.MonthlyCostUsd - current.MonthlyCostUsd);
        }
    }
}
