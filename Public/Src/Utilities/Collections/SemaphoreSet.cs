// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Represents a set of semaphores and their current usage counts
    /// </summary>
    /// <remarks>
    /// All members of this class are thread-safe.
    /// </remarks>
    public abstract class SemaphoreSet
    {
        /// <summary>
        /// The object use to synchronize access to internal data structures. Internal use only.
        /// </summary>
        protected readonly object SyncLock;

        /// <summary>
        /// List containing the limits for each semaphore. Internal use only.
        /// </summary>
        protected readonly List<int> SemaphoreLimits;

        /// <summary>
        /// List containing the usage for each semaphore. Internal use only.
        /// </summary>
        private int[] m_semaphoreUsages;

        /// <summary>
        /// Shallow copy constructor
        /// </summary>
        /// <param name="copy">the set whose structures will be shared with this instance</param>
        protected SemaphoreSet(SemaphoreSet copy)
        {
            SyncLock = copy.SyncLock;
            SemaphoreLimits = copy.SemaphoreLimits;
            m_semaphoreUsages = CollectionUtilities.EmptyArray<int>();
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        protected SemaphoreSet()
        {
            SyncLock = new object();
            SemaphoreLimits = new List<int>();
            m_semaphoreUsages = CollectionUtilities.EmptyArray<int>();
        }

        /// <summary>
        /// Gets the usage for the semaphore with the given index
        /// </summary>
        public int GetUsage(int index)
        {
            var usages = m_semaphoreUsages;
            if (index >= usages.Length)
            {
                return 0;
            }

            return usages[index];
        }

        /// <summary>
        /// Attempts to acquire the specified resources
        /// </summary>
        /// <param name="resources">the resources to acquire</param>
        /// <param name="force">true to force acquisition of the resources even acquisition would exceed limits for the semaphores</param>
        /// <returns>true if the resources where successfully acquired. False, otherwise.</returns>
        public bool TryAcquireResources(ItemResources resources, bool force = false)
        {
            return TryAcquireResources(resources, out int? limitingResourceIndex, force);
        }

        /// <summary>
        /// Attempts to acquire the specified resources
        /// </summary>
        /// <param name="resources">the resources to acquire</param>
        /// <param name="limitingResourceIndex">the first encountered semaphore limiting resource acquisition if any</param>
        /// <param name="force">true to force acquisition of the resources even acquisition would exceed limits for the semaphores</param>
        /// <returns>true if the resources where successfully acquired. False, otherwise.</returns>
        public bool TryAcquireResources(ItemResources resources, out int? limitingResourceIndex, bool force = false)
        {
            if (!resources.IsValid || resources.SemaphoreIncrements.Length == 0)
            {
                limitingResourceIndex = null;
                return true;
            }

            lock (SyncLock)
            {
                if (!HasAvailableResourcesCore(resources, out limitingResourceIndex, force))
                {
                    return false;
                }

                for (int i = 0; i < resources.SemaphoreIncrements.Length; i++)
                {
                    int usageCount = resources.SemaphoreIncrements[i];
                    if (usageCount == 0)
                    {
                        continue;
                    }

                    m_semaphoreUsages[i] += usageCount;
                    Contract.Assume(force || m_semaphoreUsages[i] <= SemaphoreLimits[i]);
                }
            }

            limitingResourceIndex = null;
            return true;
        }

        private bool HasAvailableResourcesCore(ItemResources resources, out int? limitingResourceIndex, bool force = false)
        {
            Contract.Assume(
                resources.SemaphoreIncrements.Length <= SemaphoreLimits.Count,
                "ItemResources uses unknown semaphore indices.");

            Array.Resize(ref m_semaphoreUsages, SemaphoreLimits.Count);

            if (!force)
            {
                for (int i = 0; i < resources.SemaphoreIncrements.Length; i++)
                {
                    int usageCount = resources.SemaphoreIncrements[i];
                    if (usageCount == 0)
                    {
                        continue;
                    }

                    if ((m_semaphoreUsages[i] + usageCount) > SemaphoreLimits[i])
                    {
                        limitingResourceIndex = i;
                        return false;
                    }
                }
            }

            limitingResourceIndex = null;
            return true;
        }

        /// <summary>
        /// Gets whether the semaphore set currently can acquire the given resources
        /// </summary>
        /// <param name="resources">the resources to acquire</param>
        /// <returns>true if the resources where available. False, otherwise.</returns>
        public bool HasAvailableResources(ItemResources resources)
        {
            if (!resources.IsValid || resources.SemaphoreIncrements.Length == 0)
            {
                return true;
            }

            lock (SyncLock)
            {
                return HasAvailableResourcesCore(resources, out int? limitingResourceIndex);
            }
        }

        /// <summary>
        /// Releases the resources
        /// </summary>
        public void ReleaseResources(ItemResources resources)
        {
            if (!resources.IsValid || resources.SemaphoreIncrements.Length == 0)
            {
                return;
            }

            lock (SyncLock)
            {
                Array.Resize(ref m_semaphoreUsages, SemaphoreLimits.Count);

                for (int i = 0; i < resources.SemaphoreIncrements.Length; i++)
                {
                    int usageCount = resources.SemaphoreIncrements[i];
                    m_semaphoreUsages[i] -= usageCount;
                    Contract.Assume(m_semaphoreUsages[i] >= 0);
                }
            }
        }
    }

    /// <summary>
    /// Represents a set of keyed semaphores and their current usage counts
    /// </summary>
    /// <remarks>
    /// All members of this class are thread-safe.
    /// </remarks>
    /// <typeparam name="TKey">the type of the key to use for the semaphores</typeparam>
    public sealed class SemaphoreSet<TKey> : SemaphoreSet
    {
        private readonly ConcurrentDictionary<TKey, SemaphoreInfo> m_semaphoresIndices;
        private readonly ConcurrentDictionary<int, TKey> m_semaphoreKeyIndexMap;

        /// <summary>
        /// Class constructor
        /// </summary>
        public SemaphoreSet(IEqualityComparer<TKey> equalityComparer = null)
        {
            equalityComparer = equalityComparer ?? EqualityComparer<TKey>.Default;
            m_semaphoresIndices = new ConcurrentDictionary<TKey, SemaphoreInfo>(equalityComparer);
            m_semaphoreKeyIndexMap = new ConcurrentDictionary<int, TKey>();
        }

        /// <summary>
        /// Create copy with the same backing structures but independent usage counts
        /// </summary>
        private SemaphoreSet(SemaphoreSet<TKey> copy)
            : base(copy)
        {
            m_semaphoresIndices = copy.m_semaphoresIndices;
            m_semaphoreKeyIndexMap = copy.m_semaphoreKeyIndexMap;
        }

        /// <summary>
        /// Creates copy with the same backing structures but independent usage counts
        /// </summary>
        public SemaphoreSet<TKey> CreateSharingCopy()
        {
            return new SemaphoreSet<TKey>(this);
        }

        /// <summary>
        /// Gets the corresponding key for the semaphore index
        /// </summary>
        public TKey GetKey(int semaphoreIndex)
        {
            return m_semaphoreKeyIndexMap[semaphoreIndex];
        }

        /// <summary>
        /// Creates a new semaphore with the given limit and key or gets the current one if a semaphore
        /// for the key already exists
        /// </summary>
        /// <param name="key">the key for the semaphore</param>
        /// <param name="limit">the limit for the semaphore</param>
        /// <returns>the semaphore index</returns>
        public int CreateSemaphore(TKey key, int limit)
        {
            if (m_semaphoresIndices.TryGetValue(key, out SemaphoreInfo semaphoreInfo))
            {
                if (semaphoreInfo.Limit == limit)
                {
                    return semaphoreInfo.Index;
                }
            }

            lock (SyncLock)
            {
                semaphoreInfo = m_semaphoresIndices.AddOrUpdate(
                    key,
                    new SemaphoreInfo { Index = SemaphoreLimits.Count, Limit = limit },
                    (k, info) => new SemaphoreInfo { Index = info.Index, Limit = limit });

                m_semaphoreKeyIndexMap[semaphoreInfo.Index] = key;

                if (semaphoreInfo.Index == SemaphoreLimits.Count)
                {
                    SemaphoreLimits.Add(limit);
                }
                else
                {
                    SemaphoreLimits[semaphoreInfo.Index] = limit;
                }

                return semaphoreInfo.Index;
            }
        }

        private sealed class SemaphoreInfo
        {
            public int Index;

            public int Limit;
        }
    }
}
