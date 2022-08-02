// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils; // Needed for .NET Standard build.

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// A factory class for creating a list of machine ids
    /// </summary>
    public static class MachineLocationResolver
    {
        private static readonly Tracer Tracer = new (nameof(MachineId));

        /// <nodoc />
        public record Settings
        {
            /// <summary>
            /// If true, then designated locations from the cluster state have higher priority and appear first in resolved machine list.
            /// </summary>
            public bool PrioritizeDesignatedLocations { get; init; }

            /// <summary>
            /// If true, the machines should be randomized first before sorting them based on reputation and designation to avoid
            /// hitting the same machines at ones during content copy.
            /// </summary>
            public bool Randomize { get; init; } = true;
        }

        /// <summary>
        /// Creates a list of machine locations.
        /// </summary>
        public static IReadOnlyList<MachineLocation> Resolve(
            Context context,
            MachineIdSet locations,
            MachineReputationTracker reputationTracker,
            ClusterState clusterState,
            ContentHash hash,
            Settings settings,
            IMasterElectionMechanism masterElectionMechanism)
        {
            var master = GetMasterMachineId(clusterState, masterElectionMechanism);

            var machineIds = locations.EnumerateMachineIds().ToList();
            if (settings.Randomize)
            {
                ThreadSafeRandom.Shuffle(machineIds);
            }

            // Resolving the machine locations eagerly.
            var sortedLocations = machineIds
                .OrderBy(machineId => GetMachinePriority(settings, clusterState, reputationTracker, machineId, master, hash));

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
        
        private static MachineId GetMasterMachineId(ClusterState clusterState, IMasterElectionMechanism masterElectionMechanism)
        {
            var masterLocation = masterElectionMechanism.Master;
            clusterState.TryResolveMachineId(masterLocation, out var master);
            return master;
        }

        /// <summary>
        /// Gets the priority for a given <paramref name="machineId"/>.
        /// </summary>
        private static int GetMachinePriority(
            Settings settings,
            ClusterState clusterState,
            MachineReputationTracker reputationTracker,
            MachineId machineId,
            MachineId master,
            ContentHash hash)
        {
            // This method won't throw/fail if the machine id is unknown.

            // Send master to the back. If there is no master, then the master's machine ID will be invalid
            if (master.IsValid() && master.Equals(machineId) == true)
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
