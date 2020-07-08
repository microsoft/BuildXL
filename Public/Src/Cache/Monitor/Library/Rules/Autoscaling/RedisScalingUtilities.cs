// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.Monitor.App.Rules.Autoscaling
{
    public static class RedisScalingUtilities
    {
        public static bool CanScale(RedisPlan from, RedisPlan to)
        {
            if (from == to)
            {
                return true;
            }

            // See: https://docs.microsoft.com/en-us/azure/azure-cache-for-redis/cache-how-to-scale#can-i-scale-to-from-or-within-a-premium-cache
            switch (from)
            {
                case RedisPlan.Basic:
                    // You can't scale from a Basic cache directly to a Premium cache
                    return to <= RedisPlan.Standard;
                case RedisPlan.Standard:
                    // You can't scale from a Standard cache down to a Basic cache
                    return to >= RedisPlan.Standard;
                case RedisPlan.Premium:
                    // You can't scale from a Premium cache down to a Standard or a Basic cache
                    return to == RedisPlan.Premium;
            }

            return false;
        }

        public static bool CanScale(RedisTier from, RedisTier to)
        {
            if (from.Equals(to))
            {
                return true;
            }

            if (!CanScale(from.Plan, to.Plan))
            {
                return false;
            }

            // You can scale from a Basic cache to a Standard cache but you can't change the size at the same time
            if (from.Plan == RedisPlan.Basic && to.Plan == RedisPlan.Standard)
            {
                return from.Capacity == to.Capacity;
            }

            // You can't scale from a larger size down to the C0 (250 MB) size
            if ((to.Plan == RedisPlan.Basic || to.Plan == RedisPlan.Standard) && to.Capacity == 0)
            {
                return false;
            }

            return true;
        }

        public static bool CanScale(RedisClusterSize from, RedisClusterSize to)
        {
            if (from.Equals(to))
            {
                return true;
            }

            if (!CanScale(from.Tier, to.Tier))
            {
                return false;
            }

            if (from.Shards != to.Shards && !from.Tier.Equals(to.Tier))
            {
                // Azure can't change both shards and tiers at once, we need to do them one at a time.
                return false;
            }

            return true;
        }

        public static TimeSpan ExpectedScalingDelay(RedisClusterSize from, RedisClusterSize to)
        {
            Contract.Requires(CanScale(from, to));

            if (from.Equals(to))
            {
                return TimeSpan.Zero;
            }

            if (from.Tier.Equals(to.Tier))
            {
                // The tier is the same, so autoscaling will be either adding or reducing shards
                var shardDelta = Math.Abs(from.Shards - to.Shards);
                return TimeSpan.FromTicks(Constants.RedisScaleTimePerShard.Ticks * shardDelta);
            }
            else
            {
                // Tier changed, which means the number of shards didn't. However, we will take the same amount of time
                // as the amount of shards that need to change tier.
                Contract.Assert(from.Shards == to.Shards);
                return TimeSpan.FromTicks(Constants.RedisScaleTimePerShard.Ticks * from.Shards);
            }
        }

        public class Node
        {
            public double ShortestDistanceFromSource { get; set; } = double.PositiveInfinity;

            public Node? Predecessor { get; set; } = null;

            public bool Visited { get; set; } = false;

            public RedisClusterSize ClusterSize { get; }

            public Node(RedisClusterSize clusterSize)
            {
                ClusterSize = clusterSize;
            }

            public override int GetHashCode()
            {
                return ClusterSize.GetHashCode();
            }
        }

        private class NodeComparer : IComparer<Node>, IComparer<RedisClusterSize>, IComparer<RedisTier>
        {
            public static readonly NodeComparer Instance = new NodeComparer();

            public int Compare(Node? x, Node? y)
            {
                if (x is null)
                {
                    if (y is null)
                    {
                        return 0;
                    }

                    return -1;
                }
                else if (y is null)
                {
                    return 1;
                }

                var distanceComparison = x.ShortestDistanceFromSource.CompareTo(y.ShortestDistanceFromSource);
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                return Compare(x.ClusterSize, y.ClusterSize);
            }

            public int Compare(RedisClusterSize? x, RedisClusterSize? y)
            {
                if (x is null)
                {
                    if (y is null)
                    {
                        return 0;
                    }

                    return -1;
                }
                else if (y is null)
                {
                    return 1;
                }

                var tierComparison = Compare(x.Tier, y.Tier);
                if (tierComparison != 0)
                {
                    return tierComparison;
                }

                return y.Shards - x.Shards;
            }

            public int Compare(RedisTier? x, RedisTier? y)
            {
                if (x is null)
                {
                    if (y is null)
                    {
                        return 0;
                    }

                    return -1;
                }
                else if (y is null)
                {
                    return 1;
                }

                if (x.Plan < y.Plan)
                {
                    return -1;
                }

                if (x.Plan > y.Plan)
                {
                    return 1;
                }

                return y.Capacity - x.Capacity;
            }
        }

        public static Dictionary<RedisClusterSize, Node> ComputeOneToAllShortestPath(IReadOnlyList<RedisClusterSize> vertices, Func<RedisClusterSize, IEnumerable<RedisClusterSize>> neighbors, Func<RedisClusterSize, RedisClusterSize, double> weight, RedisClusterSize from)
        {
            // We need to find a valid scale order to reach the target cluster size from the current one. To find it,
            // create an implicit graph G = (V, E) where V is the set of Redis sizes, and E is the set of valid
            // scalings given by the CanScale relation. In this graph, finding a shortest path between the current and
            // target sizes is equivalent to figuring out a way to scale among them optimally, as given by whatever
            // weight function we choose.
            var translation = new Dictionary<RedisClusterSize, Node>(capacity: vertices.Count);
            var minPriorityQueue = new SortedSet<Node>(comparer: NodeComparer.Instance);

            foreach (var vertex in vertices)
            {
                var node = new Node(vertex);
                if (vertex.Equals(from))
                {
                    node.ShortestDistanceFromSource = 0;
                }

                minPriorityQueue.Add(node);
                translation[vertex] = node;
            }

            while (minPriorityQueue.Count > 0)
            {
                var node = minPriorityQueue.Min;
                Contract.AssertNotNull(node);
                minPriorityQueue.Remove(node);

                if (node.Visited)
                {
                    continue;
                }

                node.Visited = true;
                foreach (var target in neighbors(node.ClusterSize))
                {
                    var adjacent = translation[target];
                    Contract.AssertNotNull(adjacent);

                    var distanceThroughNode = node.ShortestDistanceFromSource + weight(node.ClusterSize, target);
                    if (distanceThroughNode >= adjacent.ShortestDistanceFromSource)
                    {
                        continue;
                    }

                    // Typically, we'd like to do a decrease priority operation here. This is a work-around to avoid
                    // using a more complex data structure.
                    minPriorityQueue.Remove(adjacent);
                    adjacent.ShortestDistanceFromSource = distanceThroughNode;
                    adjacent.Predecessor = node;
                    minPriorityQueue.Add(adjacent);
                }
            }

            return translation;
        }

        public static IReadOnlyList<RedisClusterSize> ComputeShortestPath(RedisClusterSize from, RedisClusterSize to, Func<RedisClusterSize, IEnumerable<RedisClusterSize>> neighbors, Func<RedisClusterSize, RedisClusterSize, double> weight, IReadOnlyList<RedisClusterSize>? vertices = null)
        {
            if (from.Equals(to))
            {
                return Array.Empty<RedisClusterSize>();
            }

            vertices ??= RedisClusterSize.Instances;
            var shortestPaths = ComputeOneToAllShortestPath(vertices, neighbors, weight, from);
            return ComputeShortestPath(shortestPaths, from, to);
        }

        public static IReadOnlyList<RedisClusterSize> ComputeShortestPath(IReadOnlyDictionary<RedisClusterSize, Node> shortestPaths, RedisClusterSize from, RedisClusterSize to)
        {
            if (from.Equals(to))
            {
                return Array.Empty<RedisClusterSize>();
            }

            var path = new List<Node>();
            var current = shortestPaths[to];
            while (current.Predecessor != null)
            {
                path.Add(current);
                current = current.Predecessor;
            }

            if (!current.ClusterSize.Equals(from))
            {
                return Array.Empty<RedisClusterSize>();
            }

            path.Reverse();
            return path.Select(p => p.ClusterSize).ToList();
        }
    }
}
