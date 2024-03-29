// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Scheduler.Tracing;
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
        [InlineData(true, Process.Options.RequireGlobalDependencies)]
        [InlineData(false, Process.Options.RequireGlobalDependencies)]
        [InlineData(true, Process.Options.None)]
        [InlineData(false, Process.Options.None)]
        public void TranslateGlobalUntrackedScope(bool translate, Process.Options requireGlobalDependencies)
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

            builder.Options |= requireGlobalDependencies;

            Process pip = SchedulePipBuilder(builder).Process;

            if (translate && ((requireGlobalDependencies & Process.Options.RequireGlobalDependencies) == Process.Options.RequireGlobalDependencies) )
            {
                RunScheduler().AssertCacheMiss(pip.PipId);
                RunScheduler().AssertCacheHit(pip.PipId);
            }
            else
            {
                RunScheduler().AssertFailure();
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
        }
    }
}
