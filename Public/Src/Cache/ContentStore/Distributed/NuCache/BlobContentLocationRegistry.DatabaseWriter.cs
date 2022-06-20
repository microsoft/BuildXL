// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;
using RocksDbSharp;
using static BuildXL.Cache.ContentStore.Distributed.MetadataService.RocksDbOperations;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public partial class BlobContentLocationRegistry
    {
        /// <summary>
        /// Writes the full and diff entries based on the baseline and current content list
        /// </summary>
        public static BoolResult WriteSstFiles(
            OperationContext context,
            PartitionId partitionId,
            ContentListing baseline,
            ContentListing current,
            IRocksDbColumnWriter fullSstWriter,
            IRocksDbColumnWriter diffSstWriter,
            PartitionUpdateStatistics counters)
        {
            // Computes and writes per hash entries of the current listing
            // and returns the enumerator so subsequent step can consume the
            // entries
            var fullEntries = EnumerateAndWriteFullSstFile(
                fullSstWriter,
                partitionId,
                current.EnumerateEntries());

            // Compute the differences
            var diffEntries = baseline.EnumerateChanges(
                current.EnumerateEntries(),
                counters.DiffStats,
                synthesizeUniqueHashEntries: true);

            // Write the differences
            WriteDiffSstFile(
                diffSstWriter,
                diffEntries: ComputeDatabaseEntries(diffEntries),
                currentEntries: fullEntries,
                baselineEntries: ComputeDatabaseEntries(baseline.EnumerateEntries()),
                counters);

            return BoolResult.Success;
        }

        /// <summary>
        /// Computes and writes per hash entries of the current listing
        /// and returns the enumerator so subsequent step can consume the
        /// entries
        /// </summary>
        private static IEnumerator<LocationEntryBuilder> EnumerateAndWriteFullSstFile(
            IRocksDbColumnWriter writer,
            PartitionId partitionId,
            IEnumerable<MachineContentEntry> entries)
        {
            writer.DeleteLocationEntryPartitionRange(partitionId);

            foreach (var entry in ComputeDatabaseEntries(entries))
            {
                entry.Write(writer, merge: false);
                yield return entry;
            }
        }

        /// <summary>
        /// Writes the diff sst file
        /// </summary>
        private static void WriteDiffSstFile(
            IRocksDbColumnWriter writer,
            IEnumerable<LocationEntryBuilder> diffEntries,
            IEnumerator<LocationEntryBuilder> currentEntries,
            IEnumerable<LocationEntryBuilder> baselineEntries,
            PartitionUpdateStatistics counters)
        {
            // Enumerates the diff entries and writes deletions and returns the existing entries
            IEnumerable<LocationEntryBuilder> enumerateDiffEntriesAndWriteDeletions()
            {
                foreach (var item in NuCacheCollectionUtilities.DistinctMergeSorted(diffEntries.GetEnumerator(), currentEntries, e => e.Hash, e => e.Hash))
                {
                    var diffEntry = item.left;
                    var fullEntry = item.right;
                    if (item.mode == MergeMode.LeftOnly)
                    {
                        // Entry no long exists in current entries.
                        // Just put a delete
                        writer.DeleteLocationEntry(item.left.Hash);
                        counters.DiffStats.Deletes.Add(new MachineContentEntry(item.left.Hash, default, item.left.Info.Size ?? 0, default));
                    }
                    else if (item.mode == MergeMode.Both)
                    {
                        yield return diffEntry;
                    }
                    else
                    {
                        // RightOnly case not handled because this is only concerned with entries which appear in the diff.
                        // This case should probably not happen since entries are synthezized into diff for every unique hash in the
                        // current entries.
                    }
                }
            }

            // Enumerates the existing diff entries and writes minimal data to database. (i.e. size, creation time, and last access time
            // are excluded respectively if they are present in the base entry)
            foreach (var item in NuCacheCollectionUtilities.DistinctMergeSorted(enumerateDiffEntriesAndWriteDeletions(), baselineEntries, e => e.Hash, e => e.Hash))
            {
                // RightOnly case not handled because this is only concerned with entries which appear in the diff.
                if (item.mode != MergeMode.RightOnly)
                {
                    var diffEntry = item.left;
                    if (item.mode == MergeMode.Both)
                    {
                        var baselineEntry = item.right;
                        diffEntry.Info = MachineContentInfo.Diff(baseline: baselineEntry.Info, current: diffEntry.Info);
                    }

                    if (diffEntry.Entries.Count == 0)
                    {
                        // This is a touch-only entry
                        // Don't need to write size for touch-only entry
                        diffEntry.Info.Size = null;

                        if (diffEntry.Info.LatestAccessTime == null)
                        {
                            // Don't need to write touch-only entry if last access time is not set
                            continue;
                        }
                    }

                    diffEntry.Write(writer, merge: true);
                }
            }
        }

        private static IEnumerable<LocationEntryBuilder> ComputeDatabaseEntries(IEnumerable<MachineContentEntry> entries)
        {
            var group = new LocationEntryBuilder();
            foreach (var entry in entries)
            {
                if (group.HasInfo && group.Hash != entry.ShardHash)
                {
                    yield return group;
                    group.Reset();
                }

                group.Add(entry);
            }

            if (group.HasInfo)
            {
                yield return group;
            }
        }

        /// <summary>
        /// Accumulates <see cref="MachineContentEntry"/> values into a per-hash entry
        /// </summary>
        private class LocationEntryBuilder
        {
            public bool HasInfo { get; set; }
            public Buffer<LocationChange> Entries = new();
            public ShardHash Hash { get; set; } = default;
            public MachineContentInfo Info = MachineContentInfo.Default;

            public void Write(IRocksDbColumnWriter writer, bool merge)
            {
                MergeOrPutContentLocationEntry<IRocksDbColumnWriter, LocationChange>(writer,
                    Hash,
                    Entries.ItemSpan,
                    static l => l,
                    Info,
                    merge);
            }

            public void Add(MachineContentEntry entry)
            {
                HasInfo = true;
                if (Entries.Count == 0)
                {
                    Hash = entry.ShardHash;
                }

                if (!entry.Location.IsRemove)
                {
                    Info.Merge(entry);
                }

                // Need to check for valid location in this case
                // because entry may just be a synthetic entry
                // which is added for the purpose of touches
                if (entry.Location.IsValid)
                {
                    Entries.Add(entry.Location);
                }
            }

            /// <summary>
            /// Reset the entry
            /// </summary>
            public void Reset()
            {
                HasInfo = false;
                Hash = default;
                Entries.Reset();
                Info = MachineContentInfo.Default;
            }
        }
    }
}
