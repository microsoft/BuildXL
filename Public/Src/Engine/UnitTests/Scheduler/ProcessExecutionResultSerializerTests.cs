// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage;
using BuildXL.Utilities;
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
                directoryOutputs: ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.FromWithoutCopy(CreateRandomOutputDirectory(), CreateRandomOutputDirectory()), 
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
                    12324,
                    33,
                    7),
                fingerprint: new WeakContentFingerprint(fingerprint), 
                fileAccessViolationsNotWhitelisted: new[]
                {
                    reportedAccess,
                    CreateRandomReportedFileAccess(),

                    // Create reported file access that uses the same process to test deduplication during deserialization
                    CreateRandomReportedFileAccess(reportedAccess.Process),
                },
                whitelistedFileAccessViolations: new ReportedFileAccess[0],
                mustBeConsideredPerpetuallyDirty: true,
                dynamicallyObservedFiles: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(
                    CreateSourceFile().Path,
                    CreateSourceFile().Path
                ),
                dynamicallyObservedEnumerations: ReadOnlyArray<AbsolutePath>.FromWithoutCopy(
                    CreateSourceFile().Path
                ),
                allowedUndeclaredSourceReads: new ReadOnlyHashSet<AbsolutePath> {
                    CreateSourceFile().Path,
                    CreateSourceFile().Path
                },
                absentPathProbesUnderOutputDirectories: new ReadOnlyHashSet<AbsolutePath> {
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
                hasUserRetries: true);

            ExecutionResultSerializer serializer = new ExecutionResultSerializer(0, Context);

            ExecutionResult deserializedProcessExecutionResult;

            using (var stream = new MemoryStream())
            using (var writer = new BuildXLWriter(false, stream, true, false))
            using (var reader = new BuildXLReader(false, stream, true))
            {
                serializer.Serialize(writer, processExecutionResult);

                stream.Position = 0;

                deserializedProcessExecutionResult = serializer.Deserialize(reader,
                    processExecutionResult.PerformanceInformation.WorkerId);
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
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted,
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessesWhitelistedAndCacheable,
                r => r.PerformanceInformation.FileMonitoringViolations.NumFileAccessesWhitelistedButNotCacheable,
                r => r.PerformanceInformation.UserTime,
                r => r.PerformanceInformation.KernelTime,
                r => r.PerformanceInformation.PeakMemoryUsage,
                r => r.PerformanceInformation.NumberOfProcesses,

                r => r.FileAccessViolationsNotWhitelisted.Count,
                r => r.MustBeConsideredPerpetuallyDirty,
                r => r.DynamicallyObservedFiles.Length,
                r => r.DynamicallyObservedEnumerations.Length,
                r => r.AllowedUndeclaredReads.Count,

                r => r.TwoPhaseCachingInfo.WeakFingerprint,
                r => r.TwoPhaseCachingInfo.StrongFingerprint,
                r => r.TwoPhaseCachingInfo.PathSetHash,
                r => r.TwoPhaseCachingInfo.CacheEntry.MetadataHash,
                r => r.TwoPhaseCachingInfo.CacheEntry.OriginatingCache,
                r => r.TwoPhaseCachingInfo.CacheEntry.ReferencedContent.Length,

                r => r.PipProperties.Count,
                r => r.HasUserRetries
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

            for (int i = 0; i < processExecutionResult.FileAccessViolationsNotWhitelisted.Count; i++)
            {
                // Compare individual fields for ReportedFileAccess since it uses reference
                // equality for reported process which would not work for serialization/deserialization
                AssertEqual(processExecutionResult.FileAccessViolationsNotWhitelisted[i], deserializedProcessExecutionResult.FileAccessViolationsNotWhitelisted[i]);
            }

            // Ensure that reported process instances are deduplicated.
            XAssert.AreSame(deserializedProcessExecutionResult.FileAccessViolationsNotWhitelisted[0].Process,
                deserializedProcessExecutionResult.FileAccessViolationsNotWhitelisted[2].Process);

            for (int i = 0; i < processExecutionResult.DynamicallyObservedFiles.Length; i++)
            {
                AssertEqual(processExecutionResult.DynamicallyObservedFiles[i], deserializedProcessExecutionResult.DynamicallyObservedFiles[i]);
            }

            for (int i = 0; i < processExecutionResult.DynamicallyObservedEnumerations.Length; i++)
            {
                AssertEqual(processExecutionResult.DynamicallyObservedEnumerations[i], deserializedProcessExecutionResult.DynamicallyObservedEnumerations[i]);
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

        private (DirectoryArtifact, ReadOnlyArray<FileArtifact>) CreateRandomOutputDirectory()
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
