// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities.Benchmarks
{
    public sealed class ObjectCacheBenchmarks : XunitBuildXLTest
    {
        private const int Capacity = 4001;
        private const int BatchCount = 1024;
        private const int BatchSize = 1024;
        private const int HotKeyCount = 128;
        private const int ProjectCount = 64;
        private const int ProjectKeyCount = 256;
        private const int TailKeyCount = BatchSize - HotKeyCount - ProjectKeyCount;
        private const int TailWorkingSet = 128 * 1024;

        private readonly ITestOutputHelper m_output;

        public ObjectCacheBenchmarks(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void RunManifestStyleBenchmark()
        {
            int[][] batches = CreateBatches();
            string[] sources = CreateSources();
            int[] concurrencyLevels = { 1, 8, 32, 64 };

            m_output.WriteLine(
                $"ObjectCache benchmark: {BatchCount:N0} manifests, {BatchSize:N0} distinct keys/manifest, " +
                $"{Environment.ProcessorCount} logical processors.");

            foreach (int concurrency in concurrencyLevels)
            {
                Measure("locked TryGet+Add", new LockedTryGetAddCache(), batches, sources, concurrency);
                Measure("lock-free GetOrAdd", new LockFreeGetOrAddCache(), batches, sources, concurrency);
            }

            MeasureCounters();
        }

        private void Measure(
            string name,
            IBenchmarkCache cache,
            int[][] batches,
            string[] sources,
            int concurrency)
        {
            Run(cache, batches, sources, Math.Min(64, BatchCount), concurrency);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            long checksum = Run(cache, batches, sources, BatchCount, concurrency);
            stopwatch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            double operations = (double)BatchCount * BatchSize;

            m_output.WriteLine(
                $"{name,-22} c={concurrency,2}: {operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, {allocated / operations:F1} B/op, " +
                $"hits={cache.Hits:N0}, misses={cache.Misses:N0}, checksum={checksum:N0}");
        }

        private static long Run(
            IBenchmarkCache cache,
            int[][] batches,
            string[] sources,
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
                    int[] batch = batches[batchIndex];
                    for (int i = 0; i < batch.Length; i++)
                    {
                        localChecksum += cache.GetOrAdd(batch[i], sources).Length;
                    }

                    return localChecksum;
                },
                localChecksum => Interlocked.Add(ref checksum, localChecksum));

            return checksum;
        }

        private void MeasureCounters()
        {
            const int iterationsPerWorker = 2_000_000;
            int concurrency = Math.Min(64, Math.Max(1, Environment.ProcessorCount));

            long interlockedCounter = 0;
            var stopwatch = Stopwatch.StartNew();
            Parallel.For(
                0,
                concurrency,
                worker =>
                {
                    for (int i = 0; i < iterationsPerWorker; i++)
                    {
                        Interlocked.Increment(ref interlockedCounter);
                    }
                });
            stopwatch.Stop();
            double operations = (double)iterationsPerWorker * concurrency;
            m_output.WriteLine(
                $"Interlocked counter       c={concurrency,2}: {operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, count={interlockedCounter:N0}");

            var counters = new ApproximateCacheCounters();
            stopwatch.Restart();
            Parallel.For(
                0,
                concurrency,
                worker =>
                {
                    for (int i = 0; i < iterationsPerWorker; i++)
                    {
                        counters.RecordHit();
                    }
                });
            stopwatch.Stop();
            m_output.WriteLine(
                $"Approximate counter       c={concurrency,2}: {operations / stopwatch.Elapsed.TotalSeconds / 1_000_000:F2} Mops/s, " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms, count={counters.Hits:N0}");
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

        private static string[] CreateSources()
        {
            int count = HotKeyCount + (ProjectCount * ProjectKeyCount) + TailWorkingSet;
            var sources = new string[count];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = $"fragment-{i:D6}-{i % 97:D2}";
            }

            return sources;
        }

        private static string Expand(int key, string[] sources)
        {
            string source = sources[key];
            return string.Create(
                source.Length,
                source,
                static (destination, state) => state.AsSpan().CopyTo(destination));
        }

        private interface IBenchmarkCache
        {
            long Hits { get; }

            long Misses { get; }

            string GetOrAdd(int key, string[] sources);
        }

        private sealed class LockedTryGetAddCache : IBenchmarkCache
        {
            private readonly ObjectCache<int, string> m_cache = new ObjectCache<int, string>(Capacity);

            public long Hits => m_cache.Hits;

            public long Misses => m_cache.Misses;

            public string GetOrAdd(int key, string[] sources)
            {
                if (!m_cache.TryGetValue(key, out string value))
                {
                    value = Expand(key, sources);
                    m_cache.AddItem(key, value);
                }

                return value;
            }
        }

        private sealed class LockFreeGetOrAddCache : IBenchmarkCache
        {
            private readonly LockFreeObjectCache<int, string> m_cache = new LockFreeObjectCache<int, string>(Capacity);

            public long Hits => m_cache.Hits;

            public long Misses => m_cache.Misses;

            public string GetOrAdd(int key, string[] sources) =>
                m_cache.GetOrAdd(key, sources, static (cacheKey, state) => Expand(cacheKey, state));
        }

    }
}
