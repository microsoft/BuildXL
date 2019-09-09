// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;

namespace cptools.ml.offlineMapping
{
    class RocksDbBackingStore
    {
        private readonly RocksDbContentLocationDatabase m_database;
        private readonly ClusterState m_clusterState;

        private int _currentId = 0;
        private readonly ConcurrentDictionary<string, MachineId> m_knownMachines = new ConcurrentDictionary<string, MachineId>();

        public RocksDbBackingStore(string storeLocation)
        {
            var absolutePath = new AbsolutePath(storeLocation);
            var config = new RocksDbContentLocationDatabaseConfiguration(absolutePath)
            {
                StoreClusterState = true
            };
            m_clusterState = new ClusterState();

            m_database = new RocksDbContentLocationDatabase(SystemClock.Instance, config, () => new List<MachineId>());
        }

        public async Task<bool> StartupAsync(ILogger logger)
        {
            var context = new Context(logger);
            var result = await m_database.StartupAsync(context);
            return result.Succeeded;
        }

        public void StoreResult(ILogger logger, string path, List<string> machines)
        {
            foreach (var machine in machines)
            {
                m_knownMachines.GetOrAdd(machine, _ =>
                    {
                        var machineId = new MachineId(Interlocked.Increment(ref _currentId));
                        m_clusterState.AddMachine(machineId, new MachineLocation(machine));
                        return machineId;
                    });
            }

            var context = new Context(logger);
            var operationContext = new OperationContext(context);
            m_database.UpdateClusterState(operationContext, m_clusterState, true);

            var bytes = MurmurHash3.Create(Encoding.UTF8.GetBytes(path));
            var pathHash = new ShortHash(new ReadOnlyFixedBytes(bytes.ToByteArray()));

            foreach (var machine in machines)
            {
                m_database.LocationAdded(operationContext, pathHash, m_knownMachines[machine], 0);
            }
        }

        public bool CreateSnapshot(ILogger logger, string path)
        {
            var context = new Context(logger);
            var operationContext = new OperationContext(context);
            var absolutePath = new AbsolutePath(path);
            var result = m_database.SaveCheckpoint(operationContext, absolutePath);
            return result.Succeeded;
        }
    }
}
