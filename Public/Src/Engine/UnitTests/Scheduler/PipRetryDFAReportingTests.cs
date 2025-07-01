// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class PipRetryDFAReportingTests : ProcessReportingTestBase
    {
        public PipRetryDFAReportingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>
        /// Validates the handling of duplicates in AllowedUndeclaredReads during retries.
        /// </summary>
        /// <remarks>
        /// This test focuses on two scenarios:
        /// 1.) Different ObservedInputTypes for the Same Path:
        /// If different ObservedInputTypes are reported for the same path across retries, the type with the highest precedence is chosen.
        /// For example, if `FileContentRead` and `AbsentPathProbe` are both reported for a path, we opt for `FileContentRead` as it reflects a more critical observation of the file's state.
        /// 2.) We can have the same type reported during both the retries, in this case we need to ensure we report the same type during both the retries without any errors.
        /// </remarks>
        [Theory]
        [InlineData(ObservedInputType.FileContentRead, ObservedInputType.AbsentPathProbe, ObservedInputType.FileContentRead)]
        [InlineData(ObservedInputType.AbsentPathProbe, ObservedInputType.AbsentPathProbe, ObservedInputType.AbsentPathProbe)]
        public void TestMergeAllowedUndeclaredReadWithDuplicates(ObservedInputType firstObservation, ObservedInputType retryObservation, ObservedInputType resolvedObservation)
        {
            var pathA = CreateSourceFile().Path;

            IEnumerable< ExecutionResult> allExecutionResults = new List<ExecutionResult>
            {
                CreateExecutionResult(new Dictionary<AbsolutePath, ObservedInputType>
                {
                    { pathA, firstObservation },
                    { CreateSourceFile().Path, ObservedInputType.DirectoryEnumeration },
                }),

                CreateExecutionResult(new Dictionary<AbsolutePath, ObservedInputType>
                {
                    { CreateSourceFile().Path, ObservedInputType.ExistingFileProbe },
                }),

                CreateExecutionResult(new Dictionary<AbsolutePath, ObservedInputType>
                {
                    { pathA, retryObservation },
                    { CreateSourceFile().Path, ObservedInputType.FileContentRead },
                }),
            };

            var allowedUndeclaredRead = PipExecutor.MergeAllowedUndeclaredReads(allExecutionResults);

            XAssert.AreEqual(resolvedObservation, allowedUndeclaredRead[pathA]);
        }

        [Fact]
        public void TestMergeFileAccessesBeforeFirstUndeclaredReWrite()
        {
            var pathA = CreateSourceFile().Path;

            IEnumerable<ExecutionResult> allExecutionResults = new List<ExecutionResult>
            {
                CreateExecutionResult(fileAccessesBeforeFirstUndeclaredReWrite: new Dictionary<AbsolutePath, RequestedAccess>
                {
                    { pathA, RequestedAccess.Probe },
                }),

                CreateExecutionResult(fileAccessesBeforeFirstUndeclaredReWrite: new Dictionary<AbsolutePath, RequestedAccess>
                {
                    { pathA, RequestedAccess.Enumerate },
                }),
            };

            PipExecutor.MergeAllDynamicAccessesAndViolations(allExecutionResults, out _, out _, out _, out _, out var fileAccessesBeforeFirstUndeclaredRewrite);

            // We only care about the first execution result.
            XAssert.AreEqual(RequestedAccess.Probe, fileAccessesBeforeFirstUndeclaredRewrite[pathA]);
        }
    }
}