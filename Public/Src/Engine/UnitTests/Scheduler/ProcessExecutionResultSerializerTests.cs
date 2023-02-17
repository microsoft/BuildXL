// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Native.IO;
using System.Collections.Generic;

namespace Test.BuildXL.Scheduler
{
    public sealed class ProcessExecutionResultSerializerTests : ProcessReportingTestBase
    {
        public ProcessExecutionResultSerializerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestProcessExecutionResultSerialization()
        {
            var reportedAccess = CreateRandomReportedFileAccess();

            Fingerprint fingerprint = FingerprintUtilities.CreateRandom();

            var processExecutionResult = ExecutionResult.CreateSealed(
                result: PipResultStatus.Succeeded,
                numberOfWarnings: 12,
                outputContent: ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.FromWithoutCopy(CreateRandomOutputContent(), CreateRandomOutputContent()),
                directoryOutputs: ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>)>.FromWithoutCopy(CreateRandomOutputDirectory(), CreateRandomOutputDirectory()), 
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
                    ProcessMemoryCounters.CreateFromBytes(12324, 12325, 12326, 12326),
                    33,
                    7,
                    0),
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
                allowedUndeclaredSourceReads: new ReadOnlyHashSet<AbsolutePath> {
                    CreateSourceFile().Path,
                    CreateSourceFile().Path
                },
                twoPhaseCachingInfo: new TwoPhaseCachingInfo(
                    new WeakContentFingerprint(Fingerprint.Random(FingerprintUtilities.FingerprintLength)),
                    ContentHashingUtilities.CreateRandom(),
                    new StrongContentFingerprint(Fingerprint.Random(FingerprintUtilities.FingerprintLength)),
                    new CacheEntry(ContentHashingUtilities.CreateRandom(), null, CreateRandomContentHashArray())),
                pipCacheDescriptorV2Metadata: null,
                converged: true,
                pathSet: null,
                cacheLookupStepDurations: null,
                pipProperties: new Dictionary<string, int> { { "Foo", 1 }, { "Bar", 9 } },
                createdDirectories: new ReadOnlyHashSet<AbsolutePath> {
                    CreateSourceFile().Path
                },
                hasUserRetries: true,
                exitCode: 0,
                cacheMissType: PipCacheMissType.Hit);

            ExecutionResultSerializer serializer = new ExecutionResultSerializer(0, Context);

            ExecutionResult deserializedProcessExecutionResult;

            using (var stream = new MemoryStream())
            using (var writer = new BuildXLWriter(false, stream, true, false))
            using (var reader = new BuildXLReader(false, stream, true))
            {
                serializer.Serialize(writer, processExecutionResult, preservePathCasing: false);

                var position = stream.Position;
                stream.Position = 0;

                deserializedProcessExecutionResult = serializer.Deserialize(reader,
                    processExecutionResult.PerformanceInformation.WorkerId);

                // make sure we read the same amount of content from the stream we wrote there
                XAssert.AreEqual(position, stream.Position);
            }

            // Ensure successful pip result is changed to not materialized.
            XAssert.AreEqual(PipResultStatus.NotMaterialized, deserializedProcessExecutionResult.Result);

            AssertEqual(processExecutionResult, deserializedProcessExecutionResult,
                r => r.NumberOfWarnings,
                r => r.Converged,

                r => r.OutputContent.Length,
                r => r.DirectoryOutputs.Length,

                r => r.PerformanceInformation.ExecutionLevel,
                r => r.PerformanceInformation.ExecutionStop,
                r => r.PerformanceInformation.ExecutionStart,
                r => r.PerformanceInformation.ProcessExecutionTime,
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessViolationsNotAllowlisted,
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessesAllowlistedAndCacheable,
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessesAllowlistedButNotCacheable,
                r => r.PerformanceInformation.UserTime,
                r => r.PerformanceInformation.KernelTime,
                r => r.PerformanceInformation.MemoryCounters.PeakWorkingSetMb,
                r => r.PerformanceInformation.MemoryCounters.AverageWorkingSetMb,
                r => r.PerformanceInformation.MemoryCounters.PeakCommitSizeMb,
                r => r.PerformanceInformation.MemoryCounters.AverageCommitSizeMb,

                r => r.PerformanceInformation.NumberOfProcesses,

                r => r.FileAccessViolationsNotAllowlisted.Count,
                r => r.MustBeConsideredPerpetuallyDirty,
                r => r.DynamicObservations.Length,
                r => r.AllowedUndeclaredReads.Count,

                r => r.TwoPhaseCachingInfo.WeakFingerprint,
                r => r.TwoPhaseCachingInfo.StrongFingerprint,
                r => r.TwoPhaseCachingInfo.PathSetHash,
                r => r.TwoPhaseCachingInfo.CacheEntry.MetadataHash,
                r => r.TwoPhaseCachingInfo.CacheEntry.OriginatingCache,
                r => r.TwoPhaseCachingInfo.CacheEntry.ReferencedContent.Length,

                r => r.PipProperties.Count,
                r => r.HasUserRetries,
                r => r.CreatedDirectories,
                r => r.RetryInfo,
                r => r.ExitCode,
                r => r.CacheMissType
                );

