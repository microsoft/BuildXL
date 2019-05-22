// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory
{
    /// <summary>
    /// In-memory version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class MemoryContentLocationDatabase : ContentLocationDatabase
    {
        private readonly ConcurrentDictionary<ShortHash, ContentLocationEntry> _map = new ConcurrentDictionary<ShortHash, ContentLocationEntry>();

        /// <inheritdoc />
        public MemoryContentLocationDatabase(IClock clock, MemoryContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
        }

        /// <inheritdoc />
        protected override BoolResult InitializeCore(OperationContext context)
        {
            // Intentionally doing nothing.
            Tracer.Info(context, "Initializing in-memory content location database.");
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            entry = GetContentLocationEntry(hash);
            return entry != null && !entry.IsMissing;
        }

        /// <inheritdoc />
        protected override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            // consider merging the values. Right now we always reconstruct the entry.
            _map.AddOrUpdate(hash, key => entry, (key, old) => entry);
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(CancellationToken token)
        {
            return _map.Keys.OrderBy(h => h);
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            CancellationToken token,
            EnumerationFilter filter = null)
        {
            foreach (var kvp in _map)
            {
                if (filter == null || filter(Serialize(kvp.Value)))
                {
                    yield return (kvp.Key, kvp.Value);
                }
            }
        }

        /// <inheritdoc />
        protected override BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override BoolResult RestoreCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public override bool IsImmutable(AbsolutePath dbFile)
        {
            return false;
        }

        private ContentLocationEntry GetContentLocationEntry(ShortHash hash)
        {
            if (_map.TryGetValue(hash, out var entry))
            {
                return ContentLocationEntry.Create(entry.Locations, entry.ContentSize, lastAccessTimeUtc: Clock.UtcNow, creationTimeUtc: null);
            }

            return ContentLocationEntry.Missing;
        }

        /// <inheritdoc />
        protected override void UpdateClusterStateCore(OperationContext context, ClusterState clusterState, bool write)
        {
        }
    }
}
