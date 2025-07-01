// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    public abstract class ProcessReportingTestBase : SchedulerTestBase
    {
        protected ProcessReportingTestBase(ITestOutputHelper output)
            : base(output)
        {
        }

        protected static void AssertEqual(ReportedFileAccess expected, ReportedFileAccess actual)
        {
            AssertEqual(expected, actual,
               r => r.Operation,
               r => r.Process.Path,
               r => r.Process.ProcessId,
               r => r.RequestedAccess,
               r => r.Status,
               r => r.ExplicitlyReported,
               r => r.Error,
               r => r.Usn,
               r => r.DesiredAccess,
               r => r.ShareMode,
               r => r.CreationDisposition,
               r => r.FlagsAndAttributes,
               r => r.ManifestPath,
               r => r.Path,
               r => r.EnumeratePattern
            );
        }

        protected static void AssertSequenceEqual<T>(IReadOnlyCollection<T> expectedList, IReadOnlyCollection<T> actualList, params Action<T, T>[] equalityVerifiers)
        {
            Assert.Equal(expectedList.Count, actualList.Count);

            var zippedLists = expectedList.Zip(actualList, (x, y) => (x, y));

            foreach (var entry in zippedLists)
            {
                var expected = entry.Item1;
                var actual = entry.Item2;

                foreach (var equalityVerifier in equalityVerifiers)
                {
                    equalityVerifier(expected, actual);
                }
            }
        }

        protected static void AssertEqual<T>(T expected, T actual, params Func<T, object>[] getters)
        {
            int i = 0;
            foreach (var getter in getters)
            {
                var expectedValue = getter(expected);
                var actualValue = getter(expected);
                XAssert.AreEqual(expectedValue, actualValue, I($"Unequality value for getter ({i})"));
                i++;
            }
        }

        protected ReportedFileAccess CreateRandomReportedFileAccess(ReportedProcess process = null)
        {
            Random r = new Random(123);

            var manifestFile = CreateOutputFile();
            return new ReportedFileAccess(
                RandomEnum<ReportedFileOperation>(r),
                process ?? new ReportedProcess((uint)r.Next(), X("/x/processPath") + r.Next(), X("/x/processPath") + r.Next() + " args1 args2"),
                RandomEnum<RequestedAccess>(r),
                RandomEnum<FileAccessStatus>(r),
                true,
                (uint)r.Next(),
                (uint)r.Next(),
                new Usn((ulong)r.Next()),
                RandomEnum<DesiredAccess>(r),
                RandomEnum<ShareMode>(r),
                RandomEnum<CreationDisposition>(r),
                RandomEnum<FlagsAndAttributes>(r),
                manifestFile.Path,
                X("/j/accessPath") + r.Next(),
                null);
        }

        protected ReportedProcess CreateRandomReportedProcess()
        {
            Random r = new Random(123);

            return new ReportedProcess((uint)r.Next(), X("/x/processPath") + r.Next(), X("/x/processPath") + r.Next() + " args1 args2");
        }

        protected TEnum RandomEnum<TEnum>(Random r) where TEnum : struct
        {
            TEnum[] values = (TEnum[])Enum.GetValues(typeof(TEnum));
            return values[r.Next(0, values.Length)];
        }

        protected (FileArtifact, FileMaterializationInfo, PipOutputOrigin) CreateRandomOutputContent(int? seed = null)
        {
            Random r = seed.HasValue ? new Random(seed.Value) : new Random();
            var outputFile = CreateOutputFile();
            var contentHash = ContentHashingUtilities.CreateRandom();
            var fileContentInfo = new FileMaterializationInfo(
                new FileContentInfo(contentHash, r.Next(0, 102400)),
                outputFile.Path.GetName(Context.PathTable),
                opaqueDirectoryRoot: AbsolutePath.Invalid,
                dynamicOutputCaseSensitiveRelativeDirectory: RelativePath.Invalid);
            return (outputFile, fileContentInfo, PipOutputOrigin.Produced);
        }

        protected (DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>) CreateRandomOutputDirectory(int? seed = null)
        {
            Random r = seed.HasValue ? new Random(seed.Value) : new Random();
            int length = r.Next(1, 10);
            var names = new RelativePath[length];

            for (int i = 0; i < length; ++i)
            {
                names[i] = RelativePath.Create(Context.StringTable, "random_file_" + i);
            }

            return CreateOutputDirectory(relativePathToMembers: names);
        }

        protected static ContentHash[] CreateRandomContentHashArray(int? seed = null)
        {
            Random r = seed.HasValue ? new Random(seed.Value) : new Random();
            var length = r.Next(0, 10);
            var result = new ContentHash[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = ContentHash.Random();
            }

            return result;
        }

        protected ExecutionResult CreateExecutionResult(IReadOnlyDictionary<AbsolutePath, ObservedInputType> allowedUndeclaredReads = null, IReadOnlyDictionary<AbsolutePath, RequestedAccess> fileAccessesBeforeFirstUndeclaredReWrite = null)
        {
            var reportedAccess = CreateRandomReportedFileAccess();

            Fingerprint fingerprint = FingerprintUtilities.CreateRandom();

            return ExecutionResult.CreateSealed(
                result: PipResultStatus.Succeeded,
                numberOfWarnings: 12,
                outputContent: ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.FromWithoutCopy(CreateRandomOutputContent(123), CreateRandomOutputContent(123)),
                directoryOutputs: ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)>.FromWithoutCopy(CreateRandomOutputDirectory(37), CreateRandomOutputDirectory(37)),
                performanceInformation: new ProcessPipExecutionPerformance(
                    PipExecutionLevel.Executed,
                    DateTime.UtcNow,
                    DateTime.UtcNow + TimeSpan.FromMinutes(2),
                    FingerprintUtilities.ZeroFingerprint,
                    TimeSpan.FromMinutes(2),
                    new FileMonitoringViolationCounters(2, 3, 4),
                    default(IOCounters),
                    TimeSpan.FromMinutes(3),
                    TimeSpan.FromMinutes(3),
                    ProcessMemoryCounters.CreateFromBytes(12324, 12325),
                    33,
                    7,
                    0,
                    42),
                fingerprint: new WeakContentFingerprint(fingerprint),
                fileAccessViolationsNotAllowlisted: new[]
                {
                    reportedAccess,
                    CreateRandomReportedFileAccess(),

                    // Create reported file access that uses the same process to test deduplication during deserialization
                    CreateRandomReportedFileAccess(reportedAccess.Process),
                },
                allowlistedFileAccessViolations: new ReportedFileAccess[0],
                mustBeConsideredPerpetuallyDirty: true,
                dynamicObservations: ReadOnlyArray<(AbsolutePath, DynamicObservationKind)>.FromWithoutCopy(
                    (CreateSourceFile().Path, DynamicObservationKind.ObservedFile),
                    (CreateSourceFile().Path, DynamicObservationKind.ObservedFile),
                    (CreateSourceFile().Path, DynamicObservationKind.ProbedFile),
                    (CreateSourceFile().Path, DynamicObservationKind.ProbedFile),
                    (CreateSourceFile().Path, DynamicObservationKind.ProbedFile),
                    (CreateSourceFile().Path, DynamicObservationKind.Enumeration),
                    (CreateSourceFile().Path, DynamicObservationKind.AbsentPathProbeUnderOutputDirectory),
                    (CreateSourceFile().Path, DynamicObservationKind.AbsentPathProbeUnderOutputDirectory)
                ),
                allowedUndeclaredSourceReads: allowedUndeclaredReads ??  new Dictionary<AbsolutePath, ObservedInputType>
                {
                    [CreateSourceFile().Path] = ObservedInputType.FileContentRead,
                    [CreateSourceFile().Path] = ObservedInputType.FileContentRead
                },
                fileAccessesBeforeFirstUndeclaredReWrite: fileAccessesBeforeFirstUndeclaredReWrite ?? new Dictionary<AbsolutePath, RequestedAccess>
                {
                    [CreateSourceFile().Path] = RequestedAccess.Probe | RequestedAccess.Enumerate,
                },
                twoPhaseCachingInfo: new TwoPhaseCachingInfo(
                    new WeakContentFingerprint(Fingerprint.Random(FingerprintUtilities.FingerprintLength)),
                    ContentHashingUtilities.CreateRandom(),
                    new StrongContentFingerprint(Fingerprint.Random(FingerprintUtilities.FingerprintLength)),
                    new CacheEntry(ContentHashingUtilities.CreateRandom(), null, CreateRandomContentHashArray(42))),
                pipCacheDescriptorV2Metadata: null,
                converged: true,
                cacheLookupStepDurations: null,
                pipProperties: new Dictionary<string, int> { { "Foo", 1 }, { "Bar", 9 } },
                createdDirectories: new ReadOnlyHashSet<AbsolutePath> {
                    CreateSourceFile().Path
                },
                hasUserRetries: true,
                exitCode: 0,
                cacheMissType: PipCacheMissType.Hit);
        }
    }
}