            for (int i = 0; i < processExecutionResult.OutputContent.Length; i++)
            {
                int j = i;
                AssertEqual(processExecutionResult, deserializedProcessExecutionResult,
                   r => r.OutputContent[j].Item1,
                   r => r.OutputContent[j].Item2
                );

                // Ensure that output origin from deserialzed output content is changed to not materialized.
                XAssert.AreEqual(PipOutputOrigin.NotMaterialized, deserializedProcessExecutionResult.OutputContent[i].Item3);
            }

            for (int i = 0; i < processExecutionResult.DirectoryOutputs.Length; i++)
            {
                var expected = processExecutionResult.DirectoryOutputs[i];
                var result = deserializedProcessExecutionResult.DirectoryOutputs[i];
                XAssert.AreEqual(expected.Item1, result.Item1);
                XAssert.AreEqual(expected.Item2.Length, result.Item2.Length);

                for (int j = 0; j < expected.Item2.Length; j++)
                {
                    XAssert.AreEqual(expected.Item2[j], result.Item2[j]);
                }
            }

            for (int i = 0; i < processExecutionResult.FileAccessViolationsNotAllowlisted.Count; i++)
            {
                // Compare individual fields for ReportedFileAccess since it uses reference
                // equality for reported process which would not work for serialization/deserialization
                AssertEqual(processExecutionResult.FileAccessViolationsNotAllowlisted[i], deserializedProcessExecutionResult.FileAccessViolationsNotAllowlisted[i]);
            }

            // Ensure that reported process instances are deduplicated.
            XAssert.AreSame(deserializedProcessExecutionResult.FileAccessViolationsNotAllowlisted[0].Process,
                deserializedProcessExecutionResult.FileAccessViolationsNotAllowlisted[2].Process);

            for (int i = 0; i < processExecutionResult.DynamicObservations.Length; i++)
            {
                AssertEqual(processExecutionResult.DynamicObservations[i], deserializedProcessExecutionResult.DynamicObservations[i]);
            }

            XAssert.AreSetsEqual(processExecutionResult.AllowedUndeclaredReads, deserializedProcessExecutionResult.AllowedUndeclaredReads, expectedResult: true);

            var referencedContentLength = processExecutionResult.TwoPhaseCachingInfo.CacheEntry.ReferencedContent.Length;
            for (int i = 0; i < referencedContentLength; i++)
            {
                XAssert.AreEqual(
                    processExecutionResult.TwoPhaseCachingInfo.CacheEntry.ReferencedContent[i],
                    deserializedProcessExecutionResult.TwoPhaseCachingInfo.CacheEntry.ReferencedContent[i]);
            }

            XAssert.AreEqual(9, deserializedProcessExecutionResult.PipProperties["Bar"]);

            XAssert.AreSetsEqual(processExecutionResult.CreatedDirectories, deserializedProcessExecutionResult.CreatedDirectories, expectedResult: true);
        }

        private (FileArtifact, FileMaterializationInfo, PipOutputOrigin) CreateRandomOutputContent()
        {
            Random r = new Random();
            var outputFile = CreateOutputFile();
            var contentHash = ContentHashingUtilities.CreateRandom();
            var fileContentInfo = new FileMaterializationInfo(
                new FileContentInfo(contentHash, r.Next(0, 102400)), 
                outputFile.Path.GetName(Context.PathTable));
            return (outputFile, fileContentInfo, PipOutputOrigin.Produced);
        }

        private (DirectoryArtifact, ReadOnlyArray<FileArtifactWithAttributes>) CreateRandomOutputDirectory()
        {
            Random r = new Random();
            int length = r.Next(1, 10);
            var names = new RelativePath[length];

            for (int i = 0; i < length; ++i)
            {
                names[i] = RelativePath.Create(Context.StringTable, "random_file_" + i);
            }

            return CreateOutputDirectory(relativePathToMembers: names);
        }

        private static ContentHash[] CreateRandomContentHashArray()
        {
            Random r = new Random();
            var length = r.Next(0, 10);
            var result = new ContentHash[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = ContentHash.Random();
            }

            return result;
        }
    }
}
