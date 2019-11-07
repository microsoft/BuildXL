// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class ProcessBreakawayTests : SchedulerIntegrationTestBase
    {
        public ProcessBreakawayTests(ITestOutputHelper output) : base(output)
        {
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void BreakawayProcessCompensatesWithAugmentedAccesses()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            FileArtifact source = CreateSourceFile();

            var builder = CreatePipBuilder(new Operation[]
            {
                // We spawn a process, since breakaway happens for child processes only
                Operation.Spawn(Context.PathTable, waitToFinish: true, 
                    // Read and write a file. This accesses will not be observed
                    Operation.ReadFile(source),
                    Operation.WriteFile(outputInSharedOpaque, doNotInfer: true)),
                // Report the augmented accesses (in the root process, which is detoured normally) without actually
                // performing any IO
                Operation.AugmentedRead(source),
                Operation.AugmentedWrite(outputInSharedOpaque, doNotInfer: true),
            });
            builder.AddOutputDirectory(sharedOpaqueDirPath, kind: SealDirectoryKind.SharedOpaque);
            builder.AddInputFile(source);

            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<PathAtom>.FromWithoutCopy(new[] { PathAtom.Create(Context.StringTable, TestProcessToolName) });

            var pip = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();
            XAssert.IsTrue(File.Exists(ArtifactToString(outputInSharedOpaque)));

            // Make sure we can replay the file in the opaque directory. This means the write access reached detours via augmentation.
            File.Delete(ArtifactToString(outputInSharedOpaque));
            RunScheduler().AssertCacheHit(pip.Process.PipId);
            XAssert.IsTrue(File.Exists(ArtifactToString(outputInSharedOpaque)));

            // Modify the input and make sure the pip is re-run. This means the read access reached detours via augmentation.
            File.WriteAllText(ArtifactToString(source), "New content");
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
        }
    }
}
