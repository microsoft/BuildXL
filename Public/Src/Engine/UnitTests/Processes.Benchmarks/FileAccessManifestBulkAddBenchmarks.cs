// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Processes.Benchmarks
{
    /// <summary>
    /// Measures dictionary-backed bulk path insertion and large-manifest sealing.
    /// </summary>
    /// <remarks>
    /// The benchmark models a large build with 25,000 projects and 200,000 output paths. It compares
    /// <see cref="FileAccessManifest.AddPaths"/> with repeated single-path insertion for sorted and shuffled input.
    /// Run it with:
    /// <c>bxl Test.BuildXL.Processes.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet10 /server-</c>.
    /// </remarks>
    public sealed class FileAccessManifestBulkAddBenchmarks
    {
        private const int ProjectCount = 25_000;
        private const int FilesPerProject = 8;

        /// <summary>
        /// Additional nested directories used by the deep arm of <see cref="CompareBulkAndNaiveInsertionCost"/>.
        /// </summary>
        private const int ExtraDirectoryDepth = 6;

        private const int MeasurementIterations = 5;

        private const int WarmUpProjectCount = 200;

        private readonly ITestOutputHelper m_output;

        public FileAccessManifestBulkAddBenchmarks(ITestOutputHelper output)
        {
            m_output = output;
        }

        /// <summary>
        /// Compares dictionary-backed bulk insertion against the recursive <see cref="FileAccessManifest.AddPath"/>
        /// walk, for both sorted and shuffled input at two path depths.
        /// </summary>
        /// <remarks>
        /// Every arm is warmed up before measurement and reported as the best of <see cref="MeasurementIterations"/>
        /// runs, so the first arm does not absorb JIT, string-table, and heap-growth cost on behalf of the others.
        /// </remarks>
        [Fact]
        public void CompareBulkAndNaiveInsertionCost()
        {
            WarmUpInsertionArms();

            foreach (int extraDirectoryDepth in new[] { 0, ExtraDirectoryDepth })
            {
                var pathTable = new PathTable();
                AbsolutePath[] sortedPaths = CreatePaths(pathTable, extraDirectoryDepth, ProjectCount);
                AbsolutePath[] shuffledPaths = sortedPaths.ToArray();
                Shuffle(shuffledPaths);

                m_output.WriteLine(
                    $"insertion cost (depth={GetPathDepth(pathTable, sortedPaths[0])}, " +
                    $"{sortedPaths.Length:N0} paths, best of {MeasurementIterations}):");
                m_output.WriteLine($"  {"strategy",-16}{"order",-10}{"best ms",12}{"MiB allocated",16}");

                double bulkSortedMilliseconds = 0;
                double naiveSortedMilliseconds = 0;
                foreach (bool useBulkAdd in new[] { true, false })
                {
                    foreach (string order in new[] { "sorted", "shuffled" })
                    {
                        IReadOnlyList<AbsolutePath> paths = order == "sorted" ? sortedPaths : shuffledPaths;
                        (double bestMilliseconds, double allocatedMiB) = RunInsertionArm(pathTable, paths, useBulkAdd);

                        m_output.WriteLine(
                            $"  {(useBulkAdd ? "bulk (dict)" : "naive recursive"),-16}{order,-10}" +
                            $"{bestMilliseconds,12:F1}{allocatedMiB,16:F1}");

                        if (order == "sorted")
                        {
                            if (useBulkAdd)
                            {
                                bulkSortedMilliseconds = bestMilliseconds;
                            }
                            else
                            {
                                naiveSortedMilliseconds = bestMilliseconds;
                            }
                        }
                    }
                }

                m_output.WriteLine(
                    $"  sorted speedup from ancestor dictionary: {naiveSortedMilliseconds / bulkSortedMilliseconds:F2}x");
            }
        }

        [Fact]
        public void MeasureLargeManifestSealing()
        {
            var pathTable = new PathTable();
            AbsolutePath[] paths = CreatePaths(pathTable);
            var manifest = new FileAccessManifest(pathTable);
            manifest.AddPaths(paths, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
            MethodInfo sealManifestTree = typeof(FileAccessManifest).GetMethod(
                "SealManifestTree",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(sealManifestTree);

            ForceCollection();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();

            sealManifestTree.Invoke(manifest, null);

            stopwatch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            byte[] serializedTree = manifest.GetManifestTreeBytes();
            m_output.WriteLine(
                $"large manifest sealing: {stopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
                $"{allocated / (1024.0 * 1024.0):F1} MiB allocated, " +
                $"{serializedTree.Length / (1024.0 * 1024.0):F1} MiB serialized");
        }

        /// <summary>
        /// Runs a single insertion strategy against the given path order and returns its best elapsed time and
        /// allocation. A fresh manifest is built per iteration so each run does the full insertion work.
        /// </summary>
        /// <remarks>
        /// The first full-size insertion for a given path set pays for gen2 heap growth, which made whichever arm ran
        /// first appear up to twice as slow. One unmeasured iteration absorbs that, and the minimum of the remaining
        /// iterations is reported because it is the sample least contaminated by unrelated collections.
        /// </remarks>
        private static (double BestMilliseconds, double AllocatedMiB) RunInsertionArm(
            PathTable pathTable,
            IReadOnlyList<AbsolutePath> paths,
            bool useBulkAdd)
        {
            InsertPaths(new FileAccessManifest(pathTable), paths, useBulkAdd);

            double bestMilliseconds = double.MaxValue;
            long allocated = 0;

            for (int iteration = 0; iteration < MeasurementIterations; iteration++)
            {
                ForceCollection();
                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                var manifest = new FileAccessManifest(pathTable);
                var stopwatch = Stopwatch.StartNew();

                InsertPaths(manifest, paths, useBulkAdd);

                stopwatch.Stop();
                allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                bestMilliseconds = Math.Min(bestMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
                GC.KeepAlive(manifest);
            }

            return (bestMilliseconds, allocated / (1024.0 * 1024.0));
        }

        private static void InsertPaths(FileAccessManifest manifest, IReadOnlyList<AbsolutePath> paths, bool useBulkAdd)
        {
            if (useBulkAdd)
            {
                manifest.AddPaths(paths, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
                return;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                manifest.AddPath(paths[i], FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
            }
        }

        /// <summary>
        /// Exercises every measured strategy once on a small path set so that JIT compilation and string-table warmup
        /// are not attributed to whichever arm happens to run first.
        /// </summary>
        private static void WarmUpInsertionArms()
        {
            foreach (int extraDirectoryDepth in new[] { 0, ExtraDirectoryDepth })
            {
                var pathTable = new PathTable();
                AbsolutePath[] paths = CreatePaths(pathTable, extraDirectoryDepth, WarmUpProjectCount);
                AbsolutePath[] shuffledPaths = paths.ToArray();
                Shuffle(shuffledPaths);

                foreach (bool useBulkAdd in new[] { true, false })
                {
                    InsertPaths(new FileAccessManifest(pathTable), paths, useBulkAdd);
                    InsertPaths(new FileAccessManifest(pathTable), shuffledPaths, useBulkAdd);
                }
            }

            ForceCollection();
        }

        private static int GetPathDepth(PathTable pathTable, AbsolutePath path)
        {
            int depth = 0;
            AbsolutePath current = path;
            while (current.IsValid)
            {
                depth++;
                current = current.GetParent(pathTable);
            }

            return depth;
        }

        private static AbsolutePath[] CreatePaths(PathTable pathTable)
        {
            return CreatePaths(pathTable, extraDirectoryDepth: 0, projectCount: ProjectCount);
        }

        private static AbsolutePath[] CreatePaths(PathTable pathTable, int extraDirectoryDepth, int projectCount)
        {
            string root = OperatingSystemHelper.IsUnixOS ? "/benchmark" : @"c:\benchmark";
            var nestedSegments = new string[extraDirectoryDepth];
            for (int i = 0; i < extraDirectoryDepth; i++)
            {
                nestedSegments[i] = $"nested-{i:D2}";
            }

            string nested = Path.Combine(nestedSegments);
            var paths = new AbsolutePath[projectCount * FilesPerProject];
            int index = 0;
            for (int project = 0; project < projectCount; project++)
            {
                for (int file = 0; file < FilesPerProject; file++)
                {
                    paths[index++] = AbsolutePath.Create(
                        pathTable,
                        Path.Combine(root, $"project-{project:D5}", "obj", nested, $"file-{file:D2}.obj"));
                }
            }

            return paths;
        }

        private static void Shuffle(AbsolutePath[] paths)
        {
            // Use a fixed seed so shuffled benchmark runs process the same path order.
            var random = new Random(42);
            for (int i = paths.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (paths[i], paths[swapIndex]) = (paths[swapIndex], paths[i]);
            }
        }

        private static void ForceCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
