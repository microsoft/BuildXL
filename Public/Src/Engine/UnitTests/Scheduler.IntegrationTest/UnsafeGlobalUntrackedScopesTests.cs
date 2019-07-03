// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class UnsafeGlobalUntrackedScopesTests : SchedulerIntegrationTestBase
    {
        public UnsafeGlobalUntrackedScopesTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TranslateGlobalUntrackedScope(bool translate)
        {
            DirectoryArtifact sourceDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(SourceRoot, prefix: "sourceDir"));
            DirectoryArtifact targetDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(SourceRoot, prefix: "targetDir"));
            FileArtifact outputFileInTargetDir = CreateOutputFileArtifact(ArtifactToString(targetDirectory));
            FileArtifact inputFileInTargetDir = CreateSourceFile(ArtifactToString(targetDirectory));
            Configuration.Sandbox.GlobalUnsafeUntrackedScopes.Add(sourceDirectory);

            if (translate)
            {
                DirectoryTranslator = new DirectoryTranslator();
                DirectoryTranslator.AddTranslation(ArtifactToString(sourceDirectory), ArtifactToString(targetDirectory));
            }

            var ops = new Operation[]
            {
                Operation.ReadFile(inputFileInTargetDir, doNotInfer: true),
                Operation.WriteFile(outputFileInTargetDir)
            };

            var builder = CreatePipBuilder(ops);

            Process pip = SchedulePipBuilder(builder).Process;

            if (translate)
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
                RunScheduler().AssertCacheHit(pip.PipId);
            }
            else
            {
                RunScheduler().AssertFailure();
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
        }
    }
}
