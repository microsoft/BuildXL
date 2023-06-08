// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    /// <summary>
    /// This class implements the Rendezvous hashing.
    /// 
    /// See: https://en.wikipedia.org/wiki/Rendezvous_hashing
    /// </summary>
    public class RendezvousConsistentHash<TShardId> : IMultiCandidateShardingScheme<int, TShardId>
        where TShardId : IComparable<TShardId>
    {
        private readonly IShardManager<TShardId> _shardManager;
        private readonly Func<TShardId, int> _hash;

        public RendezvousConsistentHash(IShardManager<TShardId> shardManager, Func<TShardId, int> hash)
        {
            _shardManager = shardManager;
            _hash = hash;
        }

        public Shard<TShardId>? Locate(int key)
        {
            return Locate(key, 1).FirstOrDefault();
        }

        public IReadOnlyCollection<Shard<TShardId>> Locate(int key, int candidates)
        {
            Contract.Requires(candidates > 0, $"Attempt to locate {candidates} {nameof(candidates)}, must be positive");

            var locations = _shardManager.Locations;
            var set = new SortedSet<Weighted>();
            foreach (var location in locations)
            {
                if (!location.Available)
                {
                    continue;
                }

                var weighted = new Weighted(location.Location, Weight(key, location.Location));
                set.Add(weighted);
                if (set.Count > candidates)
                {
                    var min = set.Min;
                    if (min is not null)
                    {
                        set.Remove(min);
                    }
                }
            }

            return set;
        }

        private record Weighted(TShardId Location, double Weight) : Shard<TShardId>(Location), IComparable<Weighted>
        {
            public int CompareTo(Weighted? other)
            {
                // This should basically never happen, but we need to handle it to make the compiler happy.
                if (other is null)
                {
                    return 1;
                }

                int weightComparison = Weight.CompareTo(other!.Weight);
                return weightComparison != 0 ? weightComparison : Location.CompareTo(other!.Location);
            }
        }

        private double Weight(int key, TShardId shard)
        {
            // NOTE: we could probably use something like W_rand from the paper, but this is simpler and works well enough.
            // See: https://www.eecs.umich.edu/techreports/cse/96/CSE-TR-316-96.pdf
            const double min = (double)int.MinValue;
            const double r = (double)int.MaxValue - min;

            var hash = HashCodeHelper.Combine(key, _hash(shard));

            // Turn into (0, 1]
            var range = (((double)hash) - min) / r;
            if (Math.Abs(range) < double.Epsilon)
            {
                range = double.Epsilon;
            }

            var score = 1.0 / -Math.Log(range);
            return score;
        }
    }
}
