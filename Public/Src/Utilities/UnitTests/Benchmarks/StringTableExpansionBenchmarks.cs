// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities.Benchmarks
{
    public sealed class StringTableExpansionBenchmarks : XunitBuildXLTest
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

        public StringTableExpansionBenchmarks(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void RunStringTableExpansionBenchmark()
        {
            int[][] batches = CreateBatches();
            int sourceCount = HotKeyCount + (ProjectCount * ProjectKeyCount) + TailWorkingSet;
            var ids = new StringId[sourceCount];
            var stringTable = new StringTable(sourceCount);

            var setupStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = stringTable.AddString($"fragment-{i:D6}-{i % 97:D2}");
            }

            stringTable.Freeze();
            setupStopwatch.Stop();

            m_output.WriteLine(
                $"StringTable benchmark: {BatchCount:N0} manifests x {MeasurementPasses} passes, " +
                $"{BatchSize:N0} distinct keys/manifest, {ids.Length:N0} interned strings, " +
                $"{Environment.ProcessorCount} logical processors, setup={setupStopwatch.ElapsedMilliseconds:N0} ms.");

            foreach (int concurrency in new[] { 1, 8, 32, 64 })
            {
                Run(stringTable, ids, batches, Math.Min(64, BatchCount), concurrency);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                long hitsBefore = stringTable.CacheHits;
                long missesBefore = stringTable.CacheMisses;
                var stopwatch = Stopwatch.StartNew();
                long checksum = Run(stringTable, ids, batches, BatchCount * MeasurementPasses, concurrency);
                stopwatch.Stop();

                long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                long hits = stringTable.CacheHits - hitsBefore;
                long misses = stringTable.CacheMisses - missesBefore;
                double operations = (double)BatchCount * BatchSize * MeasurementPasses;

                m_output.WriteLine(
                    $"StringTable.GetString c={concurrency,2}: " +
                    $"{operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                    $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, {allocated / operations:F1} B/op, " +
                    $"hits={hits:N0}, misses={misses:N0}, checksum={checksum:N0}");
            }
        }

        private static long Run(
            StringTable stringTable,
            StringId[] ids,
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
                        localChecksum += stringTable.GetString(ids[batch[i]]).Length;
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
    }
}
