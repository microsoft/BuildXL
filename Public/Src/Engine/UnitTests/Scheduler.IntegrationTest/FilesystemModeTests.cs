// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.Executables.TestProcess;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using BuildXL.Utilities.Core;
using BuildXL.Scheduler;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class FilesystemModeTests : SchedulerIntegrationTestBase
    {
        public FilesystemModeTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void EventualFilesystemUsedForProbe()
        {
            // pipA enumerates the directory where its output and pipB's output goes. It is forced to run before B because
            // B consumes its output
            var outA = CreateOutputFileArtifact(ObjectRoot);
            var opsA = new Operation[]
            {
                Operation.Probe(CreateSourceFile()),
                Operation.EnumerateDir(CreateOutputDirectoryArtifact(ObjectRoot)),
                Operation.WriteFile(outA)
            };
            Process pipA = CreateAndSchedulePipBuilder(opsA).Process;

            var opsB = new Operation[]
            {
                Operation.Probe(outA),
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
            };
            Process pipB = CreateAndSchedulePipBuilder(opsB).Process;

            // Perform build with full graph filesystem. Both output files should be produced
            ScheduleRunResult result = RunScheduler().AssertSuccess();
            result.AssertCacheMiss(pipA.PipId);
            result.AssertCacheMiss(pipB.PipId);

            // Perform build with full graph filesystem. Both processes should be a cache hit, even though the directory
            // that got enumerated changed state
            ScheduleRunResult result2 = RunScheduler().AssertSuccess();

            XAssert.AreEqual(PipResultStatus.UpToDate, result2.PipResults[pipA.PipId]);
            XAssert.AreEqual(PipResultStatus.UpToDate, result2.PipResults[pipB.PipId]);
        }

        [Fact]
        public void DirectoryEnumerationWithMinimalGraphRespectStaticAndDynamicDirectoryContents()
        {
            Configuration.Sandbox.FileSystemMode = global::BuildXL.Utilities.Configuration.FileSystemMode.AlwaysMinimalGraph;

            // Create pip A whose job is to produce a shared opaque directory.
            var outputDir = CreateOutputDirectoryArtifact(ObjectRoot);
            var existingOutputFile = CreateOutputFileArtifact(outputDir);
            var nonExistingOutputFile = CreateOutputFileArtifact(outputDir);
            var builderA = CreatePipBuilder(new[]
            {
                Operation.WriteFile(existingOutputFile, doNotInfer: true),
                Operation.WriteFile(nonExistingOutputFile, doNotInfer: true),
                Operation.DeleteFile(nonExistingOutputFile)
            });
            builderA.AddOutputDirectory(outputDir, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            // Create a partial sealed directory whose contents are an existing file and a non-existing file.
            var sourceDirPath = CreateUniqueDirectory(root: SourceRoot);
            var existingSource = CreateSourceFile(root: sourceDirPath, prefix: "existingSource");
            var nonExistingSource = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(root: sourceDirPath.ToString(Context.PathTable), prefix: "nonExistingSource"));

            var sourceDir = CreateAndScheduleSealDirectory(sourceDirPath, SealDirectoryKind.Partial, existingSource, nonExistingSource);

            // Create pip B whose job is to enumerate the shared opaque directory produced by A and the partial sealed directory.
            var builderB = CreatePipBuilder(new[]
            {
                Operation.ReadFile(existingSource, doNotInfer: true),
                Operation.ReadFile(nonExistingSource, doNotInfer: true),
                Operation.EnumerateDir(outputDir),
                Operation.EnumerateDir(sourceDir.Directory),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builderB.AddInputDirectory(sourceDir.Directory);
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOutputDirectories().Single().Root);
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // We expect pip B to be a cache hit because the directory enumeration should respect the static and dynamic contents when
            // using the minimal graph. Also to guaranteed that pip B doesn't execute, we check that there's no convergence.
            // When there is convergence, a pip that executes can be marked as cache hit because its outputs come from the cache as
            // determined during the post process.
            var result = RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            result.AssertPipExecutorStatCounted(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged, 0);
        }
    }
}
