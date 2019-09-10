// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Store used to capture predictions on where content will be shared.
    /// </summary>
    public class RocksDbContentPlacementPredictionStore
    {
        private readonly RocksDbContentLocationDatabase _database;
        private readonly ClusterState _clusterState;

        private int _currentId = 0;
        private readonly ConcurrentDictionary<string, MachineId> _knownMachines = new ConcurrentDictionary<string, MachineId>();

        /// <nodoc />
        public RocksDbContentPlacementPredictionStore(string storeLocation, bool clean)
        {
            var absolutePath = new AbsolutePath(storeLocation);
            var config = new RocksDbContentLocationDatabaseConfiguration(absolutePath) 
            {
                StoreClusterState = true,
                CleanOnInitialize = clean,
                GarbageCollectionInterval = Timeout.InfiniteTimeSpan
            };
            _clusterState = new ClusterState();

            _database = new RocksDbContentLocationDatabase(SystemClock.Instance, config, () => new List<MachineId>());
        }

        /// <nodoc />
        public async Task<BoolResult> StartupAsync(OperationContext context)
        {
            var result = await _database.StartupAsync(context);
            _database.UpdateClusterState(context, _clusterState, false);
            return result;
        }

        /// <nodoc />
        public void StoreResult(OperationContext context, string path, List<string> machines)
        {
            foreach (var machine in machines)
            {
                _knownMachines.GetOrAdd(machine, _ =>
                    {
                        var machineId = new MachineId(Interlocked.Increment(ref _currentId));
                        _clusterState.AddMachine(machineId, new MachineLocation(machine));
                        return machineId;
                    });
            }

            var pathHash = ComputePathHash(path);

            var entry = ContentLocationEntry.Create(MachineIdSet.Empty.SetExistence(machines.SelectList(machine => _knownMachines[machine]), true), 0, UnixTime.Zero);
            _database.Store(context, pathHash, entry);
        }

        /// <nodoc />
        public IReadOnlyList<string> GetTargetMachines(OperationContext context, string path)
        {
            var pathHash = ComputePathHash(path);
            if (_database.TryGetEntry(context, pathHash, out var entry))
            {
                return entry.Locations.Select(machineId =>
                {
                    if (_clusterState.TryResolve(machineId, out var location))
                    {
                        return location.Path;
                    }

                    return null;
                }).Where(path => path != null).ToList();
            }

            return CollectionUtilities.EmptyArray<string>();
        }

        private static ShortHash ComputePathHash(string path)
        {
            var bytes = MurmurHash3.Create(Encoding.UTF8.GetBytes(path));
            var pathHash = new ShortHash(new ReadOnlyFixedBytes(bytes.ToByteArray()));
            return pathHash;
        }

        /// <nodoc />
        public bool CreateSnapshot(OperationContext context, string path)
        {
            _database.UpdateClusterState(context, _clusterState, true);
            var absolutePath = new AbsolutePath(path);
            var result = _database.SaveCheckpoint(context, absolutePath);
            return result.Succeeded;
        }
    }
}
