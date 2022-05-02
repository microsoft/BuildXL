// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils; // Needed for .NET Standard build.

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// List of lazily resolved <see cref="MachineLocation"/>s from a <see cref="MachineIdSet"/>.
    /// </summary>
    public sealed class MachineList : IReadOnlyList<MachineLocation>
    {
        private static readonly Tracer Tracer = new (nameof(MachineId));

        /// <nodoc />
        public record Settings
        {
            /// <nodoc />
            public bool PrioritizeDesignatedLocations { get; init; }

            /// <nodoc />
            public bool DeprioritizeMaster { get; init; }

            /// <summary>
            /// See <see cref="LocalLocationStoreConfiguration.ResolveMachineIdsEagerly"/>
            /// </summary>
            public bool ResolveLocationsEagerly { get; init; }
        }

        private readonly MachineReputationTracker _reputationTracker;
        private readonly ClusterState _clusterState;
        private readonly Settings _settings;
        private readonly ContentHash _hash;
        private readonly MachineIdSet _locations;
        private readonly ConcurrentDictionary<MachineId, MachineLocation> _cachedResolutions = new ();
        private List<MachineId>? _resolvedMachineIds;

        /// <inheritdoc />
        public int Count { get; }

        /// <summary>
        /// Returns a path for a given index.
        /// </summary>
        /// <remarks>
        /// Throw <see cref="InvalidOperationException"/> if the machine for a given <paramref name="index"/> is unknown.
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

        /// <summary>
        /// Creates a list of machine locations.
        /// </summary>
        /// <remarks>
        /// If <see cref="Settings.ResolveLocationsEagerly"/> is true, then the resulting machine location list
        /// is resolved eagerly and all the unknown machines are filter out (and the list of such machines is traced).
        /// </remarks>
        public static IReadOnlyList<MachineLocation> Create(
            Context context,
            MachineIdSet locations,
            MachineReputationTracker reputationTracker,
            ClusterState clusterState,
            ContentHash hash,
            Settings settings)
        {
            if (!settings.ResolveLocationsEagerly)
            {
                return new MachineList(locations, reputationTracker, clusterState, hash, settings);
            }

            return ResolveMachineLocations(context, locations, reputationTracker, clusterState, hash, settings);
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

                var master = _clusterState.MasterMachineId;

                resolvedMachineIds = resolvedMachineIds
                    .OrderBy(id => GetMachinePriority(_settings, _clusterState, _reputationTracker, id, master, _hash))
                    .ToList();

                _resolvedMachineIds = resolvedMachineIds;
            }
        }

        private static IReadOnlyList<MachineLocation> ResolveMachineLocations(
            Context context, MachineIdSet locations, MachineReputationTracker reputationTracker, ClusterState clusterState, ContentHash hash, Settings settings)
        {
            // Resolving the machine locations eagerly.
            var master = clusterState.MasterMachineId;

            var sortedLocations = locations.EnumerateMachineIds().OrderBy(id => GetMachinePriority(settings, clusterState, reputationTracker, id, master, hash));

            var (resolvedLocations, unresolvedLocations) = resolveMachines(clusterState, sortedLocations, locations.Count);

            // Tracing the errors if needed.
            if (unresolvedLocations?.Count > 0)
            {
                Tracer.Error(context, $"Failed to resolved the following machine Id(s): {string.Join(", ", unresolvedLocations.Select(id => id.ToString()))}");
            }

            return resolvedLocations;

            static (List<MachineLocation> resolvedMachines, List<MachineId>? unresolvedMachines) resolveMachines(
                ClusterState clusterState, IEnumerable<MachineId> machineIds, int count)
            {
                var resolved = new List<MachineLocation>(count);
                List<MachineId>? unresolvedMachines = null;
                foreach (var machineId in machineIds)
                {
                    if (clusterState.TryResolve(machineId, out var resolvedLocation))
                    {
                        resolved.Add(resolvedLocation);
                    }
                    else
                    {
                        unresolvedMachines ??= new List<MachineId>();
                        unresolvedMachines.Add(machineId);
                    }
                }

                return (resolved, unresolvedMachines);
            }
        }

        /// <summary>
        /// Gets the priority for a given <paramref name="machineId"/>.
        /// </summary>
        private static int GetMachinePriority(
            Settings settings, ClusterState clusterState, MachineReputationTracker reputationTracker, MachineId machineId, MachineId? master, ContentHash hash)
        {
            // This method won't throw/fail if the machine id is unknown.

            // Send master to the back.
            if (settings.DeprioritizeMaster && (master?.Equals(machineId) == true))
            {
                return int.MaxValue;
            }

            // Pull designated locations to front.
            if (settings.PrioritizeDesignatedLocations && clusterState.IsDesignatedLocation(machineId, hash, includeExpired: false))
            {
                return -1;
            }

            // Sort by reputation.
            // In some cases the reputation can be not available. Considering its a bad one.
            var reputation = reputationTracker.GetReputation(machineId);

            return (int)reputation;
        }
    }
}
