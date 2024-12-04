// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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

            var processExecutionResult = CreateExecutionResult();

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
                r => r.PerformanceInformation.PushOutputsToCacheDurationMs,

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
    }
}
