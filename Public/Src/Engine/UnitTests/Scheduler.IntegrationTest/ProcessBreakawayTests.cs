// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
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

        [Fact]
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
            XAssert.IsTrue(EventListener.GetLogMessagesForEventId((int)LogEventId.PipOutputProduced).Single().ToCanonicalizedPath().Contains(output.Path.ToString(Context.PathTable).ToCanonicalizedPath()));
        }

        [Fact]
        public void BreakawayProcessesAreUntracked()
        {
            var outerSourceFile = CreateSourceFileWithPrefix(SourceRoot, prefix: $"{nameof(BreakawayProcessesAreUntracked)}-outer-src");
            var innerSourceFile = CreateSourceFileWithPrefix(SourceRoot, prefix: $"{nameof(BreakawayProcessesAreUntracked)}-inner-src");

            var outerOutputFile = CreateOutputFileArtifact(ObjectRoot, prefix: $"{nameof(BreakawayProcessesAreUntracked)}-outer-out");
            var innerOutputFile = CreateOutputFileArtifact(ObjectRoot, prefix: $"{nameof(BreakawayProcessesAreUntracked)}-inner-out");

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outerSourceFile),
                Operation.WriteFile(outerOutputFile),

                Operation.Spawn(Context.PathTable, waitToFinish: true, 
                    Operation.WriteFile(innerOutputFile, doNotInfer: true),
                    Operation.ReadFile(innerSourceFile, doNotInfer: true))
            });

            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<PathAtom>.FromWithoutCopy(new[] { PathAtom.Create(Context.StringTable, TestProcessToolName) });

            var pip = SchedulePipBuilder(builder);

            // 1st run: success + cache miss
            var result = RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);

            // there should be a single "produced output" message and it should be for the outer output file
            var producedOutputLogMessage = EventListener.GetLogMessagesForEventId((int)LogEventId.PipOutputProduced).Single().ToCanonicalizedPath();
            XAssert.Contains(producedOutputLogMessage, ToString(outerOutputFile).ToCanonicalizedPath());
            XAssert.ContainsNot(producedOutputLogMessage, ToString(innerOutputFile).ToCanonicalizedPath());

            // 2nd run (no changes): success + up to date
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // 3nd run (change inner source file): success + cache hit (because inner source is untracked)
            File.WriteAllText(ToString(innerSourceFile), contents: Guid.NewGuid().ToString());
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);

            // 4th run (change outer source file): success + cache miss
            File.WriteAllText(ToString(outerSourceFile), contents: Guid.NewGuid().ToString());
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
        }

        [Fact]
        public void BreakawayProcessesCanOutliveThePip()
        {
            var pidFile = CreateOutputFileArtifact(ObjectRoot, prefix: $"{nameof(BreakawayProcessesCanOutliveThePip)}.pid");
            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.SpawnAndWritePidFile(Context.PathTable, waitToFinish: false, pidFile: pidFile, doNotInfer: false,
                    Operation.Block())
            });

            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<PathAtom>.FromWithoutCopy(new[] { PathAtom.Create(Context.StringTable, TestProcessToolName) });

            var pip = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();

            var pidFilePath = ToString(pidFile);
            XAssert.FileExists(pidFilePath);

            var pidFileContent = File.ReadAllText(pidFilePath);
            XAssert.IsTrue(int.TryParse(pidFileContent, out var pid), $"Cannot convert pid file content '{pidFileContent}' to integer");

            var proc = TryGetProcessById(pid);
            XAssert.IsNotNull(proc, $"Could not find the process (PID:{pid}) that was supposed to break away");

            proc.Kill();
        }

        /// <summary>
        /// File based existence denials are windows only for now
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void AllowedTrustedAccessesTrumpFileBasedExistenceDenials()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact output = CreateOutputFileArtifact(sharedOpaqueDir);

            var builder = CreatePipBuilder(new Operation[]
            {
                // We spawn a process, since breakaway happens for child processes only
                Operation.Spawn(Context.PathTable, waitToFinish: true, 
                    // Write the output. This access will not be observed.
                    Operation.WriteFile(output, doNotInfer: true)),
                // Write the output again. This should normally generate a denial based on file existence
                // since the file to be written looks like it was there before the pip started (because we missed 
                // the access)
                Operation.WriteFile(output, doNotInfer: true),
                // Report the augmented write. Even happening later, this should change the previous denial into an allowed access
                Operation.AugmentedWrite(output, doNotInfer: true),
                Operation.Spawn(Context.PathTable, waitToFinish: true, 
                    // Delete the output. This access will not be observed.
                    Operation.DeleteFile(output, doNotInfer: true)),
                // Write to the file again. This makes sure the augmented write is also affecting subsequent decisions
                Operation.WriteFile(output, doNotInfer: true)
            });

            builder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            // File-existence-based denials for now only occur when allowed undeclared source reads is enabled
            builder.Options |= Process.Options.AllowUndeclaredSourceReads;

            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<PathAtom>.FromWithoutCopy(new[] { PathAtom.Create(Context.StringTable, TestProcessToolName) });

            SchedulePipBuilder(builder);
            // No violations should occur
            RunScheduler().AssertSuccess();
        }


        private System.Diagnostics.Process TryGetProcessById(int pid)
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
