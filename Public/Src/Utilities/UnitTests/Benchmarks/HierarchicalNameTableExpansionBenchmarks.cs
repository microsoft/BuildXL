// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities.Benchmarks
{
    public sealed class HierarchicalNameTableExpansionBenchmarks : XunitBuildXLTest
    {
        private const int BatchCount = 1024;
        private const int BatchSize = 1024;
        private const int MeasurementPasses = 16;
        private const int HotKeyCount = 128;
        private const int ProjectCount = 64;
        private const int ProjectKeyCount = 256;
        private const int TailKeyCount = BatchSize - HotKeyCount - ProjectKeyCount;
        private const int TailWorkingSet = 128 * 1024;

        private readonly ITestOutputHelper m_output;

        public HierarchicalNameTableExpansionBenchmarks(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void RunHierarchicalNameTableExpansionBenchmark()
        {
            int[][] batches = CreateBatches();
            int sourceCount = HotKeyCount + (ProjectCount * ProjectKeyCount) + TailWorkingSet;
            var ids = new HierarchicalNameId[sourceCount];
            var table = new HierarchicalNameTable(new StringTable(), ignoreCase: true, Path.DirectorySeparatorChar);

            string root = OperatingSystemHelper.IsUnixOS ? "/benchmark" : @"c:\benchmark";
            var setupStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = AddName(
                    table,
                    Path.Combine(
                        root,
                        $"project-{i % ProjectCount:D2}",
                        $"directory-{i % 997:D3}",
                        $"file-{i:D6}.obj"));
            }

            table.Freeze();
            setupStopwatch.Stop();

            m_output.WriteLine(
                $"HierarchicalNameTable benchmark: {BatchCount:N0} manifests x {MeasurementPasses} passes, " +
                $"{BatchSize:N0} keys/manifest, {ids.Length:N0} names, " +
                $"{Environment.ProcessorCount} logical processors, setup={setupStopwatch.ElapsedMilliseconds:N0} ms.");

            foreach (int concurrency in new[] { 1, 8, 32, 64 })
            {
                Run(table, ids, batches, Math.Min(64, BatchCount), concurrency);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                long hitsBefore = table.CacheHits;
                long missesBefore = table.CacheMisses;
                var stopwatch = Stopwatch.StartNew();
                long checksum = Run(table, ids, batches, BatchCount * MeasurementPasses, concurrency);
                stopwatch.Stop();

                long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                long hits = table.CacheHits - hitsBefore;
                long misses = table.CacheMisses - missesBefore;
                double operations = (double)BatchCount * BatchSize * MeasurementPasses;

                m_output.WriteLine(
                    $"HierarchicalNameTable.ExpandName c={concurrency,2}: " +
                    $"{operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                    $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, {allocated / operations:F1} B/op, " +
                    $"hits={hits:N0}, misses={misses:N0}, checksum={checksum:N0}");
            }
        }

        private static long Run(
            HierarchicalNameTable table,
            HierarchicalNameId[] ids,
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
                        localChecksum += table.ExpandName(ids[batch[i]]).Length;
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

        private static HierarchicalNameId AddName(HierarchicalNameTable table, string name)
        {
            string[] components = name.Split(
                new[] { Path.DirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            int rootOffset = OperatingSystemHelper.IsUnixOS ? 1 : 0;
            var componentIds = new StringId[components.Length + rootOffset];
            if (rootOffset != 0)
            {
                componentIds[0] = table.StringTable.AddString(HierarchicalNameTable.UnixPathRootSentinel);
            }

            for (int i = 0; i < components.Length; i++)
            {
                componentIds[i + rootOffset] = table.StringTable.AddString(components[i]);
            }

            return table.AddComponents(HierarchicalNameId.Invalid, componentIds);
        }
    }
}
