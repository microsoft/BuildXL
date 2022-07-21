// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Interfaces that used by <see cref="EffectiveLastAccessTimeProvider"/> in order to resolve content locations.
    /// </summary>
    public interface IContentResolver
    {
        /// <summary>
        /// The local machine id
        /// </summary>
        MachineId LocalMachineId { get; }

        /// <summary>
        /// Tries to obtain <see cref="ContentInfo"/> from a store and <see cref="ContentLocationEntry"/> from a content location database.
        /// </summary>
        (ContentInfo localInfo, ContentLocationEntry distributedEntry, bool isDesignatedLocation) GetContentInfo(OperationContext context, ContentHash hash);
    }

    /// <summary>
    /// A helper class responsible for computing content's effective age.
    /// </summary>
    public sealed class EffectiveLastAccessTimeProvider
    {
        private readonly LocalLocationStoreConfiguration _configuration;

        private readonly IContentResolver _contentResolver;
        private readonly DateTime _now;

        /// <nodoc />
        public EffectiveLastAccessTimeProvider(
            LocalLocationStoreConfiguration configuration,
            IClock clock,
            IContentResolver contentResolver)
        {
            _now = clock.UtcNow;
            _configuration = configuration;
            _contentResolver = contentResolver;
        }

        /// <summary>
        /// Returns effective last access time for all the <paramref name="contentHashes"/>.
        /// </summary>
        /// <remarks>
        /// Effective last access time is computed based on entries last access time considering content's size and replica count.
        /// This method is used in distributed eviction.
        /// </remarks>
        public Result<IReadOnlyList<ContentEvictionInfo>> GetEffectiveLastAccessTimes(
            OperationContext context,
            IReadOnlyList<ContentHashWithLastAccessTime> contentHashes)
        {
            Contract.RequiresNotNull(contentHashes);
            Contract.Requires(contentHashes.Count > 0);

            // This is required because the code inside could throw.

            var effectiveLastAccessTimes = new List<ContentEvictionInfo>();
            double logInverseMachineRisk = -Math.Log(_configuration.MachineRisk);

            foreach (var contentHash in contentHashes)
            {
                DateTime lastAccessTime = contentHash.LastAccessTime;

                // Don't treat current machine as replica in consumer only case
                int replicaCount = _configuration.DistributedContentConsumerOnly ? 0 : 1;
                ReplicaRank rank = ReplicaRank.None;

                var (localInfo, distributedEntry, isDesignatedLocation) = _contentResolver.GetContentInfo(context, contentHash.Hash);

                // Getting a size from content directory information first.
                long size = localInfo.Size;

                if (distributedEntry != null)
                {
                    // Use the latest last access time between LLS and local last access time
                    DateTime distributedLastAccessTime = distributedEntry.LastAccessTimeUtc.ToDateTime();
                    lastAccessTime = distributedLastAccessTime > lastAccessTime ? distributedLastAccessTime : lastAccessTime;

                    if (isDesignatedLocation)
                    {
                        rank = Combine(ReplicaRank.Designated, rank);
                    }

                    replicaCount = distributedEntry.Locations.Count;

                    if (size == 0)
                    {
                        size = distributedEntry.ContentSize;
                    }
                }

                var localAge = _now - contentHash.LastAccessTime;
                var age = _now - lastAccessTime;
                var effectiveAge = GetEffectiveLastAccessTime(_configuration, age, replicaCount, size, rank, logInverseMachineRisk);

                var info = new ContentEvictionInfo(
                    contentHash.Hash, 
                    age: age, 
                    localAge: localAge, 
                    effectiveAge: effectiveAge, 
                    replicaCount: replicaCount, 
                    size: size,
                    rank: rank,
                    timestampUtc: _now);
                effectiveLastAccessTimes.Add(info);
            }

            return Result.Success<IReadOnlyList<ContentEvictionInfo>>(effectiveLastAccessTimes);
        }

        private static ReplicaRank Combine(ReplicaRank rank1, ReplicaRank rank2)
        {
            return (ReplicaRank)Math.Max((byte)rank1, (byte)rank2);
        }

        /// <summary>
        /// Gets effective last access time based on the age and importance.
        /// </summary>
        public static TimeSpan GetEffectiveLastAccessTime(LocalLocationStoreConfiguration configuration, TimeSpan age, int replicaCount, long size, ReplicaRank rank, double logInverseMachineRisk)
        {
            if (configuration.UseTieredDistributedEviction)
            {
                if (rank == ReplicaRank.Protected)
                {
                    // Protected replicas should be evicted only as a last resort.
                    return TimeSpan.Zero;
                }

                var ageBucketIndex = FindAgeBucketIndex(configuration, age);
                if (rank != ReplicaRank.None)
                {
                    ageBucketIndex = Math.Max(0, ageBucketIndex - configuration.Settings.ImportantReplicaBucketOffset);
                }

                return configuration.AgeBuckets[ageBucketIndex];
            }
            else
            {
                // Incorporate both replica count and size into an evictability metric.
                // It's better to eliminate big content (more bytes freed per eviction) and it's better to eliminate content with more replicas (less chance
                // of all replicas being inaccessible).
                // A simple model with exponential decay of likelihood-to-use and a fixed probability of each replica being inaccessible shows that the metric
                //   evictability = age + (time decay parameter) * (-log(risk of content unavailability) * (number of replicas) + log(size of content))
                // minimizes the increase in the probability of (content wanted && all replicas inaccessible) / per bytes freed.
                // Since this metric is just the age plus a computed quantity, it can be interpreted as an "effective age".
                TimeSpan totalReplicaPenalty = TimeSpan.FromMinutes(configuration.ContentLifetime.TotalMinutes * (Math.Max(1, replicaCount) * logInverseMachineRisk + Math.Log(Math.Max(1, size))));
                return age + totalReplicaPenalty;
            }
        }

        private static int FindAgeBucketIndex(LocalLocationStoreConfiguration configuration, TimeSpan age)
        {
            Contract.Requires(configuration.AgeBuckets.Count > 0);

            for (int i = 0; i < configuration.AgeBuckets.Count; i++)
            {
                var bucket = configuration.AgeBuckets[i];
                if (age < bucket)
                {
                    return i;
                }
            }

            return configuration.AgeBuckets.Count - 1;
        }
    }
}
