// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Verifies filtering, aggregation, and deterministic ordering of observed file accesses after sandbox processing.
    /// </summary>
    /// <remarks>
    /// Coverage includes shared opaque outputs, explicitly excluded paths, directory enumerations, directory creation,
    /// multiple accesses to the same path, and sorting with the expanded-path comparer.
    /// </remarks>
    public sealed class ObservedFileAccessCollectionTests
    {
        /// <summary>
        /// Verifies representative inclusion and exclusion rules while preserving all accesses for each retained path.
        /// </summary>
        [Fact]
        public void FiltersAndSortsObservedAccesses()
        {
            var pathTable = new PathTable();
            var accessesByPath = new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>();
            var excludedPaths = new HashSet<AbsolutePath>();

            AbsolutePath zPath = CreatePath(pathTable, "z.txt");
            AbsolutePath aPath = CreatePath(pathTable, "a.txt");
            AbsolutePath sharedOutput = CreatePath(pathTable, "shared-output.txt");
            AbsolutePath omittedSharedOutput = CreatePath(pathTable, "omitted-shared-output.txt");
            AbsolutePath excludedProbe = CreatePath(pathTable, "excluded-probe.txt");
            AbsolutePath excludedEnumeration = CreatePath(pathTable, "excluded-enumeration");
            AbsolutePath createdEnumeration = CreatePath(pathTable, "created-enumeration");

            accessesByPath.Add(zPath, CreateAccess(ObservationFlags.None, CreateReportedAccess(zPath, RequestedAccess.Read)));
            accessesByPath[zPath].ReportedFileAccesses =
                accessesByPath[zPath].ReportedFileAccesses.Add(CreateReportedAccess(zPath, RequestedAccess.Probe));
            accessesByPath.Add(aPath, CreateAccess(ObservationFlags.FileProbe, CreateReportedAccess(aPath, RequestedAccess.Probe)));
            accessesByPath.Add(
                sharedOutput,
                CreateAccess(ObservationFlags.None, CreateReportedAccess(sharedOutput, RequestedAccess.Write), isSharedOpaqueOutput: true));
            accessesByPath.Add(
                omittedSharedOutput,
                CreateAccess(
                    ObservationFlags.None,
                    CreateReportedAccess(omittedSharedOutput, RequestedAccess.Read),
                    excludeFromObservedFileAccesses: true));
            accessesByPath.Add(
                excludedProbe,
                CreateAccess(ObservationFlags.FileProbe, CreateReportedAccess(excludedProbe, RequestedAccess.Probe)));
            accessesByPath.Add(
                excludedEnumeration,
                CreateAccess(ObservationFlags.Enumeration, CreateReportedAccess(excludedEnumeration, RequestedAccess.Enumerate)));
            accessesByPath.Add(
                createdEnumeration,
                CreateAccess(
                    ObservationFlags.Enumeration,
                    CreateReportedAccess(createdEnumeration, RequestedAccess.Write, ReportedFileOperation.CreateDirectory)));

            excludedPaths.Add(excludedProbe);
            excludedPaths.Add(excludedEnumeration);
            excludedPaths.Add(createdEnumeration);

            var comparer = new ObservedFileAccessExpandedPathComparer(pathTable.ExpandedPathComparer);
            ObservedFileAccess[] result = SandboxedProcessPipExecutor.CreateSortedObservedFileAccesses(accessesByPath, excludedPaths, comparer);

            Assert.Equal(new[] { aPath, excludedEnumeration, zPath }, result.Select(access => access.Path));
            Assert.Equal(2, result.Single(access => access.Path == zPath).Accesses.Count);
            Assert.DoesNotContain(result, access => access.Path == sharedOutput);
            Assert.DoesNotContain(result, access => access.Path == omittedSharedOutput);
            Assert.DoesNotContain(result, access => access.Path == excludedProbe);
            Assert.DoesNotContain(result, access => access.Path == createdEnumeration);
        }

        /// <summary>
        /// Stress-validates that a large reverse-ordered input produces every eligible path exactly once in expanded-path order.
        /// </summary>
        /// <remarks>
        /// The 50,000-path workload mixes shared opaque outputs, excluded probes, valid excluded enumerations, and
        /// excluded directory creations. Every retained path carries both a probe and a read access.
        /// </remarks>
        [Fact]
        public void StressPreservesEveryEligiblePathInExpandedPathOrder()
        {
            const int pathCount = 50_000;
            var pathTable = new PathTable();
            var accessesByPath = new Dictionary<AbsolutePath, ReportedFileAccessesAndFlagsMutable>(pathCount);
            var excludedPaths = new HashSet<AbsolutePath>();
            int expectedCount = 0;

            for (int i = pathCount - 1; i >= 0; i--)
            {
                AbsolutePath path = CreatePath(pathTable, $"shard-{i % 64:D2}\\project-{i:D5}\\input-{i:D5}.txt");
                bool sharedOutput = i % 13 == 0;
                bool excluded = i % 11 == 0;
                bool enumeration = i % 7 == 0;
                bool directoryCreation = excluded && enumeration && i % 5 == 0;
                ObservationFlags flags = enumeration ? ObservationFlags.Enumeration : ObservationFlags.FileProbe;
                ReportedFileOperation operation = directoryCreation ? ReportedFileOperation.CreateDirectory : ReportedFileOperation.GetFileAttributes;
                var access = CreateAccess(flags, CreateReportedAccess(path, RequestedAccess.Probe, operation), sharedOutput);
                access.ReportedFileAccesses = access.ReportedFileAccesses.Add(CreateReportedAccess(path, RequestedAccess.Read));
                accessesByPath.Add(path, access);

                if (excluded)
                {
                    excludedPaths.Add(path);
                }

                // Excluded enumerations remain observable unless the access created the directory.
                if (!sharedOutput && (!excluded || (enumeration && !directoryCreation)))
                {
                    expectedCount++;
                }
            }

            var comparer = new ObservedFileAccessExpandedPathComparer(pathTable.ExpandedPathComparer);
            ObservedFileAccess[] result = SandboxedProcessPipExecutor.CreateSortedObservedFileAccesses(accessesByPath, excludedPaths, comparer);

            Assert.Equal(expectedCount, result.Length);
            Assert.Equal(expectedCount, result.Select(access => access.Path).Distinct().Count());
            Assert.All(result, access => Assert.Equal(2, access.Accesses.Count));
            for (int i = 1; i < result.Length; i++)
            {
                Assert.True(comparer.Compare(result[i - 1], result[i]) < 0);
            }
        }

        /// <summary>
        /// Creates a platform-appropriate absolute path under the test root.
        /// </summary>
        private static AbsolutePath CreatePath(PathTable pathTable, string relativePath)
        {
            string root = OperatingSystemHelper.IsUnixOS ? "/observed-access-tests" : @"C:\observed-access-tests";
            return AbsolutePath.Create(pathTable, System.IO.Path.Combine(root, relativePath));
        }

        /// <summary>
        /// Creates the mutable per-path state consumed by <see cref="SandboxedProcessPipExecutor.CreateSortedObservedFileAccesses"/>.
        /// </summary>
        private static ReportedFileAccessesAndFlagsMutable CreateAccess(
            ObservationFlags flags,
            ReportedFileAccess access,
            bool isSharedOpaqueOutput = false,
            bool excludeFromObservedFileAccesses = false)
        {
            return new ReportedFileAccessesAndFlagsMutable
            {
                ObservationFlags = flags,
                ReportedFileAccesses = new CompactSet<ReportedFileAccess>().Add(access),
                IsSharedOpaqueOutput = isSharedOpaqueOutput,
                ExcludeFromObservedFileAccesses = excludeFromObservedFileAccesses,
            };
        }

        /// <summary>
        /// Creates an allowed explicit access with the requested operation for collection tests.
        /// </summary>
        private static ReportedFileAccess CreateReportedAccess(
            AbsolutePath path,
            RequestedAccess requestedAccess,
            ReportedFileOperation operation = ReportedFileOperation.GetFileAttributes)
        {
            return new ReportedFileAccess(
                operation,
                new ReportedProcess(1, "tool"),
                requestedAccess,
                FileAccessStatus.Allowed,
                explicitlyReported: true,
                error: 0,
                rawError: 0,
                ReportedFileAccess.NoUsn,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_READ,
                CreationDisposition.OPEN_EXISTING,
                FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                path,
                path: null,
                enumeratePattern: null);
        }
    }
}
