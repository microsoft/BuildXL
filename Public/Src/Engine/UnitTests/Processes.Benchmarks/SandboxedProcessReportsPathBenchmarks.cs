// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Processes.Benchmarks
{
    /// <summary>
    /// Compares the path-resolution and string-canonicalization work performed for sandbox access reports.
    /// Run with: bxl Test.BuildXL.Processes.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet10 /server-
    /// </summary>
    public sealed class SandboxedProcessReportsPathBenchmarks
    {
        private const int UniquePathCount = 100_000;
        private const int MeasurementRounds = 5;

        private readonly ITestOutputHelper m_output;

        public SandboxedProcessReportsPathBenchmarks(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Fact]
        public void CompareReportPathProcessing()
        {
            WarmUp();
            CompareScenario(reportsPerPath: 1);
            CompareScenario(reportsPerPath: 4);
        }

        private void CompareScenario(int reportsPerPath)
        {
            var controlMeasurements = new List<Measurement>(MeasurementRounds);
            var proposedMeasurements = new List<Measurement>(MeasurementRounds);

            for (int round = 0; round < MeasurementRounds; round++)
            {
                if ((round & 1) == 0)
                {
                    controlMeasurements.Add(Measure(useProposed: false, reportsPerPath));
                    proposedMeasurements.Add(Measure(useProposed: true, reportsPerPath));
                }
                else
                {
                    proposedMeasurements.Add(Measure(useProposed: true, reportsPerPath));
                    controlMeasurements.Add(Measure(useProposed: false, reportsPerPath));
                }

                Measurement control = controlMeasurements[round];
                Measurement proposed = proposedMeasurements[round];
                Assert.Equal(control.Checksum, proposed.Checksum);
                Assert.Equal(control.CachedPathCount, proposed.CachedPathCount);
                PrintMeasurement("control", reportsPerPath, round + 1, control);
                PrintMeasurement("proposed", reportsPerPath, round + 1, proposed);
            }

            Measurement controlMedian = Median(controlMeasurements);
            Measurement proposedMedian = Median(proposedMeasurements);

            m_output.WriteLine(
                $"SUMMARY reports/path={reportsPerPath}: proposed elapsed={proposedMedian.Elapsed.TotalMilliseconds / controlMedian.Elapsed.TotalMilliseconds:P1} of control, " +
                $"allocated={proposedMedian.AllocatedBytes / (double)controlMedian.AllocatedBytes:P1} of control, " +
                $"retained delta={proposedMedian.RetainedBytes - controlMedian.RetainedBytes:N0} B.");
        }

        private static void WarmUp()
        {
            Measure(useProposed: false, reportsPerPath: 2, uniquePathCount: 100);
            Measure(useProposed: true, reportsPerPath: 2, uniquePathCount: 100);
        }

        private static Measurement Measure(bool useProposed, int reportsPerPath, int uniquePathCount = UniquePathCount)
        {
            var pathTable = new PathTable();
            PathData[] paths = CreatePaths(pathTable, uniquePathCount);
            IPathProcessor processor = useProposed
                ? new ProposedPathProcessor(pathTable)
                : new ControlPathProcessor(pathTable);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            long checksum = 0;

            for (int pass = 0; pass < reportsPerPath; pass++)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string reportPath = new string(paths[i].PathCharacters);
                    checksum = unchecked(checksum + processor.Process(reportPath, paths[i].ManifestPath));
                }
            }

            stopwatch.Stop();
            long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            long retainedBytes = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;

            GC.KeepAlive(paths);
            GC.KeepAlive(processor);

            return new Measurement(
                stopwatch.Elapsed,
                allocatedBytes,
                retainedBytes,
                processor.CachedPathCount,
                checksum);
        }

        private static PathData[] CreatePaths(PathTable pathTable, int uniquePathCount)
        {
            string root = OperatingSystemHelper.IsWindowsOS ? @"C:\src\repo" : "/src/repo";
            char separator = OperatingSystemHelper.IsWindowsOS ? '\\' : '/';
            var paths = new PathData[uniquePathCount];

            for (int i = 0; i < uniquePathCount; i++)
            {
                string projectPath = $"{root}{separator}shard-{i % 64:D2}{separator}project-{i:D6}";
                string filePath = $"{projectPath}{separator}obj{separator}release{separator}file-{i:D6}.dll";

                // Half of reports model exact manifest entries already interned in the PathTable. The other half
                // model scope descendants whose full paths are first observed while processing sandbox reports.
                string manifestPath = (i & 1) == 0 ? filePath : projectPath;
                paths[i] = new PathData(filePath.ToCharArray(), AbsolutePath.Create(pathTable, manifestPath));
            }

            return paths;
        }

        private void PrintMeasurement(string name, int reportsPerPath, int round, Measurement measurement)
        {
            m_output.WriteLine(
                $"{name,-8} reports/path={reportsPerPath}, round {round}: {measurement.Elapsed.TotalMilliseconds,8:F1} ms, " +
                $"{measurement.AllocatedBytes / (1024.0 * 1024.0),8:F1} MiB allocated, " +
                $"{measurement.RetainedBytes / (1024.0 * 1024.0),8:F1} MiB retained, " +
                $"{measurement.CachedPathCount:N0} cached paths.");
        }

        private static Measurement Median(List<Measurement> measurements)
        {
            return measurements.OrderBy(measurement => measurement.Elapsed).ElementAt(measurements.Count / 2);
        }

        private interface IPathProcessor
        {
            int CachedPathCount { get; }

            int Process(string path, AbsolutePath manifestPath);
        }

        private sealed class ControlPathProcessor : IPathProcessor
        {
            private readonly PathTable m_pathTable;
            private readonly Dictionary<string, string> m_pathCache = new(OperatingSystemHelper.PathComparer);

            public ControlPathProcessor(PathTable pathTable)
            {
                m_pathTable = pathTable;
            }

            public int CachedPathCount => m_pathCache.Count;

            public int Process(string path, AbsolutePath manifestPath)
            {
                AbsolutePath finalPath = AbsolutePath.Invalid;
                if (AbsolutePath.TryGet(m_pathTable, path, out finalPath) && finalPath == manifestPath)
                {
                    path = null;
                }

                if (!finalPath.IsValid)
                {
                    AbsolutePath.TryCreate(m_pathTable, path, out finalPath);
                }

                if (path is not null)
                {
                    if (m_pathCache.TryGetValue(path, out string cachedPath))
                    {
                        path = cachedPath;
                    }
                    else
                    {
                        m_pathCache[path] = path;
                    }
                }

                return (finalPath.IsValid ? 17 : 31) + (path?.Length ?? 0);
            }
        }

        private sealed class ProposedPathProcessor : IPathProcessor
        {
            private readonly PathTable m_pathTable;
            private readonly HashSet<string> m_pathCache = new(OperatingSystemHelper.PathComparer);

            public ProposedPathProcessor(PathTable pathTable)
            {
                m_pathTable = pathTable;
            }

            public int CachedPathCount => m_pathCache.Count;

            public int Process(string path, AbsolutePath manifestPath)
            {
                bool pathCreated = AbsolutePath.TryCreate(m_pathTable, path, out AbsolutePath finalPath);
                if (pathCreated && finalPath == manifestPath)
                {
                    path = null;
                }

                if (path is not null)
                {
                    if (m_pathCache.TryGetValue(path, out string cachedPath))
                    {
                        path = cachedPath;
                    }
                    else
                    {
                        m_pathCache.Add(path);
                    }
                }

                return (finalPath.IsValid ? 17 : 31) + (path?.Length ?? 0);
            }
        }

        private readonly struct PathData
        {
            public readonly char[] PathCharacters;
            public readonly AbsolutePath ManifestPath;

            public PathData(char[] pathCharacters, AbsolutePath manifestPath)
            {
                PathCharacters = pathCharacters;
                ManifestPath = manifestPath;
            }
        }

        private readonly struct Measurement
        {
            public readonly TimeSpan Elapsed;
            public readonly long AllocatedBytes;
            public readonly long RetainedBytes;
            public readonly int CachedPathCount;
            public readonly long Checksum;

            public Measurement(
                TimeSpan elapsed,
                long allocatedBytes,
                long retainedBytes,
                int cachedPathCount,
                long checksum)
            {
                Elapsed = elapsed;
                AllocatedBytes = allocatedBytes;
                RetainedBytes = retainedBytes;
                CachedPathCount = cachedPathCount;
                Checksum = checksum;
            }
        }
    }
}
