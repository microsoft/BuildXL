// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities.Benchmarks
{
    public sealed class DirectoryContentsCacheBenchmarks : XunitBuildXLTest
    {
        private const int BatchCount = 1024;
        private const int BatchSize = 1024;
        private const int MeasurementPasses = 16;
        private const int HotKeyCount = 128;
        private const int ProjectCount = 64;
        private const int ProjectKeyCount = 256;
        private const int TailKeyCount = BatchSize - HotKeyCount - ProjectKeyCount;
        private const int TailWorkingSet = 128 * 1024;
        private const int CacheCapacity = 100000;

        private static readonly object s_result = new object();

        private readonly ITestOutputHelper m_output;

        public DirectoryContentsCacheBenchmarks(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void RunDirectoryContentsCacheBenchmark()
        {
            int[][] batches = CreateBatches();
            int sourceCount = HotKeyCount + (ProjectCount * ProjectKeyCount) + TailWorkingSet;
            var paths = new AbsolutePath[sourceCount];
            var pathTable = new PathTable();
            string root = OperatingSystemHelper.IsUnixOS ? "/benchmark" : @"c:\benchmark";

            var setupStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = AbsolutePath.Create(
                    pathTable,
                    Path.Combine(
                        root,
                        $"project-{i % ProjectCount:D2}",
                        $"directory-{i:D6}"));
            }

            pathTable.Freeze();
            setupStopwatch.Stop();

            m_output.WriteLine(
                $"Directory contents cache benchmark: {BatchCount:N0} manifests x {MeasurementPasses} passes, " +
                $"{BatchSize:N0} keys/manifest, {paths.Length:N0} paths, capacity={CacheCapacity:N0}, " +
                $"{Environment.ProcessorCount} logical processors, setup={setupStopwatch.ElapsedMilliseconds:N0} ms.");

            foreach (int concurrency in new[] { 1, 8, 32, 64 })
            {
                RunCurrent(paths, batches, concurrency);
                RunVersioned(paths, batches, concurrency);
            }
        }

        private void RunCurrent(AbsolutePath[] paths, int[][] batches, int concurrency)
        {
            var cache = new ObjectCache<AbsolutePath, Lazy<object>>(CacheCapacity);
            RunCurrent(cache, paths, batches, Math.Min(64, BatchCount), concurrency);
            Measure(
                "Directory contents ObjectCache",
                concurrency,
                () => cache.Hits,
                () => cache.Misses,
                () => RunCurrent(cache, paths, batches, BatchCount * MeasurementPasses, concurrency));
        }

        private void RunVersioned(AbsolutePath[] paths, int[][] batches, int concurrency)
        {
            var cache = new VersionedDirectoryContentsCache(CacheCapacity);
            RunVersioned(cache, paths, batches, Math.Min(64, BatchCount), concurrency);
            Measure(
                "Directory contents versioned",
                concurrency,
                () => cache.Hits,
                () => cache.Misses,
                () => RunVersioned(cache, paths, batches, BatchCount * MeasurementPasses, concurrency));
        }

        private void Measure(
            string name,
            int concurrency,
            Func<long> getHits,
            Func<long> getMisses,
            Func<long> run)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            long hitsBefore = getHits();
            long missesBefore = getMisses();
            var stopwatch = Stopwatch.StartNew();
            long checksum = run();
            stopwatch.Stop();

            double operations = (double)BatchCount * BatchSize * MeasurementPasses;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            m_output.WriteLine(
                $"{name} c={concurrency,2}: " +
                $"{operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, {allocated / operations:F1} B/op, " +
                $"hits={getHits() - hitsBefore:N0}, misses={getMisses() - missesBefore:N0}, checksum={checksum:N0}");
        }

        private static long RunCurrent(
            ObjectCache<AbsolutePath, Lazy<object>> cache,
            AbsolutePath[] paths,
            int[][] batches,
            int batchCount,
            int concurrency)
        {
            long checksum = 0;
            Parallel.For(
                0,
                batchCount,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                () => 0L,
                (batchIndex, _, localChecksum) =>
                {
                    int[] batch = batches[batchIndex % batches.Length];
                    for (int i = 0; i < batch.Length; i++)
                    {
                        AbsolutePath path = paths[batch[i]];
                        if (!cache.TryGetValue(path, out Lazy<object> contents))
                        {
                            contents = new Lazy<object>(() => s_result);
                            cache.AddItem(path, contents);
                        }

                        localChecksum += ReferenceEquals(contents.Value, s_result) ? 1 : 0;
                    }

                    return localChecksum;
                },
                localChecksum => Interlocked.Add(ref checksum, localChecksum));

            return checksum;
        }

        private static long RunVersioned(
            VersionedDirectoryContentsCache cache,
            AbsolutePath[] paths,
            int[][] batches,
            int batchCount,
            int concurrency)
        {
            long checksum = 0;
            Parallel.For(
                0,
                batchCount,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                () => 0L,
                (batchIndex, _, localChecksum) =>
                {
                    int[] batch = batches[batchIndex % batches.Length];
                    for (int i = 0; i < batch.Length; i++)
                    {
                        AbsolutePath path = paths[batch[i]];
                        if (!cache.TryGetValue(path, out Lazy<object> contents))
                        {
                            contents = new Lazy<object>(() => s_result);
                            cache.AddItem(path, contents);
                        }

                        localChecksum += ReferenceEquals(contents.Value, s_result) ? 1 : 0;
                    }

                    return localChecksum;
                },
                localChecksum => Interlocked.Add(ref checksum, localChecksum));

            return checksum;
        }

        private static int[][] CreateBatches()
        {
            var batches = new int[BatchCount][];
            int projectKeyBase = HotKeyCount;
            int tailKeyBase = projectKeyBase + (ProjectCount * ProjectKeyCount);

            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                var batch = new int[BatchSize];
                int index = 0;
                for (int key = 0; key < HotKeyCount; key++)
                {
                    batch[index++] = key;
                }

                int projectBase = projectKeyBase + ((batchIndex % ProjectCount) * ProjectKeyCount);
                for (int key = 0; key < ProjectKeyCount; key++)
                {
                    batch[index++] = projectBase + key;
                }

                int tailBase = (batchIndex * TailKeyCount) % TailWorkingSet;
                for (int key = 0; key < TailKeyCount; key++)
                {
                    batch[index++] = tailKeyBase + ((tailBase + key) % TailWorkingSet);
                }

                var random = new Random(batchIndex);
                for (int i = batch.Length - 1; i > 0; i--)
                {
                    int swapIndex = random.Next(i + 1);
                    (batch[i], batch[swapIndex]) = (batch[swapIndex], batch[i]);
                }

                batches[batchIndex] = batch;
            }

            return batches;
        }

        private sealed class VersionedDirectoryContentsCache
        {
            private struct Entry
            {
                public int Version;
                public int KeyValue;
                public Lazy<object> Value;
            }

            private readonly Entry[] m_slots;
            private readonly ApproximateCacheCounters m_counters = new ApproximateCacheCounters();

            public VersionedDirectoryContentsCache(int capacity)
            {
                m_slots = new Entry[capacity];
            }

            public long Hits => m_counters.Hits;

            public long Misses => m_counters.Misses;

            public bool TryGetValue(AbsolutePath key, out Lazy<object> value)
            {
                GetIndexes(key.RawValue, out uint primaryIndex, out uint backupIndex);
                if (TryGetValue(key.RawValue, primaryIndex, out value)
                    || TryGetValue(key.RawValue, backupIndex, out value))
                {
                    m_counters.RecordHit();
                    return true;
                }

                m_counters.RecordMiss();
                return false;
            }

            public void AddItem(AbsolutePath key, Lazy<object> value)
            {
                GetIndexes(key.RawValue, out uint primaryIndex, out uint backupIndex);
                TrySetEntry(primaryIndex, key.RawValue, value);
                TrySetEntry(backupIndex, key.RawValue, value);
            }

            private bool TryGetValue(int keyValue, uint index, out Lazy<object> value)
            {
                ref Entry entry = ref m_slots[index];
                int initialVersion = Volatile.Read(ref entry.Version);
                if ((initialVersion & 1) != 0)
                {
                    value = null;
                    return false;
                }

                int candidateKey = Volatile.Read(ref entry.KeyValue);
                var candidate = Volatile.Read(ref entry.Value);
                int finalVersion = Volatile.Read(ref entry.Version);
                if (initialVersion == finalVersion && candidateKey == keyValue && candidate != null)
                {
                    value = candidate;
                    return true;
                }

                value = null;
                return false;
            }

            private void TrySetEntry(uint index, int keyValue, Lazy<object> value)
            {
                ref Entry entry = ref m_slots[index];
                int stableVersion = Volatile.Read(ref entry.Version);
                if ((stableVersion & 1) != 0)
                {
                    return;
                }

                int writeVersion = unchecked(stableVersion + 1);
                if (Interlocked.CompareExchange(ref entry.Version, writeVersion, stableVersion) != stableVersion)
                {
                    return;
                }

                Volatile.Write(ref entry.KeyValue, keyValue);
                Volatile.Write(ref entry.Value, value);
                Volatile.Write(ref entry.Version, unchecked(stableVersion + 2));
            }

            private void GetIndexes(int keyValue, out uint primaryIndex, out uint backupIndex)
            {
                unchecked
                {
                    int primaryHashCode = keyValue == 0 ? int.MaxValue : keyValue;
                    primaryIndex = (uint)primaryHashCode % (uint)m_slots.Length;

                    int backupHashCode = HashCodeHelper.Combine(primaryHashCode, 17);
                    if (backupHashCode == 0)
                    {
                        backupHashCode = int.MaxValue;
                    }

                    backupIndex = (uint)backupHashCode % (uint)m_slots.Length;
                }
            }
        }
    }
}
