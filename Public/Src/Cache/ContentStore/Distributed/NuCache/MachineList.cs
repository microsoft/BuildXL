// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// List of lazily resolved <see cref="MachineLocation"/>s from a <see cref="MachineIdSet"/>
    /// </summary>
    public sealed class MachineList : IReadOnlyList<MachineLocation>
    {
        private readonly Func<MachineId, MachineLocation> _resolvePath;
        private readonly MachineReputationTracker _reputationTracker;
        private readonly bool _randomize;
        private List<MachineId> _resolvedMachineIds;

        private readonly MachineIdSet _locations;

        /// <inheritdoc />
        public int Count { get; }

        /// <summary>
        /// Returns a path for a given index.
        /// </summary>
        /// <remarks>
        /// Result is null if resolvePath function returns null for a given machine id index.
        /// </remarks>
        public MachineLocation this[int index]
        {
            get
            {
                // TODO: Should we cache the resolution (bug 1365340)
                ResolveLocations();
                return _resolvePath(_resolvedMachineIds[index]);
            }
        }

        /// <nodoc />
        public MachineList(MachineIdSet locations, Func<MachineId, MachineLocation> resolvePath, MachineReputationTracker reputationTracker, bool randomize)
        {
            _locations = locations;
            _resolvePath = resolvePath;
            _reputationTracker = reputationTracker;
            _randomize = randomize;

            // Capture the count rather than recomputing every time
            Count = locations.Count;
        }

        /// <inheritdoc />
        public IEnumerator<MachineLocation> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void ResolveLocations()
        {
            if (_resolvedMachineIds == null)
            {
                _resolvedMachineIds = new List<MachineId>(Count);
                _resolvedMachineIds.AddRange(_locations.EnumerateMachineIds());

                if (_randomize)
                {
                    ThreadSafeRandom.Shuffle(_resolvedMachineIds);
                }

                // Sorting resolved machine ids by reputation: machines with good reputation should be used first.
                _resolvedMachineIds = _resolvedMachineIds.OrderBy(id => (int)_reputationTracker.GetReputation(id)).ToList();
            }
        }
    }
}
