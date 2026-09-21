// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Processes.Benchmarks
{
    /// <summary>
    /// Measures the grouping, filtering, sorting, and allocation cost of producing observed file accesses from sandbox
    /// reports.
    /// </summary>
    /// <remarks>
    /// The benchmark compares the previous incremental sorted-tree strategy with the deferred array sort used by
    /// <see cref="SandboxedProcessPipExecutor.CreateSortedObservedFileAccesses"/>. It runs equivalent mixed-access workloads in single-threaded and
    /// concurrent modes and verifies identical output before recording measurements. Run it with:
    /// <c>bxl Test.BuildXL.Processes.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet10 /server-</c>.
    /// </remarks>
    public sealed class ObservedFileAccessValidationBenchmarks
    {
        private const int UniquePathCount = 50_000;
        private const int AccessesPerPath = 4;
        private const int MeasurementPasses = 4;
        private const int MeasurementRounds = 3;

        /// <summary>
        /// Filling is a small fraction of a full pass, so it is repeated to make the difference measurable.
        /// </summary>
        private const int FillIterations = 200;

        private readonly ITestOutputHelper m_output;

        public ObservedFileAccessValidationBenchmarks(ITestOutputHelper output)
        {
            m_output = output;
        }

        /// <summary>
        /// Compares incremental tree maintenance with dictionary accumulation followed by one final array sort.
        /// </summary>
        /// <remarks>
        /// Each measured round processes four passes over 200,000 reports representing 50,000 unique paths. Checksums
        /// ensure that every strategy and concurrency level produces equivalent observations.
        /// </remarks>
        [Fact]
        public void CompareIncrementalTreeAndDeferredArraySort()
        {
            var pathTable = new PathTable();
            BenchmarkData data = CreateData(pathTable);
            var comparer = new ObservedFileAccessExpandedPathComparer(pathTable.ExpandedPathComparer);

            ObservedFileAccess[] control = BuildWithIncrementalSortedDictionary(data, comparer);
            ObservedFileAccess[] optimized = BuildWithDeferredSort(data, comparer);
            AssertEquivalent(control, optimized);

            int maximumConcurrency = Math.Min(8, Math.Max(1, Environment.ProcessorCount));
            int[] concurrencyLevels = maximumConcurrency == 1 ? new[] { 1 } : new[] { 1, maximumConcurrency };

            m_output.WriteLine(
                $"Observed access benchmark: {UniquePathCount:N0} unique paths, " +
                $"{data.Accesses.Length:N0} mixed reports/pass, {MeasurementPasses:N0} passes/round, " +
                $"{MeasurementRounds:N0} rounds, {Environment.ProcessorCount:N0} logical processors.");

            foreach (int concurrency in concurrencyLevels)
            {
                MeasureRounds("incremental sorted tree", data, comparer, concurrency, BuildWithIncrementalSortedDictionary);
                MeasureRounds("deferred array sort", data, comparer, concurrency, BuildWithDeferredSort);
            }
        }

        /// <summary>
        /// Measures one strategy for three rounds at the requested concurrency and reports time, allocation, and checksum.
        /// </summary>
        private void MeasureRounds(
            string name,
            BenchmarkData data,
            ObservedFileAccessExpandedPathComparer comparer,
            int concurrency,
            Func<BenchmarkData, ObservedFileAccessExpandedPathComparer, ObservedFileAccess[]> operation)
        {
            for (int round = 1; round <= MeasurementRounds; round++)
            {
                ForceCollection();
                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                var stopwatch = Stopwatch.StartNew();
                long checksum = RunPasses(data, comparer, concurrency, operation);
                stopwatch.Stop();
                long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

                m_output.WriteLine(
                    $"{name,-24} c={concurrency,2} round={round}: " +
                    $"{stopwatch.Elapsed.TotalMilliseconds,8:N1} ms, " +
                    $"{allocated / (1024.0 * 1024.0),8:N1} MiB allocated, checksum={checksum}");
            }
        }

        /// <summary>
        /// Executes all measurement passes sequentially or with bounded parallelism and combines their checksums.
        /// </summary>
        private static long RunPasses(
            BenchmarkData data,
            ObservedFileAccessExpandedPathComparer comparer,
            int concurrency,
            Func<BenchmarkData, ObservedFileAccessExpandedPathComparer, ObservedFileAccess[]> operation)
        {
            if (concurrency == 1)
            {
                long checksum = 0;
                for (int pass = 0; pass < MeasurementPasses; pass++)
                {
                    checksum = unchecked(checksum + GetChecksum(operation(data, comparer)));
                }

                return checksum;
            }

            long parallelChecksum = 0;
            Parallel.For(
                0,
                MeasurementPasses,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                () => 0L,
                (_, _, localChecksum) => unchecked(localChecksum + GetChecksum(operation(data, comparer))),
                localChecksum => Interlocked.Add(ref parallelChecksum, localChecksum));
            return parallelChecksum;
        }

        /// <summary>
        /// Models the previous implementation, which maintained a sorted tree as each unique path was discovered.
        /// </summary>
        private static ObservedFileAccess[] BuildWithIncrementalSortedDictionary(
            BenchmarkData data,
            ObservedFileAccessExpandedPathComparer comparer)
        {
            var accessesByPath = new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(UniquePathCount);
            var sortedByPath = new SortedDictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(comparer.PathComparer);

            foreach (AccessSample sample in data.Accesses)
            {
                if (!accessesByPath.TryGetValue(sample.Path, out ReportedFileAccessesAndFlagsMutable access))
                {
                    access = new ReportedFileAccessesAndFlagsMutable();
                    accessesByPath.Add(sample.Path, access);
                    if (!data.DeclaredOutputs.Contains(sample.Path))
                    {
                        sortedByPath.Add(sample.Path, access);
                    }
                }

                UpdateAccess(access, sample);
                if (access.IsSharedOpaqueOutput)
                {
                    sortedByPath.Remove(sample.Path);
                }
            }

            foreach (AbsolutePath declaredOutput in data.DeclaredOutputs)
            {
                accessesByPath.Remove(declaredOutput);
            }

            var unusedMaterialization = new ObservedFileAccess[sortedByPath.Count];
            int index = 0;
            foreach (var access in sortedByPath)
            {
                unusedMaterialization[index++] =
                    new ObservedFileAccess(access.Key, access.Value.ObservationFlags, access.Value.ReportedFileAccesses);
            }

            GC.KeepAlive(unusedMaterialization);
            return sortedByPath
                .Where(access => ShouldIncludeAccess(access.Key, access.Value, data.ExcludedPaths))
                .Select(access => new ObservedFileAccess(
                    access.Key,
                    access.Value.ObservationFlags,
                    access.Value.ReportedFileAccesses))
                .ToArray();
        }

        /// <summary>
        /// Models the optimized implementation, which accumulates by path and sorts only the final eligible array.
        /// </summary>
        private static ObservedFileAccess[] BuildWithDeferredSort(
            BenchmarkData data,
            ObservedFileAccessExpandedPathComparer comparer)
        {
            var accessesByPath = new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(UniquePathCount);

            foreach (AccessSample sample in data.Accesses)
            {
                if (!accessesByPath.TryGetValue(sample.Path, out ReportedFileAccessesAndFlagsMutable access))
                {
                    access = new ReportedFileAccessesAndFlagsMutable();
                    accessesByPath.Add(sample.Path, access);
                }

                UpdateAccess(access, sample);
            }

            foreach (AbsolutePath declaredOutput in data.DeclaredOutputs)
            {
                accessesByPath.Remove(declaredOutput);
            }

            return SandboxedProcessPipExecutor.CreateSortedObservedFileAccesses(accessesByPath, data.ExcludedPaths, comparer);
        }

        /// <summary>
        /// Compares the strategies for turning the accumulated dictionary into the final sorted array.
        /// </summary>
        /// <remarks>
        /// Review question: whether traversing the dictionary twice to size the result exactly is cheaper than one
        /// traversal into an intermediate buffer that is then copied. The two-pass arm pays a second cache-unfriendly
        /// dictionary walk and a second <c>ShouldIncludeAccess</c> call per entry; the buffered arms pay one copy.
        /// Each arm is warmed up and reported as the best of <see cref="MeasurementRounds"/> rounds so the first arm
        /// measured does not absorb heap growth on behalf of the others.
        /// </remarks>
        [Fact]
        public void CompareMaterializationStrategies()
        {
            var pathTable = new PathTable();
            BenchmarkData data = CreateData(pathTable);
            var comparer = new ObservedFileAccessExpandedPathComparer(pathTable.ExpandedPathComparer);

            // Accumulate once and reuse it. Filling does not mutate the dictionary, and timing the accumulation of
            // 200,000 reports alongside it hides the difference being measured.
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath = Accumulate(data);
            HashSet<AbsolutePath> excluded = data.ExcludedPaths;

            var strategies = new (string Name, Func<Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>, HashSet<AbsolutePath>, ObservedFileAccess[]> Fill)[]
            {
                ("two-pass exact", FillTwoPassExact),
                ("pooled list", FillPooledList),
                ("upper-bound buffer", FillUpperBoundBuffer),
            };

            ObservedFileAccess[] baseline = strategies[0].Fill(accessesByPath, excluded);
            foreach (var strategy in strategies)
            {
                ObservedFileAccess[] candidate = strategy.Fill(accessesByPath, excluded);
                Assert.Equal(baseline.Length, candidate.Length);
            }

            m_output.WriteLine(
                $"fill only, no sort: {accessesByPath.Count:N0} accumulated paths, {baseline.Length:N0} retained, " +
                $"{FillIterations:N0} iterations/round, best of {MeasurementRounds:N0}:");
            m_output.WriteLine($"  {"strategy",-20}{"best ms",10}{"us/call",10}{"MiB/call",12}");

            foreach (var strategy in strategies)
            {
                // Discard one round so the first strategy measured does not absorb gen2 growth for the rest.
                RunFillIterations(accessesByPath, excluded, strategy.Fill);

                double bestMilliseconds = double.MaxValue;
                double allocatedMiB = 0;
                for (int round = 0; round < MeasurementRounds; round++)
                {
                    ForceCollection();
                    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                    var stopwatch = Stopwatch.StartNew();
                    RunFillIterations(accessesByPath, excluded, strategy.Fill);
                    stopwatch.Stop();
                    long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

                    if (stopwatch.Elapsed.TotalMilliseconds < bestMilliseconds)
                    {
                        bestMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                        allocatedMiB = allocated / (1024.0 * 1024.0) / FillIterations;
                    }
                }

                m_output.WriteLine(
                    $"  {strategy.Name,-20}{bestMilliseconds,10:F1}" +
                    $"{bestMilliseconds * 1000.0 / FillIterations,10:F1}{allocatedMiB,12:F2}");
            }

            MeasureSortCost(accessesByPath, excluded, comparer);
        }

        /// <summary>
        /// Reports the cost of the sort that follows filling, for scale against the fill differences above.
        /// </summary>
        private void MeasureSortCost(
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath,
            HashSet<AbsolutePath> excludedPaths,
            ObservedFileAccessExpandedPathComparer comparer)
        {
            ObservedFileAccess[] unsorted = FillTwoPassExact(accessesByPath, excludedPaths);
            double bestMilliseconds = double.MaxValue;

            for (int round = 0; round < MeasurementRounds + 1; round++)
            {
                var toSort = (ObservedFileAccess[])unsorted.Clone();
                ForceCollection();
                var stopwatch = Stopwatch.StartNew();
                Array.Sort(toSort, comparer);
                stopwatch.Stop();

                // Skip the first round, which absorbs path expansion warm-up.
                if (round > 0)
                {
                    bestMilliseconds = Math.Min(bestMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            m_output.WriteLine($"  {"sort (once)",-20}{bestMilliseconds,10:F1}{bestMilliseconds * 1000.0,10:F1}");
        }

        private static ObservedFileAccess[] FillTwoPassExact(
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath,
            HashSet<AbsolutePath> excludedPaths)
        {
            int includedAccessCount = 0;
            foreach (var access in accessesByPath)
            {
                if (SandboxedProcessPipExecutor.ShouldIncludeObservedFileAccess(access.Key, access.Value, excludedPaths))
                {
                    includedAccessCount++;
                }
            }

            var sortedAccesses = new ObservedFileAccess[includedAccessCount];
            int index = 0;
            foreach (var access in accessesByPath)
            {
                if (SandboxedProcessPipExecutor.ShouldIncludeObservedFileAccess(access.Key, access.Value, excludedPaths))
                {
                    sortedAccesses[index++] = new ObservedFileAccess(
                        access.Key,
                        access.Value.ObservationFlags,
                        access.Value.ReportedFileAccesses);
                }
            }

            return sortedAccesses;
        }

        private static ObservedFileAccess[] FillPooledList(
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath,
            HashSet<AbsolutePath> excludedPaths)
        {
            using (var wrapper = ProcessPools.ObservedFileAccessList.GetInstance())
            {
                List<ObservedFileAccess> includedAccesses = wrapper.Instance;
                foreach (var access in accessesByPath)
                {
                    if (SandboxedProcessPipExecutor.ShouldIncludeObservedFileAccess(access.Key, access.Value, excludedPaths))
                    {
                        includedAccesses.Add(new ObservedFileAccess(
                            access.Key,
                            access.Value.ObservationFlags,
                            access.Value.ReportedFileAccesses));
                    }
                }

                return includedAccesses.ToArray();
            }
        }

        private static ObservedFileAccess[] FillUpperBoundBuffer(
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath,
            HashSet<AbsolutePath> excludedPaths)
        {
            var buffer = new ObservedFileAccess[accessesByPath.Count];
            int includedAccessCount = 0;
            foreach (var access in accessesByPath)
            {
                if (SandboxedProcessPipExecutor.ShouldIncludeObservedFileAccess(access.Key, access.Value, excludedPaths))
                {
                    buffer[includedAccessCount++] = new ObservedFileAccess(
                        access.Key,
                        access.Value.ObservationFlags,
                        access.Value.ReportedFileAccesses);
                }
            }

            if (includedAccessCount == buffer.Length)
            {
                return buffer;
            }

            var sortedAccesses = new ObservedFileAccess[includedAccessCount];
            Array.Copy(buffer, sortedAccesses, includedAccessCount);
            return sortedAccesses;
        }

        /// <summary>
        /// Runs the fill step repeatedly over an already accumulated dictionary.
        /// </summary>
        private static long RunFillIterations(
            Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> accessesByPath,
            HashSet<AbsolutePath> excludedPaths,
            Func<Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>, HashSet<AbsolutePath>, ObservedFileAccess[]> fill)
        {
            long checksum = 0;
            for (int iteration = 0; iteration < FillIterations; iteration++)
            {
                checksum = unchecked(checksum + fill(accessesByPath, excludedPaths).Length);
            }

            return checksum;
        }

        /// <summary>
        /// Accumulates reports by path and drops declared outputs, producing the dictionary the executor materializes.
        /// </summary>
        private static Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable> Accumulate(BenchmarkData data)
        {
            var accessesByPath = new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(UniquePathCount);

            foreach (AccessSample sample in data.Accesses)
            {
                if (!accessesByPath.TryGetValue(sample.Path, out ReportedFileAccessesAndFlagsMutable access))
                {
                    access = new ReportedFileAccessesAndFlagsMutable();
                    accessesByPath.Add(sample.Path, access);
                }

                UpdateAccess(access, sample);
            }

            foreach (AbsolutePath declaredOutput in data.DeclaredOutputs)
            {
                accessesByPath.Remove(declaredOutput);
            }

            return accessesByPath;
        }

        /// <summary>
        /// Merges one reported access into the mutable state associated with its normalized path.
        /// </summary>
        private static void UpdateAccess(ReportedFileAccessesAndFlagsMutable access, AccessSample sample)
        {
            access.ObservationFlags |= sample.Flags;
            access.ReportedFileAccesses = access.ReportedFileAccesses.Add(sample.Access);
            access.IsSharedOpaqueOutput |= sample.IsSharedOpaqueOutput;
        }

        /// <summary>
        /// Applies the legacy final inclusion rule used to verify the deferred-sort implementation.
        /// </summary>
        /// <remarks>
        /// Explicitly excluded paths are omitted unless they represent a directory enumeration that did not create the
        /// directory.
        /// </remarks>
        private static bool ShouldIncludeAccess(
            AbsolutePath path,
            ReportedFileAccessesAndFlagsMutable access,
            HashSet<AbsolutePath> excludedPaths)
        {
            if (!excludedPaths.Contains(path))
            {
                return true;
            }

            return access.ObservationFlags.HasFlag(ObservationFlags.Enumeration)
                && !access.ReportedFileAccesses.Any(reportedAccess => reportedAccess.IsDirectoryCreation());
        }

        /// <summary>
        /// Creates deterministic mixed sandbox reports with declared outputs, shared opaque writes, exclusions,
        /// enumerations, absent paths, and path-casing differences.
        /// </summary>
        private static BenchmarkData CreateData(PathTable pathTable)
        {
            string root = OperatingSystemHelper.IsUnixOS ? "/observed-access-benchmark" : @"C:\observed-access-benchmark";
            var accesses = new AccessSample[UniquePathCount * AccessesPerPath];
            var declaredOutputs = new HashSet<AbsolutePath>();
            var excludedPaths = new HashSet<AbsolutePath>();
            var process = new ReportedProcess(1, Path.Combine(root, "tools", "compiler.exe"));

            for (int i = 0; i < UniquePathCount; i++)
            {
                string pathString = Path.Combine(
                    root,
                    $"shard-{i % 64:D2}",
                    $"project-{i:D5}",
                    i % 7 == 0 ? $"directory-{i:D5}" : $"input-{i:D5}.dat");
                AbsolutePath path = AbsolutePath.Create(pathTable, pathString);
                bool declaredOutput = i % 17 == 0;
                bool sharedOpaqueOutput = !declaredOutput && i % 19 == 0;
                bool excluded = i % 23 == 0;
                bool directoryProbe = i % 7 == 0;
                bool absent = i % 5 == 0;

                if (declaredOutput)
                {
                    declaredOutputs.Add(path);
                }

                if (excluded)
                {
                    excludedPaths.Add(path);
                }

                for (int accessIndex = 0; accessIndex < AccessesPerPath; accessIndex++)
                {
                    RequestedAccess requestedAccess = accessIndex switch
                    {
                        0 => RequestedAccess.Probe,
                        1 => RequestedAccess.Read,
                        2 => directoryProbe ? RequestedAccess.Enumerate : RequestedAccess.Probe,
                        _ => sharedOpaqueOutput ? RequestedAccess.Write : RequestedAccess.Read,
                    };
                    ObservationFlags flags = directoryProbe
                        ? ObservationFlags.DirectoryLocation | ObservationFlags.Enumeration
                        : requestedAccess == RequestedAccess.Probe ? ObservationFlags.FileProbe : ObservationFlags.None;
                    string reportedPath = accessIndex == 2 && OperatingSystemHelper.IsWindowsOS
                        ? pathString.ToUpperInvariant()
                        : pathString;
                    AbsolutePath reportedAbsolutePath = AbsolutePath.Create(pathTable, reportedPath);
                    var reportedAccess = new ReportedFileAccess(
                        directoryProbe && accessIndex == 3 ? ReportedFileOperation.CreateDirectory : ReportedFileOperation.GetFileAttributes,
                        process,
                        requestedAccess,
                        declaredOutput ? FileAccessStatus.Allowed : FileAccessStatus.Denied,
                        explicitlyReported: true,
                        error: absent ? (uint)ReportedFileAccess.ERROR_FILE_NOT_FOUND : 0,
                        rawError: absent ? (uint)ReportedFileAccess.ERROR_FILE_NOT_FOUND : 0,
                        ReportedFileAccess.NoUsn,
                        DesiredAccess.GENERIC_READ,
                        ShareMode.FILE_SHARE_READ,
                        CreationDisposition.OPEN_EXISTING,
                        directoryProbe ? FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY : FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                        reportedAbsolutePath,
                        reportedPath,
                        enumeratePattern: directoryProbe ? "*" : null,
                        fileAccessStatusMethod: declaredOutput ? FileAccessStatusMethod.PolicyBased : FileAccessStatusMethod.FileExistenceBased);

                    accesses[(i * AccessesPerPath) + accessIndex] =
                        new AccessSample(reportedAbsolutePath, flags, sharedOpaqueOutput, reportedAccess);
                }
            }

            Shuffle(accesses);
            return new BenchmarkData(accesses, declaredOutputs, excludedPaths);
        }

        /// <summary>
        /// Randomizes report arrival order with a fixed seed so all benchmark runs process identical input.
        /// </summary>
        private static void Shuffle(AccessSample[] accesses)
        {
            var random = new Random(0x5EED);
            for (int i = accesses.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (accesses[i], accesses[swapIndex]) = (accesses[swapIndex], accesses[i]);
            }
        }

        /// <summary>
        /// Verifies that both strategies produce the same ordered paths, flags, and per-path access counts.
        /// </summary>
        private static void AssertEquivalent(ObservedFileAccess[] expected, ObservedFileAccess[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].Path, actual[i].Path);
                Assert.Equal(expected[i].ObservationFlags, actual[i].ObservationFlags);
                Assert.Equal(expected[i].Accesses.Count, actual[i].Accesses.Count);
            }
        }

        /// <summary>
        /// Enumerates the result and produces a checksum that prevents unused benchmark output.
        /// </summary>
        private static long GetChecksum(ObservedFileAccess[] accesses)
        {
            long checksum = 0;
            foreach (ObservedFileAccess access in accesses)
            {
                checksum = unchecked(
                    checksum + access.Path.GetHashCode() + (int)access.ObservationFlags + access.Accesses.Count);
            }

            return checksum;
        }

        private static void ForceCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private readonly record struct AccessSample(
            AbsolutePath Path,
            ObservationFlags Flags,
            bool IsSharedOpaqueOutput,
            ReportedFileAccess Access);

        private sealed record BenchmarkData(
            AccessSample[] Accesses,
            HashSet<AbsolutePath> DeclaredOutputs,
            HashSet<AbsolutePath> ExcludedPaths);
    }
}
