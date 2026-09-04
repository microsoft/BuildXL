// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Processes.Benchmarks
{
    /// <summary>
    /// Compares production FAM lookup paths before and after the mutable tree is replaced by its serialized form.
    /// Run with: bxl Test.BuildXL.Processes.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet8 /server-
    /// </summary>
    public sealed class FileAccessManifestLookupBenchmarks
    {
        private const int ProjectCount = 20_000;
        private const int FilesPerProject = 8;
        private const int QueriesPerProject = 5;
        private const int MeasurementPasses = 16;
        private const int MeasurementRounds = 3;

        private readonly ITestOutputHelper m_output;

        public FileAccessManifestLookupBenchmarks(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void CompareMutableAndSealedTreeLookups()
        {
            var pathTable = new PathTable();
            BenchmarkData data = CreateData(pathTable);

            PreparedManifest mutable = PrepareManifest(pathTable, data.Root, data.ManifestPaths, seal: false);
            PreparedManifest sealedManifest = PrepareManifest(pathTable, data.Root, data.ManifestPaths, seal: true);

            Assert.False(mutable.Manifest.IsManifestTreeBlockSealed);
            Assert.True(sealedManifest.Manifest.IsManifestTreeBlockSealed);
            Assert.Equal(mutable.TreeSize, sealedManifest.TreeSize);
            ValidateEquivalent(mutable.Manifest, sealedManifest.Manifest, data.Queries);

            m_output.WriteLine(
                $"FAM lookup benchmark: {ProjectCount:N0} projects, {data.ManifestPaths.Length:N0} declared paths, " +
                $"{data.Queries.Length:N0} mixed queries/pass, {MeasurementPasses:N0} passes/round, " +
                $"{MeasurementRounds:N0} rounds, {Environment.ProcessorCount:N0} logical processors.");
            PrintPreparation("control mutable tree", mutable);
            PrintPreparation("proposed sealed tree", sealedManifest);

            int maximumConcurrency = Math.Min(16, Math.Max(1, Environment.ProcessorCount));
            int[] concurrencyLevels = maximumConcurrency == 1 ? new[] { 1 } : new[] { 1, maximumConcurrency };
            foreach (int concurrency in concurrencyLevels)
            {
                RunQueries(mutable.Manifest, data.Queries, Math.Max(2, concurrency), concurrency);
                RunQueries(sealedManifest.Manifest, data.Queries, Math.Max(2, concurrency), concurrency);
                MeasureComparison(mutable.Manifest, sealedManifest.Manifest, data.Queries, concurrency);
            }

            GC.KeepAlive(mutable.Manifest);
            GC.KeepAlive(sealedManifest.Manifest);
        }

        private void MeasureComparison(
            FileAccessManifest mutable,
            FileAccessManifest sealedManifest,
            AbsolutePath[] queries,
            int concurrency)
        {
            var mutableMeasurements = new List<Measurement>(MeasurementRounds);
            var sealedMeasurements = new List<Measurement>(MeasurementRounds);

            for (int round = 0; round < MeasurementRounds; round++)
            {
                if ((round & 1) == 0)
                {
                    mutableMeasurements.Add(Measure(mutable, queries, concurrency));
                    sealedMeasurements.Add(Measure(sealedManifest, queries, concurrency));
                }
                else
                {
                    sealedMeasurements.Add(Measure(sealedManifest, queries, concurrency));
                    mutableMeasurements.Add(Measure(mutable, queries, concurrency));
                }

                Measurement mutableMeasurement = mutableMeasurements[round];
                Measurement sealedMeasurement = sealedMeasurements[round];
                Assert.Equal(mutableMeasurement.Checksum, sealedMeasurement.Checksum);
                PrintMeasurement("control mutable", concurrency, round + 1, mutableMeasurement);
                PrintMeasurement("proposed sealed", concurrency, round + 1, sealedMeasurement);
            }

            Measurement mutableAggregate = Aggregate(mutableMeasurements);
            Measurement sealedAggregate = Aggregate(sealedMeasurements);
            double throughputRatio = sealedAggregate.QueriesPerSecond / mutableAggregate.QueriesPerSecond;
            double latencyRatio = sealedAggregate.NanosecondsPerQuery / mutableAggregate.NanosecondsPerQuery;
            double allocationDelta = sealedAggregate.BytesPerQuery - mutableAggregate.BytesPerQuery;

            m_output.WriteLine(
                $"SUMMARY c={concurrency,2}: sealed throughput={throughputRatio:P1} of control, " +
                $"latency={latencyRatio:F2}x control, allocation delta={allocationDelta:F1} B/query.");
        }

        private static Measurement Measure(FileAccessManifest manifest, AbsolutePath[] queries, int concurrency)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            long checksum = RunQueries(manifest, queries, MeasurementPasses, concurrency);
            stopwatch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            return new Measurement(
                queryCount: (long)queries.Length * MeasurementPasses,
                elapsed: stopwatch.Elapsed,
                allocatedBytes: allocated,
                checksum: checksum);
        }

        private static long RunQueries(
            FileAccessManifest manifest,
            AbsolutePath[] queries,
            int passes,
            int concurrency)
        {
            if (concurrency == 1)
            {
                long checksum = 0;
                for (int pass = 0; pass < passes; pass++)
                {
                    checksum = unchecked(checksum + RunQueryPass(manifest, queries));
                }

                return checksum;
            }

            long parallelChecksum = 0;
            Parallel.For(
                0,
                passes,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                () => 0L,
                (_, _, localChecksum) => unchecked(localChecksum + RunQueryPass(manifest, queries)),
                localChecksum => Interlocked.Add(ref parallelChecksum, localChecksum));

            return parallelChecksum;
        }

        private static long RunQueryPass(FileAccessManifest manifest, AbsolutePath[] queries)
        {
            long checksum = 0;
            for (int i = 0; i < queries.Length; i++)
            {
                bool found = manifest.TryFindManifestPathFor(
                    queries[i],
                    out AbsolutePath manifestPath,
                    out FileAccessPolicy policy);
                checksum = unchecked(checksum + (found ? 17 : 31) + manifestPath.GetHashCode() + (int)policy);
            }

            return checksum;
        }

        private static void ValidateEquivalent(
            FileAccessManifest mutable,
            FileAccessManifest sealedManifest,
            AbsolutePath[] queries)
        {
            for (int i = 0; i < queries.Length; i++)
            {
                bool mutableFound = mutable.TryFindManifestPathFor(
                    queries[i],
                    out AbsolutePath mutablePath,
                    out FileAccessPolicy mutablePolicy);
                bool sealedFound = sealedManifest.TryFindManifestPathFor(
                    queries[i],
                    out AbsolutePath sealedPath,
                    out FileAccessPolicy sealedPolicy);

                Assert.Equal(mutableFound, sealedFound);
                Assert.Equal(mutablePath, sealedPath);
                Assert.Equal(mutablePolicy, sealedPolicy);
            }
        }

        private static PreparedManifest PrepareManifest(
            PathTable pathTable,
            AbsolutePath root,
            AbsolutePath[] paths,
            bool seal)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            var manifest = new FileAccessManifest(pathTable);
            manifest.AddScope(root, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowRead);
            manifest.AddPaths(paths, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);

            int treeSize;
            if (seal)
            {
                using var stream = new MemoryStream();
                manifest.Serialize(stream);
                treeSize = manifest.GetManifestTreeBytes().Length;
            }
            else
            {
                treeSize = manifest.GetManifestTreeBytes().Length;
            }

            stopwatch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            return new PreparedManifest(manifest, treeSize, stopwatch.Elapsed, allocated);
        }

        private static BenchmarkData CreateData(PathTable pathTable)
        {
            string rootString = OperatingSystemHelper.IsUnixOS ? "/fam-benchmark/repo" : @"C:\fam-benchmark\repo";
            AbsolutePath root = AbsolutePath.Create(pathTable, rootString);
            var manifestPaths = new AbsolutePath[ProjectCount * FilesPerProject];
            var queries = new AbsolutePath[ProjectCount * QueriesPerProject];

            for (int project = 0; project < ProjectCount; project++)
            {
                string projectDirectory = Path.Combine(
                    rootString,
                    $"shard-{project % 64:D2}",
                    $"project-{project:D5}",
                    "obj",
                    "release");

                int pathBase = project * FilesPerProject;
                for (int file = 0; file < FilesPerProject; file++)
                {
                    manifestPaths[pathBase + file] = AbsolutePath.Create(
                        pathTable,
                        Path.Combine(projectDirectory, $"output-{project:D5}-{file:D2}.dll"));
                }

                int queryBase = project * QueriesPerProject;
                queries[queryBase] = manifestPaths[pathBase];
                queries[queryBase + 1] = manifestPaths[pathBase + FilesPerProject - 1];
                queries[queryBase + 2] = AbsolutePath.Create(
                    pathTable,
                    Path.Combine(projectDirectory, $"missing-{project:D5}.dll"));
                queries[queryBase + 3] = AbsolutePath.Create(
                    pathTable,
                    Path.Combine(projectDirectory, "generated", $"deep-missing-{project:D5}.obj"));
                queries[queryBase + 4] = OperatingSystemHelper.IsUnixOS
                    ? manifestPaths[pathBase + 1]
                    : AbsolutePath.Create(pathTable, manifestPaths[pathBase + 1].ToString(pathTable).ToUpperInvariant());
            }

            Shuffle(queries);
            return new BenchmarkData(root, manifestPaths, queries);
        }

        private static void Shuffle(AbsolutePath[] paths)
        {
            var random = new Random(0x5EED);
            for (int i = paths.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (paths[i], paths[swapIndex]) = (paths[swapIndex], paths[i]);
            }
        }

        private void PrintPreparation(string name, PreparedManifest prepared)
        {
            m_output.WriteLine(
                $"{name,-22}: {prepared.Elapsed.TotalMilliseconds:N0} ms, " +
                $"{prepared.AllocatedBytes / (1024.0 * 1024.0):N1} MiB allocated, " +
                $"{prepared.TreeSize / (1024.0 * 1024.0):N1} MiB serialized tree.");
        }

        private void PrintMeasurement(string name, int concurrency, int round, Measurement measurement)
        {
            m_output.WriteLine(
                $"{name,-22} c={concurrency,2} round={round}: " +
                $"{measurement.QueriesPerSecond / 1_000_000.0:F2} Mqueries/s, " +
                $"{measurement.NanosecondsPerQuery:F1} ns/query, " +
                $"{measurement.BytesPerQuery:F1} B/query, checksum={measurement.Checksum:N0}.");
        }

        private static Measurement Aggregate(IReadOnlyList<Measurement> measurements)
        {
            return new Measurement(
                queryCount: measurements.Sum(measurement => measurement.QueryCount),
                elapsed: TimeSpan.FromTicks(measurements.Sum(measurement => measurement.Elapsed.Ticks)),
                allocatedBytes: measurements.Sum(measurement => measurement.AllocatedBytes),
                checksum: measurements.Sum(measurement => measurement.Checksum));
        }

        private readonly struct BenchmarkData
        {
            public AbsolutePath Root { get; }
            public AbsolutePath[] ManifestPaths { get; }
            public AbsolutePath[] Queries { get; }

            public BenchmarkData(AbsolutePath root, AbsolutePath[] manifestPaths, AbsolutePath[] queries)
            {
                Root = root;
                ManifestPaths = manifestPaths;
                Queries = queries;
            }
        }

        private readonly struct PreparedManifest
        {
            public FileAccessManifest Manifest { get; }
            public int TreeSize { get; }
            public TimeSpan Elapsed { get; }
            public long AllocatedBytes { get; }

            public PreparedManifest(FileAccessManifest manifest, int treeSize, TimeSpan elapsed, long allocatedBytes)
            {
                Manifest = manifest;
                TreeSize = treeSize;
                Elapsed = elapsed;
                AllocatedBytes = allocatedBytes;
            }
        }

        private readonly struct Measurement
        {
            public long QueryCount { get; }
            public TimeSpan Elapsed { get; }
            public long AllocatedBytes { get; }
            public long Checksum { get; }
            public double QueriesPerSecond => QueryCount / Elapsed.TotalSeconds;
            public double NanosecondsPerQuery => Elapsed.TotalSeconds * 1_000_000_000.0 / QueryCount;
            public double BytesPerQuery => (double)AllocatedBytes / QueryCount;

            public Measurement(long queryCount, TimeSpan elapsed, long allocatedBytes, long checksum)
            {
                QueryCount = queryCount;
                Elapsed = elapsed;
                AllocatedBytes = allocatedBytes;
                Checksum = checksum;
            }
        }
    }
}
