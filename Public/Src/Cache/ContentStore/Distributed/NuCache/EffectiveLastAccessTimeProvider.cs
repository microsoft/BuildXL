// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
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
        /// Tries to obtain <see cref="ContentInfo"/> from a store and <see cref="ContentLocationEntry"/> from a content location database.
        /// </summary>
        (ContentInfo info, ContentLocationEntry entry) GetContentInfo(OperationContext context, ContentHash hash);
    }

    /// <summary>
    /// A helper class responsible for computing content's effective age.
    /// </summary>
    public sealed class EffectiveLastAccessTimeProvider
    {
        private readonly LocalLocationStoreConfiguration _configuration;

        private readonly IContentResolver _contentResolver;
        private readonly IClock _clock;

        /// <nodoc />
        public EffectiveLastAccessTimeProvider(
            LocalLocationStoreConfiguration configuration,
            IClock clock,
            IContentResolver contentResolver)
        {
            _clock = clock;
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
            MachineId localMachineId,
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
                bool isImportantReplica = false;

                var (contentInfo, entry) = _contentResolver.GetContentInfo(context, contentHash.Hash);

                // Getting a size from content directory information first.
                long size = contentInfo.Size;

                if (entry != null)
                {
                    // Use the latest last access time between LLS and local last access time
                    DateTime distributedLastAccessTime = entry.LastAccessTimeUtc.ToDateTime();
                    lastAccessTime = distributedLastAccessTime > lastAccessTime ? distributedLastAccessTime : lastAccessTime;

                    isImportantReplica = IsImportantReplica(contentHash.Hash, entry, localMachineId, _configuration.DesiredReplicaRetention);
                    replicaCount = entry.Locations.Count;

                    if (size == 0)
                    {
                        size = entry.ContentSize;
                    }
                }

                var age = _clock.UtcNow - lastAccessTime;
                var effectiveAge = GetEffectiveLastAccessTime(_configuration, age, replicaCount, size, isImportantReplica, logInverseMachineRisk);

                var info = new ContentEvictionInfo(contentHash.Hash, age, effectiveAge, replicaCount, size, isImportantReplica);
                effectiveLastAccessTimes.Add(info);
            }

            return Result.Success<IReadOnlyList<ContentEvictionInfo>>(effectiveLastAccessTimes);
        }

        /// <summary>
        /// Returns true if a given hash considered to be an important replica for the given machine.
        /// </summary>
        public static bool IsImportantReplica(ContentHash hash, ContentLocationEntry entry, MachineId localMachineId, long desiredReplicaCount)
        {
            var locationsCount = entry.Locations.Count;
            if (locationsCount <= desiredReplicaCount)
            {
                return true;
            }

            if (desiredReplicaCount == 0)
            {
                return false;
            }

            // Making sure that probabilistically, some locations are considered important for the current machine.
            long replicaHash = unchecked((uint)HashCodeHelper.Combine(hash[0] | hash[1] << 8, localMachineId.Index + 1));

            var offset = replicaHash % locationsCount;

            var importantRangeStart = offset;
            var importantRangeStop = (offset + desiredReplicaCount) % locationsCount;

            // Getting an index of a current location in the location list
            int currentMachineLocationIndex = entry.Locations.GetMachineIdIndex(localMachineId);
            if (currentMachineLocationIndex == -1)
            {
                // This is used for testing only. The machine Id should be part of the machines.
                // But in tests it is useful to control the behavior of this method and in some cases to guarantee that some replica won't be important.
                return false;
            }

            // For instance, for locations [1, 20]
            // the important range can be [5, 7]
            // or [19, 1]
            if (importantRangeStart < importantRangeStop)
            {
                // This is the first case: the start index is less then the stop,
                // so the desired location should be within the range
                return currentMachineLocationIndex >= importantRangeStart && currentMachineLocationIndex <= importantRangeStop;
            }

            // Important range is broken because the start is greater then the end.
            // like [19, 1], so the location is important if it's index is >= start or <= the stop.
            // For instance, for 10 locations the start is 9 and the end is 2
            return currentMachineLocationIndex >= importantRangeStart || currentMachineLocationIndex <= importantRangeStop;
        }

        /// <summary>
        /// Gets effective last access time based on the age and importance.
        /// </summary>
        public static TimeSpan GetEffectiveLastAccessTime(LocalLocationStoreConfiguration configuration, TimeSpan age, int replicaCount, long size, bool isImportantReplica, double logInverseMachineRisk)
        {
            if (configuration.UseTieredDistributedEviction)
            {
                var ageBucketIndex = FindAgeBucketIndex(configuration, age);
                if (isImportantReplica)
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
