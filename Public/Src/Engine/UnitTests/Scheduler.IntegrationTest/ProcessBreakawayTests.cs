// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
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
                    // Write a file. This access will not be observed
                    Operation.WriteFile(outputInSharedOpaque, doNotInfer: true)
                ),
                // Report the augmented accesses (in the root process, which is detoured normally) without actually
                // performing any IO
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
         }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TrustedAccessesOnProcessBreakawayAreProperlyReported()
        {
            // One (mandatory) output and one optional output
            FileArtifact output = CreateOutputFileArtifact();
            FileArtifact optionalOutput = CreateOutputFileArtifact();

            // A regular source file and a sealed directory
            AbsolutePath sealedRoot = ObjectRootPath.Combine(Context.PathTable, "Sealed");
            FileArtifact source = CreateSourceFile();
            FileArtifact sourceInFullySealed = CreateSourceFile(sealedRoot.ToString(Context.PathTable));
            var sealedDirectory = SealDirectory(sealedRoot, SealDirectoryKind.Full, new[] { sourceInFullySealed });

            var builder = CreatePipBuilder(new Operation[]
            {
                // We spawn a process, since breakaway happens for child processes only
                Operation.Spawn(Context.PathTable, waitToFinish: true, 
                    // Write the mandatory file and read one of the source inputs. These accesses will not be observed.
                    Operation.WriteFile(output, doNotInfer: true),
                    Operation.ReadFile(source, doNotInfer: true))
            });

            builder.AddInputFile(source);
            builder.AddInputDirectory(sealedDirectory);
            builder.AddOutputFile(output.Path);
            builder.AddOutputFile(optionalOutput.Path, FileExistence.Optional);

            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<PathAtom>.FromWithoutCopy(new[] { PathAtom.Create(Context.StringTable, TestProcessToolName) });
            // And to compensate for missing accesses based on declared artifacts
            builder.Options |= Process.Options.TrustStaticallyDeclaredAccesses;

            var pip = SchedulePipBuilder(builder);
            var result = RunScheduler().AssertSuccess();

            // The source file under the full seal directory should be part of the path set for the pip
            XAssert.Contains(result.PathSets[pip.Process.PipId].Value.Paths.Select(pathEntry => pathEntry.Path), sourceInFullySealed);
            // There should be a single produced output (the mandatory one)
            XAssert.IsTrue(EventListener.GetLogMessagesForEventId(EventId.PipOutputProduced).Single().ToUpperInvariant().Contains(output.Path.ToString(Context.PathTable).ToUpperInvariant()));
        }
    }
}
