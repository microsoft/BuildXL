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
        /// from <see cref="Mutate{TState}"/> method.
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
        private ClusterState(ClusterStateInternal clusterStateInternal) => ClusterStateInternal = clusterStateInternal;

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
        private Result<ClusterStateInternal> Mutate<TState>(Func<ClusterStateInternal, TState, Result<ClusterStateInternal>> mutation, TState state)
        {
            using var token = _lock.AcquireWriteLock();

            var newClusterState = mutation(ClusterStateInternal.Value!, state);
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

        /// <nodoc />
        private TResult Read<TResult, TState>(Func<ClusterStateInternal, TState, TResult> action, TState state)
        {
            return action(ClusterStateInternal.Value!, state);
        }

        /// <summary>
        /// The time at which the machine was last in an inactive state
        /// </summary>
        public DateTime LastInactiveTime { get => Read(static c => c.LastInactiveTime); set => Mutate(static (c, value) => c.With(lastInactiveTime: value), value).ThrowIfFailure(); }

        /// <summary>
        /// Max known machine id.
        /// </summary>
        public int MaxMachineId { get => Read(static c => c.MaxMachineId); set => Mutate(static (c, value) => c.With(maxMachineId: value), value).ThrowIfFailure(); }

        /// <summary>
        /// The machine id representing the primary CAS instance on the machine. 
        /// In multi-drive scenarios where one CAS instance is on SSD and others are on HDD
        /// this should correspond to the SSD CAS instance.
        /// </summary>
        public MachineId PrimaryMachineId { get => Read(static c => c.PrimaryMachineId); set => Mutate(static (c, value) => c.With(primaryMachineId: value), value).ThrowIfFailure(); }

        /// <summary>
        /// Gets a list of machine ids representing unique CAS instances on the current machine
        /// </summary>
        public IReadOnlyList<MachineMapping> LocalMachineMappings => Read(static c => c.LocalMachineMappings);

        /// <nodoc />
        public bool EnableBinManagerUpdates { get => Read(static c => c.EnableBinManagerUpdates); set => Mutate(static (c, value) => c.With(enableBinManagerUpdates: value), value).ThrowIfFailure(); }

        /// <nodoc />
        public MachineId? MasterMachineId => Read(static c => c.MasterMachineId);

        /// <summary>
        /// Returns a list of inactive machines.
        /// </summary>
        public IReadOnlyList<MachineId> InactiveMachines => Read(static c => c.InactiveMachines);

        /// <summary>
        /// Returns a list of closed machines.
        /// </summary>
        public IReadOnlyList<MachineId> ClosedMachines => Read(static c => c.ClosedMachines);

        /// <nodoc />
        public IReadOnlyList<MachineLocation> Locations => Read(static c => c.Locations);

        /// <summary>
        /// Gets whether a machine is marked inactive
        /// </summary>
        public bool IsMachineMarkedInactive(MachineId machineId) => Read(static (clusterState, machineId) => clusterState.IsMachineMarkedInactive(machineId), machineId);

        /// <summary>
        /// Gets whether a machine is marked closed
        /// </summary>
        public bool IsMachineMarkedClosed(MachineId machineId) => Read(static (clusterState, machineId) => clusterState.IsMachineMarkedClosed(machineId), machineId);

        /// <nodoc />
        public BoolResult SetMachineStates(BitMachineIdSet inactiveMachines, BitMachineIdSet? closedMachines = null) => Mutate(static (clusterState, tpl) => clusterState.SetMachineStates(tpl.inactiveMachines, tpl.closedMachines), (inactiveMachines, closedMachines));

        /// <summary>
        /// Marks that a machine with <paramref name="machineId"/> is Active.
        /// </summary>
        public BoolResult MarkMachineActive(MachineId machineId) => Mutate(static (clusterState, machineId) => clusterState.MarkMachineActive(machineId), machineId);

        /// <summary>
        /// Sets max known machine id and unknown machines.
        /// </summary>
        public void AddUnknownMachines(int maxMachineId, Dictionary<MachineId, MachineLocation> unknownMachines) => Mutate(static (clusterState, tpl) => clusterState.AddUnknownMachines(tpl.maxMachineId, tpl.unknownMachines), (maxMachineId, unknownMachines)).ThrowIfFailure();

        /// <summary>
        /// Add a mapping from <paramref name="machineId"/> to <paramref name="machineLocation"/>.
        /// </summary>
        internal void AddMachine(MachineId machineId, MachineLocation machineLocation) => Mutate(static (clusterState, tpl) => clusterState.AddMachine(tpl.machineId, tpl.machineLocation), (machineId, machineLocation)).ThrowIfFailure();

        /// <summary>
        /// Tries to resolve <see cref="MachineLocation"/> by a machine id (<paramref name="machine"/>).
        /// </summary>
        public bool TryResolve(MachineId machine, out MachineLocation machineLocation)
        {
            var result = Read(static (clusterState, machine) => clusterState.TryResolve(machine), machine);
            machineLocation = result.MachineLocation;
            return result.Succeeded;
        }

        /// <summary>
        /// Tries to resolve <see cref="MachineId"/> by <paramref name="machineLocation"/>.
        /// </summary>
        public bool TryResolveMachineId(MachineLocation machineLocation, out MachineId machineId)
        {
            var result = Read(static (clusterState, machineLocation) => clusterState.TryResolveMachineId(machineLocation), machineLocation);
            machineId = result.MachineId;
            return result.Succeeded;
        }

        /// <summary>
        /// Gets a random locations excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except) => Read(static (clusterState, except) => clusterState.GetRandomMachineLocation(except), except);

        /// <summary>
        /// This should only be used from the master.
        /// </summary>
        internal Result<MachineId[][]> GetBinMappings() => Read((clusterState) => clusterState.GetBinMappings());

        /// <summary>
        /// Gets the set of locations designated for a hash. This is relevant for proactive copies and eviction.
        /// </summary>
        internal Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash, bool includeExpired) => Read(static (clusterState, tpl) => clusterState.GetDesignatedLocations(tpl.hash, tpl.includeExpired), (hash, includeExpired));

        /// <summary>
        /// Gets whether the given machine is a designated location for the hash
        /// </summary>
        internal bool IsDesignatedLocation(MachineId machineId, ContentHash hash, bool includeExpired) => Read(static (clusterState, tpl) => clusterState.IsDesignatedLocation(tpl.machineId, tpl.hash, tpl.includeExpired), ((machineId, hash, includeExpired)));

        /// <summary>
        /// Getting or setting an instance of <see cref="BinManager"/>.
        /// </summary>
        /// <remarks>
        /// The setter is called by the worker machines.
        /// </remarks>
        public BinManager? BinManager { get => Read(static c => c.BinManager); set => Mutate(static (c, value) => c.With(binManager: value), value).ThrowIfFailure(); }

        /// <summary>
        /// Initializes the BinManager if it is required.
        /// </summary>
        /// <remarks>This operation is used only by the master. The worker still may set BinManager via <see cref="BinManager"/> property.</remarks>
        internal void InitializeBinManagerIfNeeded(int locationsPerBin, IClock clock, TimeSpan expiryTime) => Mutate(static (clusterState, tpl) => clusterState.InitializeBinManagerIfNeeded(tpl.locationsPerBin, tpl.clock, tpl.expiryTime), (locationsPerBin, clock, expiryTime)).ThrowIfFailure();

        /// <nodoc />
        internal void SetMasterMachine(MachineLocation producer) => Mutate(static (clusterState, producer) => clusterState.SetMasterMachine(producer), producer).ThrowIfFailure();

        /// <nodoc />
        public int ApproximateNumberOfMachines() => Read((clusterState) => clusterState.ApproximateNumberOfMachines());
    }
}
