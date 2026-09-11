// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities.Benchmarks
{
    public sealed class AbsolutePathAncestorCheckerBenchmarks : XunitBuildXLTest
    {
        private const int QueryCount = 64 * 1024;
        private const int ParentCount = 1024;
        private const int MeasurementPasses = 64;

        private static readonly FieldInfo s_positiveCacheField = typeof(AbsolutePathAncestorChecker).GetField(
            "m_pathsWithKnownAncestor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_negativeCacheField = typeof(AbsolutePathAncestorChecker).GetField(
            "m_pathsWithoutKnownAncestor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ITestOutputHelper m_output;

        public AbsolutePathAncestorCheckerBenchmarks(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void RunAbsolutePathAncestorCheckerBenchmark()
        {
            var pathTable = new PathTable();
            string root = OperatingSystemHelper.IsUnixOS ? "/benchmark" : @"c:\benchmark";
            var knownAncestor = AbsolutePath.Create(pathTable, Path.Combine(root, "known"));
            var positiveQueries = CreateQueries(pathTable, Path.Combine(root, "known"));
            var negativeQueries = CreateQueries(pathTable, Path.Combine(root, "unknown"));
            pathTable.Freeze();

            RunScenario(pathTable, knownAncestor, positiveQueries, "positive");
            RunScenario(pathTable, knownAncestor, negativeQueries, "negative");
        }

        private void RunScenario(
            PathTable pathTable,
            AbsolutePath knownAncestor,
            AbsolutePath[] queries,
            string scenario)
        {
            var baseline = new BaselineAncestorChecker();
            baseline.AddPath(knownAncestor);
            RunBaseline(baseline, pathTable, queries, 1);

            var optimized = new AbsolutePathAncestorChecker();
            optimized.AddPath(knownAncestor);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var coldStopwatch = Stopwatch.StartNew();
            int coldChecksum = RunOptimized(optimized, pathTable, queries, 1);
            coldStopwatch.Stop();
            long coldAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            var baselineStopwatch = Stopwatch.StartNew();
            int baselineChecksum = RunBaseline(baseline, pathTable, queries, MeasurementPasses);
            baselineStopwatch.Stop();

            var optimizedStopwatch = Stopwatch.StartNew();
            int optimizedChecksum = RunOptimized(optimized, pathTable, queries, MeasurementPasses);
            optimizedStopwatch.Stop();

            var positiveCache = GetCache(optimized, s_positiveCacheField);
            var negativeCache = GetCache(optimized, s_negativeCacheField);
            m_output.WriteLine(
                $"{scenario}: cold={coldStopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
                $"coldAllocated={coldAllocated / 1024.0:F1} KiB, " +
                $"positiveCache={positiveCache.Count:N0}/{positiveCache.EnsureCapacity(0):N0}, " +
                $"negativeCache={negativeCache.Count:N0}/{negativeCache.EnsureCapacity(0):N0}");
            m_output.WriteLine(
                $"{scenario}: baseline={GetMillionOperationsPerSecond(baselineStopwatch):F2} Mops/s, " +
                $"optimized={GetMillionOperationsPerSecond(optimizedStopwatch):F2} Mops/s, " +
                $"speedup={baselineStopwatch.Elapsed.TotalMilliseconds / optimizedStopwatch.Elapsed.TotalMilliseconds:F2}x, " +
                $"checksum={coldChecksum + baselineChecksum + optimizedChecksum:N0}");

            optimized.Clear();
            m_output.WriteLine(
                $"{scenario}: capacity after Clear: " +
                $"positive={positiveCache.EnsureCapacity(0):N0}, negative={negativeCache.EnsureCapacity(0):N0}");
        }

        private static AbsolutePath[] CreateQueries(PathTable pathTable, string root)
        {
            var queries = new AbsolutePath[QueryCount];
            for (int i = 0; i < queries.Length; i++)
            {
                queries[i] = AbsolutePath.Create(
                    pathTable,
                    Path.Combine(
                        root,
                        $"project-{i % ParentCount:D4}",
                        "obj",
                        "configuration",
                        $"file-{i:D6}.obj"));
            }

            return queries;
        }

        private static double GetMillionOperationsPerSecond(Stopwatch stopwatch)
        {
            return (double)QueryCount * MeasurementPasses / stopwatch.Elapsed.TotalSeconds / 1_000_000;
        }

        private static HashSet<HierarchicalNameId> GetCache(AbsolutePathAncestorChecker checker, FieldInfo field)
        {
            return (HashSet<HierarchicalNameId>)field.GetValue(checker);
        }

        private static int RunOptimized(
            AbsolutePathAncestorChecker checker,
            PathTable pathTable,
            AbsolutePath[] queries,
            int passes)
        {
            int checksum = 0;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < queries.Length; i++)
                {
                    checksum += checker.HasKnownAncestor(pathTable, queries[i]) ? 1 : 0;
                }
            }

            return checksum;
        }

        private static int RunBaseline(
            BaselineAncestorChecker checker,
            PathTable pathTable,
            AbsolutePath[] queries,
            int passes)
        {
            int checksum = 0;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < queries.Length; i++)
                {
                    checksum += checker.HasKnownAncestor(pathTable, queries[i]) ? 1 : 0;
                }
            }

            return checksum;
        }

        private sealed class BaselineAncestorChecker
        {
            private readonly HashSet<HierarchicalNameId> m_paths = new HashSet<HierarchicalNameId>();

            public void AddPath(AbsolutePath path)
            {
                m_paths.Add(path.Value);
            }

            public bool HasKnownAncestor(PathTable pathTable, AbsolutePath path)
            {
                var currentPath = path;
                while (currentPath != AbsolutePath.Invalid)
                {
                    if (m_paths.Contains(currentPath.Value))
                    {
                        return true;
                    }

                    currentPath = currentPath.GetParent(pathTable);
                }

                return false;
            }
        }
    }
}
