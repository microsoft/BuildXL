// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// List of lazily resolved <see cref="MachineLocation"/>s from a <see cref="MachineIdSet"/>.
    /// </summary>
    public sealed class MachineList : IReadOnlyList<MachineLocation>
    {
        /// <nodoc />
        public class Settings
        {
            /// <nodoc />
            public bool Randomize { get; set; } = true;

            /// <nodoc />
            public bool PrioritizeDesignatedLocations { get; set; }

            /// <nodoc />
            public bool DeprioritizeMaster { get; set; }
        }

        private readonly MachineReputationTracker _reputationTracker;
        private readonly ClusterState _clusterState;
        private readonly Settings _settings;
        private readonly ContentHash _hash;
        private readonly MachineIdSet _locations;
        private readonly ConcurrentDictionary<MachineId, MachineLocation> _cachedResolutions = new ConcurrentDictionary<MachineId, MachineLocation>();
        private List<MachineId>? _resolvedMachineIds;

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
                ResolveLocations();

                return _cachedResolutions.GetOrAdd(_resolvedMachineIds![index], static (id, @this) =>
                {
                    if (@this._clusterState.TryResolve(id, out var result))
                    {
                        return result;
                    }

                    throw new InvalidOperationException($"Unable to resolve machine location for machine id '{id}'.");
                }, this);
            }
        }

        /// <nodoc />
        public MachineList(MachineIdSet locations, MachineReputationTracker reputationTracker, ClusterState clusterState, ContentHash hash, Settings settings)
        {
            _locations = locations;
            _reputationTracker = reputationTracker;
            _clusterState = clusterState;
            _hash = hash;
            _settings = settings;

            // Capture the count rather than recomputing every time
            Count = locations.Count;
        }

        /// <inheritdoc />
        public IEnumerator<MachineLocation> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void ResolveLocations()
        {
            if (_resolvedMachineIds == null)
            {
                var resolvedMachineIds = new List<MachineId>(Count);
                resolvedMachineIds.AddRange(_locations.EnumerateMachineIds());

                if (_settings.Randomize)
                {
                    ThreadSafeRandom.Shuffle(resolvedMachineIds);
                }

                var master = _clusterState.MasterMachineId;

                resolvedMachineIds = resolvedMachineIds.OrderBy(id =>
                {
                    // Send master to the back.
                    if (_settings.DeprioritizeMaster && (master?.Equals(id) == true))
                    {
                        return int.MaxValue;
                    }

                    // Pull designated locations to front.
                    if (_settings.PrioritizeDesignatedLocations && _clusterState.IsDesignatedLocation(id, _hash, includeExpired: false))
                    {
                        return -1;
                    }

                    // Sort by reputation.
                    return (int)_reputationTracker.GetReputation(id);
                }).ToList();

                _resolvedMachineIds = resolvedMachineIds;
            }
        }
    }
}
