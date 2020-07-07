// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory
{
    /// <summary>
    /// In-memory version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class MemoryContentLocationDatabase : ContentLocationDatabase
    {
        private readonly ConcurrentDictionary<ShortHash, ContentLocationEntry> _map = new ConcurrentDictionary<ShortHash, ContentLocationEntry>();
        private readonly ConcurrentDictionary<string, string> _globalEntryMap = new ConcurrentDictionary<string, string>();

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
        public override void SetGlobalEntry(string key, string value)
        {
            if (value == null)
            {
                _globalEntryMap.TryRemove(key, out _);
            }
            else
            {
                _globalEntryMap[key] = value;
            }
        }

        /// <inheritdoc />
        public override bool TryGetGlobalEntry(string key, out string value)
        {
            return _globalEntryMap.TryGetValue(key, out value);
        }

        /// <inheritdoc />
        protected override bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            entry = GetContentLocationEntry(hash);
            return entry != null && !entry.IsMissing;
        }

        /// <inheritdoc />
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            // consider merging the values. Right now we always reconstruct the entry.
            _map.AddOrUpdate(hash, key => entry, (key, old) => entry);
        }


        /// <inheritdoc />
        public override Possible<bool> TryUpsert(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism replacement,
            Func<MetadataEntry, bool> shouldReplace)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override GetContentHashListResult GetContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            throw new NotImplementedException();
        }
        
        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override IEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(OperationContext context)
        {
            return _map.Keys.OrderBy(h => h);
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            OperationContext context,
            EnumerationFilter valueFilter,
            bool returnKeysOnly)
        {
            return _map
                // Directly calling OrderBy on ConcurrentDictionary instance is not thread-safe.
                // Making a copy by calling instance ToArray method first.
                .ToArray()
                .OrderBy(kvp => kvp.Key)
                .SkipWhile(kvp => valueFilter?.StartingPoint != null && valueFilter.StartingPoint > kvp.Key)
                .Where(kvp => {
                    if (returnKeysOnly)
                    {
                        return true;
                    }

                    return valueFilter?.ShouldEnumerate == null || valueFilter.ShouldEnumerate?.Invoke(SerializeContentLocationEntry(kvp.Value)) == true;
                })
                .Select(kvp => (kvp.Key, returnKeysOnly ? null : kvp.Value));
        }

        /// <inheritdoc />
        protected override BoolResult SaveCheckpointCore(OperationContext context, Interfaces.FileSystem.AbsolutePath checkpointDirectory)
        {
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override BoolResult RestoreCheckpointCore(OperationContext context, Interfaces.FileSystem.AbsolutePath checkpointDirectory)
        {
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public override bool IsImmutable(Interfaces.FileSystem.AbsolutePath dbFile)
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
        protected override BoolResult GarbageCollectMetadataCore(OperationContext context)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override Result<long> GetContentDatabaseSizeBytes()
        {
            throw new NotImplementedException();
        }
    }
}
