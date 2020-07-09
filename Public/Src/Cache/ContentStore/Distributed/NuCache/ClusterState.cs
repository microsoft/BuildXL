// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Utilities.Threading;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{

    /// <summary>
    /// State of all known machines in the stamp
    /// </summary>
    public sealed class ClusterState
    {
        /// <summary>
        /// The minimum valid machine id.
        /// NOTE: This value is implied from the logic in GetOrAddMachine.lua. 
        /// </summary>
        public const int MinValidMachineId = 1;

        /// <summary>
        /// The de facto invalid machine id.
        /// NOTE: This value is implied from the logic in GetOrAddMachine.lua. 
        /// </summary>
        public const int InvalidMachineId = 0;

        /// <summary>
        /// A current cluster state value.
        /// </summary>
        /// <remarks>
        /// Keeping the result of cluster state and not the cluster state instance itself to avoid extra allocations
        /// from <see cref="Mutate"/> method.
        /// That method returns a result of cluster state and creating a wrapper each time the method is called
        /// generated hundreds of megabytes of traffic a minute.
        ///
        /// Intentionally using a property here instead of using a field for thread safety reasons:
        /// <see cref="Read{T}"/> method uses this property value in a given callback and passes a copy of the
        /// object reference.
        /// </remarks>
        private Result<ClusterStateInternal> ClusterStateInternal { get; set; }

        private readonly ReadWriteLock _lock = ReadWriteLock.Create();

        /// <nodoc />
        private ClusterState(ClusterStateInternal clusterStateInternal)
        {
            ClusterStateInternal = clusterStateInternal;
        }

        /// <nodoc />
        public ClusterState(MachineId primaryMachineId, IReadOnlyList<MachineMapping> localMachineMappings)
            : this(new ClusterStateInternal(primaryMachineId, localMachineMappings))
        {
        }

        /// <summary>
        /// Create an empty cluster state only suitable for testing purposes.
        /// </summary>
        public static ClusterState CreateForTest()
        {
            return new ClusterState(default(MachineId), Array.Empty<MachineMapping>());
        }

        /// <summary>
        /// Mutates the current cluster state value and returns the new one produced by the callback.
        /// </summary>
        /// <remarks>
        /// The cluster state field is changed only when the callback provides a successful result.
        /// </remarks>
        private Result<ClusterStateInternal> Mutate(Func<ClusterStateInternal, Result<ClusterStateInternal>> mutation)
        {
            using var token = _lock.AcquireWriteLock();

            var newClusterState = mutation(ClusterStateInternal.Value!);
            if (!newClusterState.Succeeded)
            {
                return newClusterState;
            }

            ClusterStateInternal = newClusterState;
            return ClusterStateInternal;
        }

        /// <nodoc />
        private T Read<T>(Func<ClusterStateInternal, T> action)
        {
            return action(ClusterStateInternal.Value!);
        }

        /// <summary>
        /// The time at which the machine was last in an inactive state
        /// </summary>
        public DateTime LastInactiveTime { get => Read(c => c.LastInactiveTime); set => Mutate(c => c.With(lastInactiveTime: value)).ThrowIfFailure(); }

        /// <summary>
        /// Max known machine id.
        /// </summary>
        public int MaxMachineId { get => Read(c => c.MaxMachineId); set => Mutate(c => c.With(maxMachineId: value)).ThrowIfFailure(); }

        /// <summary>
        /// The machine id representing the primary CAS instance on the machine. 
        /// In multi-drive scenarios where one CAS instance is on SSD and others are on HDD
        /// this should correspond to the SSD CAS instance.
        /// </summary>
        public MachineId PrimaryMachineId { get => Read(c => c.PrimaryMachineId); set => Mutate(c => c.With(primaryMachineId: value)).ThrowIfFailure(); }

        /// <summary>
        /// Gets a list of machine ids representing unique CAS instances on the current machine
        /// </summary>
        public IReadOnlyList<MachineMapping> LocalMachineMappings => Read(c => c.LocalMachineMappings);

        /// <nodoc />
        public BinManager? BinManager { get => Read(c => c.BinManager); set => Mutate(c => c.With(binManager: value)).ThrowIfFailure(); }

        /// <nodoc />
        public bool EnableBinManagerUpdates { get => Read(c => c.EnableBinManagerUpdates); set => Mutate(c => c.With(enableBinManagerUpdates: value)).ThrowIfFailure(); }

        /// <nodoc />
        public MachineId? MasterMachineId => Read(c => c.MasterMachineId);

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachines => Read(c => c.InactiveMachines);

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        public IReadOnlyList<MachineId> ClosedMachines => Read(c => c.ClosedMachines);

        /// <summary>
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId) => Read((clusterState) => clusterState.IsMachineMarkedInactive(machineId));

        /// <summary>
        /// Gets whether a machine is marked closed
        /// </summary>
        public bool IsMachineMarkedClosed(MachineId machineId) => Read((clusterState) => clusterState.IsMachineMarkedClosed(machineId));

        /// <nodoc />
        public BoolResult SetMachineStates(BitMachineIdSet inactiveMachines, BitMachineIdSet? closedMachines = null) => Mutate((clusterState) => clusterState.SetMachineStates(inactiveMachines, closedMachines));

        /// <summary>
        /// Marks that a machine with <paramref name="machineId"/> is Active.
        /// </summary>
        public BoolResult MarkMachineActive(MachineId machineId) => Mutate((clusterState) => clusterState.MarkMachineActive(machineId));

        /// <summary>
        /// Sets max known machine id and unknown machines.
        /// </summary>
        public void AddUnknownMachines(int maxMachineId, Dictionary<MachineId, MachineLocation> unknownMachines) => Mutate((clusterState) => clusterState.AddUnknownMachines(maxMachineId, unknownMachines)).ThrowIfFailure();

        /// <summary>
        /// Add a mapping from <paramref name="machineId"/> to <paramref name="machineLocation"/>.
        /// </summary>
        internal void AddMachine(MachineId machineId, MachineLocation machineLocation) => Mutate((clusterState) => clusterState.AddMachine(machineId, machineLocation)).ThrowIfFailure();

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public bool TryResolve(MachineId machine, out MachineLocation machineLocation)
        {
            var result = Read((clusterState) => clusterState.TryResolve(machine));
            machineLocation = result.MachineLocation;
            return result.Succeeded;
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineId"/> by <paramref name="machineLocation"/>.
        /// </summary>
        public bool TryResolveMachineId(MachineLocation machineLocation, out MachineId machineId)
        {
            var result = Read((clusterState) => clusterState.TryResolveMachineId(machineLocation));
            machineId = result.MachineId;
            return result.Succeeded;
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except) => Read((clusterState) => clusterState.GetRandomMachineLocation(except));

        /// <summary>
        /// This should only be used from the master.
        /// </summary>
        internal Result<MachineId[][]> GetBinMappings() => Read((clusterState) => clusterState.GetBinMappings());

        /// <summary>
        /// Gets the set of locations designated for a hash. This is relevant for proactive copies and eviction.
        /// </summary>
        internal Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash, bool includeExpired) => Read((clusterState) => clusterState.GetDesignatedLocations(hash, includeExpired));

        /// <summary>
        /// Gets whether the given machine is a designated location for the hash
        /// </summary>
        internal bool IsDesignatedLocation(MachineId machineId, ContentHash hash, bool includeExpired) => Read((clusterState) => clusterState.IsDesignatedLocation(machineId, hash, includeExpired));

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        internal void InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime) => Mutate((clusterState) => clusterState.InitializeBinManagerIfNeeded(locationsPerBin, clock, expiryTime)).ThrowIfFailure();

        /// <nodoc />
        internal void SetMasterMachine(MachineLocation producer) => Mutate((clusterState) => clusterState.SetMasterMachine(producer)).ThrowIfFailure();

        /// <nodoc />
        public int ApproximateNumberOfMachines() => Read((clusterState) => clusterState.ApproximateNumberOfMachines());
    }
}
