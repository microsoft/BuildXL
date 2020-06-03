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
                int replicaCount = 1;
                ReplicaRank rank = ReplicaRank.None;

                var (localInfo, distributedEntry, isDesignatedLocation) = _contentResolver.GetContentInfo(context, contentHash.Hash);

                // Getting a size from content directory information first.
                long size = localInfo.Size;

                if (distributedEntry != null)
                {
                    // Use the latest last access time between LLS and local last access time
                    DateTime distributedLastAccessTime = distributedEntry.LastAccessTimeUtc.ToDateTime();
                    lastAccessTime = distributedLastAccessTime > lastAccessTime ? distributedLastAccessTime : lastAccessTime;

                    rank = GetReplicaRank(contentHash.Hash, distributedEntry, _contentResolver.LocalMachineId, _configuration, _now);
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
                    rank: rank);
                effectiveLastAccessTimes.Add(info);
            }

            return Result.Success<IReadOnlyList<ContentEvictionInfo>>(effectiveLastAccessTimes);
        }

        private static ReplicaRank Combine(ReplicaRank rank1, ReplicaRank rank2)
        {
            return (ReplicaRank)Math.Max((byte)rank1, (byte)rank2);
        }

        /// <summary>
        /// Returns rank of a given hash which determines the degree to which content is preserved by altering the effective last access time
        /// </summary>
        /// <remarks>
        /// When throttled eviction is enabled (<see cref="LocalLocationStoreConfiguration.ThrottledEvictionInterval"/> != 0), 
        /// content eviction is throttled by having all but one replica consider content Protected. (Protected means EffectiveAge=0 such
        /// that content is only evicted as a last resort).
        /// </remarks>
        public static ReplicaRank GetReplicaRank(
            ContentHash hash,
            ContentLocationEntry entry,
            MachineId localMachineId,
            LocalLocationStoreConfiguration configuration,
            DateTime now)
        {
            var desiredReplicaCount = configuration.DesiredReplicaRetention;
            if (desiredReplicaCount == 0)
            {
                return ReplicaRank.None;
            }

            var locationsCount = entry.Locations.Count;
            if (locationsCount <= desiredReplicaCount
                // If using throttled eviction, we might need to upgrade the rank to Protected
                // so don't return here
                && configuration.ThrottledEvictionInterval == TimeSpan.Zero)
            {
                return ReplicaRank.Important;
            }

            // Making sure that probabilistically, some locations are considered important for the current machine.
            long contentHashCode = unchecked((uint)HashCodeHelper.Combine(hash[0] | hash[1] << 8, hash[1]));

            var importantRangeStart = contentHashCode % locationsCount;

            // Getting an index of a current location in the location list
            int currentMachineLocationIndex = entry.Locations.GetMachineIdIndex(localMachineId);
            if (currentMachineLocationIndex == -1)
            {
                // This is used for testing only. The machine Id should be part of the machines.
                // But in tests it is useful to control the behavior of this method and in some cases to guarantee that some replica won't be important.
                return ReplicaRank.None;
            }

            // In case of important range wrapping around end of location list to start of location list
            // we need to compute a positive offset from the range start to see if the replica exists in the range
            // i.e. range start = 5, location count = 7, and desired location count = 3
            // important range contains [5, 6 and 0] since it overflows the end of the list
            var offset = currentMachineLocationIndex - importantRangeStart;
            if (offset < 0)
            {
                offset += locationsCount;
            }

            var lastImportantReplicaOffset = Math.Min(desiredReplicaCount, locationsCount) - 1;
            if (offset >= desiredReplicaCount)
            {
                return ReplicaRank.None;
            }
            else if (configuration.ThrottledEvictionInterval == TimeSpan.Zero)
            {
                // Throttled eviction is disabled. Just mark the replica as important
                // since its in the important range
                return ReplicaRank.Important;
            }
            else if (offset != lastImportantReplicaOffset)
            {
                // All but last important replica are always Protected
                return ReplicaRank.Protected;
            }

            // How throttled eviction works:
            // 1. Compute which machines consider the content important
            // This is done by computing a hash code from the content hash modulo location count to 
            // generate a start index into the list replicas.
            // For instance,
            // given locations: [4, 11, 22, 35, 73, 89]
            // locationCount = 6,
            // if contentHashCode % locationCount = 2 and DesiredReplicaCount = 3
            // then the machines considering content important are [22, 35, 73]
            // 2. All but last important replica must be consider Protected (i.e. 22, 35 have rank Protected)
            // 3. Compute if last replica is protected.
            // This is based of to time ranges or buckets of duration ThrottledEvictionInterval
            // For instance,
            // if ThrottleInterval = 20 minutes
            // 10:00AM-10:20AM -> (timeBucketIndex = 23045230) % DesiredReplicaCount = 2 = evictableOffset
            // 10:20AM-10:40AM -> (timeBucketIndex = 23045231) % DesiredReplicaCount = 0 = evictableOffset
            // 10:40AM-11:00AM -> (timeBucketIndex = 23045232) % DesiredReplicaCount = 1 = evictableOffset
            // 11:00AM-11:20AM -> (timeBucketIndex = 23045233) % DesiredReplicaCount = 2 = evictableOffset
            // So for times 10:00AM-10:20AM and 11:00AM-11:20AM the last important replica is evictable
            var timeBucketIndex = now.Ticks / configuration.ThrottledEvictionInterval.Ticks;

            // NOTE: We add contentHashCode to timeBucketIndex so that not all Protected content is considered evictable
            // at the same time
            var evictableOffset = (contentHashCode + timeBucketIndex) % desiredReplicaCount;
            if (evictableOffset == offset)
            {
                return ReplicaRank.Important;
            }
            else
            {
                // The replica is not currently evictable. Mark it as protected which will give it the minimum effective age
                // so that it is only evicted as a last resort
                return ReplicaRank.Protected;
            }
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
                    ageBucketIndex = Math.Max(0, ageBucketIndex - configuration.ImportantReplicaBucketOffset);
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
