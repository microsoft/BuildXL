// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class UnsafeSharedOpaqueDirectoryTests : SchedulerIntegrationTestBase
    {
        public UnsafeSharedOpaqueDirectoryTests(ITestOutputHelper output) : base(output)
        {
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).SkipFlaggingSharedOpaqueOutputs = true;

        }

        [Fact]
        public void SkipFlaggingSharedOpaquesIsHonored()
        {
            // Set up PipA  => sharedOpaqueDirectory => PipB
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            FileArtifact source = CreateSourceFile();

            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: source, new KeyValuePair<FileArtifact, string>(outputInSharedOpaque, null));

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputInSharedOpaque, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            var pipB = SchedulePipBuilder(builderB);

            // B should be able to consume the file in the opaque directory. Second build should have both cached
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // The output shouldn't be flagged as a shared opaque
            XAssert.IsFalse(SharedOpaqueOutputHelper.IsSharedOpaqueOutput(outputInSharedOpaque.Path.ToString(Context.PathTable)));

            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // The output shouldn't be flagged as a shared opaque
            XAssert.IsFalse(SharedOpaqueOutputHelper.IsSharedOpaqueOutput(outputInSharedOpaque.Path.ToString(Context.PathTable)));

            // Make sure we can replay the file in the opaque directory
            File.Delete(ArtifactToString(outputInSharedOpaque));
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            XAssert.IsTrue(File.Exists(ArtifactToString(outputInSharedOpaque)));

            // The output shouldn't be flagged as a shared opaque
            XAssert.IsFalse(SharedOpaqueOutputHelper.IsSharedOpaqueOutput(outputInSharedOpaque.Path.ToString(Context.PathTable)));

            // Modify the input and make sure both are rerun
            File.WriteAllText(ArtifactToString(source), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // The output shouldn't be flagged as a shared opaque
            XAssert.IsFalse(SharedOpaqueOutputHelper.IsSharedOpaqueOutput(outputInSharedOpaque.Path.ToString(Context.PathTable)));
        }
    }
}
