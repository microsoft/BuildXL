// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    [TestClassIfSupported(requiresSandbox: true)]
    public class SharedOpaqueDirectoryTests_ExperimentalSharedOpaqueTracking : SharedOpaqueDirectoryTests
    {
        public SharedOpaqueDirectoryTests_ExperimentalSharedOpaqueTracking(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.ExperimentalSharedOpaqueTracking = true;
        }

        /// <summary>
        /// Experimental tracking allows sandbox accesses but still rejects undeclared dependencies after execution.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExperimentalSharedOpaqueTrackingValidatesAccessesAfterExecution(bool declareDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact sharedOutput = CreateOutputFileArtifact(sharedOpaqueDir);
            const string SharedContent = "shared opaque content";
            var producer = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: CreateSourceFile(),
                new KeyValuePair<FileArtifact, string>(sharedOutput, SharedContent));

            FileArtifact consumerOutput = CreateOutputFileArtifact();
            var consumerBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadAndWriteFile(sharedOutput, consumerOutput, doNotInfer: true),
            });
            consumerBuilder.AddOutputFile(consumerOutput.Path);
            consumerBuilder.AddOrderDependency(producer.Process.PipId);
            if (declareDependency)
            {
                consumerBuilder.AddInputDirectory(producer.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            }

            SchedulePipBuilder(consumerBuilder);

            // RunScheduler copies and normalizes Configuration through BuildXLEngine before creating the scheduler.
            // Do not set FailUnexpectedFileAccesses here: this must exercise the feature's automatic configuration.
            var result = RunScheduler();
            if (declareDependency)
            {
                result.AssertSuccess();
            }
            else
            {
                result.AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }

            // Check the actual content: ReadAndWriteFile catches access-denied errors and writes random content instead.
            XAssert.AreEqual(SharedContent, File.ReadAllText(consumerOutput.Path.ToString(Context.PathTable)));
        }
    }
}
