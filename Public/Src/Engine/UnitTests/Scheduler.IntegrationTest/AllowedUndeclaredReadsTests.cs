// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using LogEventId = BuildXL.Scheduler.Tracing.LogEventId;
using Process = BuildXL.Pips.Operations.Process;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "AllowedUndeclaredReadsTests")]
    public class AllowedUndeclaredReadsTests : SchedulerIntegrationTestBase
    {
        /// <summary>
        /// A directory that is outside any known mounts
        /// </summary>
        protected string OutOfMountRoot;

        /// <summary>
        /// An (arbitrary) shared opaque directory root, used by many tests
        /// </summary>
        protected string SharedOpaqueDirectoryRoot;

        public AllowedUndeclaredReadsTests(ITestOutputHelper output) : base(output)
        {
            // TODO: remove when the default changes
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).IgnoreUndeclaredAccessesUnderSharedOpaques = false;

            SharedOpaqueDirectoryRoot = Path.Combine(ObjectRoot, "sharedOpaqueDirectory");
            OutOfMountRoot = Path.Combine(TemporaryDirectory, "outOfMount");
            Directory.CreateDirectory(OutOfMountRoot);
        }

        [Fact]
        public void UndeclaredReadsAreAllowed()
        {
            ScheduleProcessWithUndeclaredReads(CreateSourceFile());
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void UndeclaredReadsCachingBehavior()
        {
            var source = CreateSourceFile();

            var pip = ScheduleProcessWithUndeclaredReads(source);

            // First run should be a miss, second one a hit.
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            // Modify the source file that was read/probed. Next two runs should be a miss and a hit respectively.
            File.WriteAllText(source.Path.ToString(Context.PathTable), "New content");
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void PresentProbeCachingBehavior()
        {
            var source = CreateSourceFile();

            var pip = ScheduleProcessWithUndeclaredReads(source, probeInsteadOfRead: true);

            // First run should be a miss, second one a hit.
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            // Delete the file that was probed. Next two runs should be a miss and a hit respectively.
            File.Delete(source.Path.ToString(Context.PathTable));

            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void AbsentProbeCachingBehavior()
        {
            var source = CreateSourceFile();

            var pip = ScheduleProcessWithUndeclaredReads(source, probeInsteadOfRead: true);

            File.Delete(source.Path.ToString(Context.PathTable));

            // First run should be a miss, second one a hit.
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);

            // Create the file that was probed (in absence). Next two runs should be a miss and a hit respectively.
            File.WriteAllText(source.Path.ToString(Context.PathTable), "Some content");

            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void AbsentProbeOutOfMountCachingBehavior()
        {
            var source = CreateSourceFile(OutOfMountRoot);

            var pip = ScheduleProcessWithUndeclaredReads(source, probeInsteadOfRead: true);

            File.Delete(source.Path.ToString(Context.PathTable));

            // We allow absent probes on undefined mounts without reporting any errors/warnings
            // First run should be a miss, second one a hit.
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.Process.PipId);
            RunScheduler().AssertCacheHit(pip.Process.PipId);
            AssertErrorCount();
            AssertWarningCount();

            // Create the file that was probed (in absence).
            File.WriteAllText(source.Path.ToString(Context.PathTable), "Some content");

            // This run should be a miss but the scheduler should succeed. Undeclared reads are allowed on undefined mounts
            RunScheduler().AssertSuccess();
            AssertWarningCount();
        }

        [Fact]
        public void MountPolicyBlocksUndeclaredReads()
        {
            var source = CreateSourceFile(NonHashableRoot);
            ScheduleProcessWithUndeclaredReads(source);
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.AbortObservedInputProcessorBecauseFileUntracked);

            ResetPipGraphBuilder();
            source = CreateSourceFile(NonReadableRoot);
            ScheduleProcessWithUndeclaredReads(source);
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.AbortObservedInputProcessorBecauseFileUntracked);
        }

        [Fact]
        public void OutOfMountUndeclaredReadsAreAllowed()
        {
            var source = CreateSourceFile(OutOfMountRoot);
            ScheduleProcessWithUndeclaredReads(source);
            RunScheduler().AssertSuccess();
            AssertWarningCount();
        }

        [Fact]
        public void TwoPipsReadingSameUndeclaredSourceIsOk()
        {
            FileArtifact source = CreateSourceFile();
            ScheduleProcessWithUndeclaredReads(source);
            ScheduleProcessWithUndeclaredReads(source);
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void AStaticAndADynamicReadOnTheSameSourceIsOk()
        {
            FileArtifact source = CreateSourceFile();

            // Schedule a dynamic read
            ScheduleProcessWithUndeclaredReads(source);

            // Schedule a static read on the same source
            var readPipBuilder = CreatePipBuilder(new[]
                                                   {
                                                       Operation.ReadFile(source),
                                                       Operation.WriteFile(CreateOutputFileArtifact()), // dummy output
                                                   });
            SchedulePipBuilder(readPipBuilder);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void PipReadingUnderSharedOpaqueIsOk()
        {
            // Create a pip that writes a file under a shared opaque
            var fileToWrite = CreateOutputFileArtifact(SharedOpaqueDirectoryRoot);
            CreateAndScheduleSharedOpaqueProducer(SharedOpaqueDirectoryRoot, filesToProduce: fileToWrite);

            // Create a source file (different from fileToWrite) under the shared opaque and read it
            FileArtifact source = CreateSourceFile(SharedOpaqueDirectoryRoot);
            ScheduleProcessWithUndeclaredReads(source);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void ReadingAStaticallyDeclaredOutputIsBlocked()
        {
            var source = CreateSourceFile();
            var readPip = ScheduleProcessWithUndeclaredReads(source);

            var writePipBuilder = CreatePipBuilder(new[]
                                            {
                                                Operation.WriteFile(source.CreateNextWrittenVersion()),
                                            });
            // Here we order the writer after the reader just to avoid non-deterministic write locks
            // The violation is checked against static write information, so it is not worth a test for the opposite order
            writePipBuilder.AddInputFile(readPip.ProcessOutputs.GetOutputFiles().First());
            SchedulePipBuilder(writePipBuilder);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void EnumeratingDirectoryUnderSharedOpaqueUsesMinimalGraphFileSystem()
        {
            // Enable minimal lazy output materialization to ensure files are not placed on disk after cache hit
            Configuration.Schedule.RequiredOutputMaterialization = RequiredOutputMaterialization.Minimal;

            // Enumeration path is sub path of shared opaque to ensure enumerating pip doesn't explicitly
            // mention the path in its dependencies
            var enumerationPath = Path.Combine(SharedOpaqueDirectoryRoot, "enum");

            // Create a pip that writes a file under a shared opaque
            var fileToWrite = CreateOutputFileArtifact(enumerationPath);
            var sharedOpaqueProducer = CreateAndScheduleSharedOpaqueProducer(enumerationPath, filesToProduce: fileToWrite);

            // Create a pip which enumerates the shared opaque
            var enumeratingPipBuilder = CreatePipBuilder(new[]
            {
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(Context.PathTable, enumerationPath), doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
            });

            // Mark the pip with allow undeclared source reads to test regression of directory enumeration behavior in this case
            enumeratingPipBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            enumeratingPipBuilder.AddInputDirectory(sharedOpaqueProducer.ProcessOutputs.GetOutputDirectories().Single().Root);

            var enumeratingPip = SchedulePipBuilder(enumeratingPipBuilder);
            RunScheduler().AssertSuccess();

            // After deleting the enumerated directory contents we should still get a hit even though the
            // state of the file system has changed since we are using the minimal graph file system
            FileUtilities.DeleteDirectoryContents(enumerationPath);
            RunScheduler().AssertCacheHit(enumeratingPip.Process.PipId, sharedOpaqueProducer.Process.PipId);

            AssertTrue(Directory.GetFiles(enumerationPath, "*.*", SearchOption.AllDirectories).Length == 0, "Enumerated directory should be empty due to lazy materialization");
        }

        [Fact]
        public void ReadingUnderAnExclusiveOpaqueIsBlocked()
        {
            var exclusiveOpaqueRoot = Path.Combine(ObjectRoot, "exclusiveOpaque");
            var source = CreateSourceFile(exclusiveOpaqueRoot);

            // Read the source file
            ScheduleProcessWithUndeclaredReads(source);

            // Create an empty exclusive opaque
            var builder = CreatePipBuilder(new List<Operation>());
            var exclusiveOpaqueDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(Context.PathTable, exclusiveOpaqueRoot);
            builder.AddOutputDirectory(exclusiveOpaqueDirectoryArtifact, SealDirectoryKind.Opaque);
            SchedulePipBuilder(builder);

            // A violation should be detected regardless of the content of the exclusive opaque
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void PresentProbeOnOutputFileIsBlocked()
        {
            var source = CreateSourceFile();

            // Create a pip that probes the file (and so this will be a present file probe)
            var probePip = ScheduleProcessWithUndeclaredReads(source, probeInsteadOfRead: true);

            // Create a pip that writes to the file, statically declared
            var writePipBuilder = CreatePipBuilder(new[]
                                                   {
                                                       Operation.WriteFile(source.CreateNextWrittenVersion()),
                                                   });
            // Here we order the writer after the reader just to avoid non-deterministic write locks
            writePipBuilder.AddInputFile(probePip.ProcessOutputs.GetOutputFiles().First());
            SchedulePipBuilder(writePipBuilder);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void AbsentProbeOnOutputFileIsBlocked()
        {
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, SharedOpaqueDirectoryRoot);
            FileArtifact source = CreateSourceFile(sharedOpaqueDirPath);

            // Create a pip that creates and deletes the source file under a shared opaque
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            var deletePipBuilder = CreatePipBuilder(new[]
                                                   {
                                                       Operation.WriteFile(source.CreateNextWrittenVersion(), doNotInfer: true),
                                                       Operation.DeleteFile(source.CreateNextWrittenVersion(), doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            deletePipBuilder.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);

            var deletePip = SchedulePipBuilder(deletePipBuilder);

            // Creates a pip that probes the (deleted) source file (undeclared). 
            // Make the reader depend on the dummy output of the writer to ensure read happens after write
            ScheduleProcessWithUndeclaredReads(source, fileDependencies: deletePip.ProcessOutputs.GetOutputFiles(), probeInsteadOfRead: true);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void ReadingAndDynamicallyWritingIsBlocked()
        {
            var source = CreateSourceFile(SharedOpaqueDirectoryRoot);

            // Read the source file
            var readPip = ScheduleProcessWithUndeclaredReads(source);

            // Write to the source file.
            // Make the writer depend on the the reader to ensure write happens after read
            CreateAndScheduleSharedOpaqueProducer(
                SharedOpaqueDirectoryRoot,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: readPip.ProcessOutputs.GetOutputFiles().First(),
                filesToProduceDynamically: source.CreateNextWrittenVersion());

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void DynamicallyWritingAndReadingIsBlocked()
        {
            var source = CreateSourceFile(SharedOpaqueDirectoryRoot);

            // Write to the source file.
            var writePip = CreateAndScheduleSharedOpaqueProducer(
                SharedOpaqueDirectoryRoot,
                fileToProduceStatically: CreateOutputFileArtifact(),
                sourceFileToRead: FileArtifact.Invalid,
                filesToProduceDynamically: source.CreateNextWrittenVersion());

            // Creates a pip that reads from the source file (undeclared)
            // Make the reader depend on the outputs of the writer to ensure read happens after write
            ScheduleProcessWithUndeclaredReads(source, fileDependencies: writePip.ProcessOutputs.GetOutputFiles());

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        [Fact]
        public void ReadViolationsAreDetectedEvenWhenRunningFromCache()
        {
            // Read a file and cache the pip
            FileArtifact source = CreateSourceFile();
            var readPip = ScheduleProcessWithUndeclaredReads(source);
            RunScheduler().AssertSuccess().AssertCacheMiss(readPip.Process.PipId);

            ResetPipGraphBuilder();

            // Schedule the same read. It should run from the cache
            readPip = ScheduleProcessWithUndeclaredReads(source);
            // Also schedule a write into the same source file
            var writePipBuilder = CreatePipBuilder(new[]
                                            {
                                                Operation.WriteFile(source.CreateNextWrittenVersion())
                                            });
            writePipBuilder.AddInputFile(readPip.ProcessOutputs.GetOutputFiles().First());
            SchedulePipBuilder(writePipBuilder);

            // We should get the violation anyway
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteInUndeclaredSourceRead);
        }

        /// <summary>
        /// TODO: blocking writes on existing undeclared inputs is not implemented on Mac yet
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void WritingUndeclaredInputsUnderSharedOpaquesAreBlocked()
        {
            // Create an undeclared source file under the cone of a shared opaque
            var source = CreateSourceFile(SharedOpaqueDirectoryRoot);

            // Run a pip that writes into the source file
            var pipBuilder = CreatePipBuilder(new Operation[] { Operation.WriteFile(source, doNotInfer: true) });
            pipBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, SharedOpaqueDirectoryRoot)), SealDirectoryKind.SharedOpaque);
            pipBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var result = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertFailure();
            IgnoreWarnings();
            AssertErrorEventLogged(LogEventId.DependencyViolationWriteOnExistingFile);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void WritingToExistentFileProducedBySamePipIsAllowed(bool varyPath)
        {
            // Run a pip that writes into a file twice: the second time, the file will exist. However, this should be allowed.
            // This test complements WritingUndeclaredInputsUnderSharedOpaquesAreBlocked, since the check cannot be made purely based on file existence,
            // but should also consider when the first write happened
            var outputFile = CreateOutputFileArtifact(SharedOpaqueDirectoryRoot);
            var pipBuilder = CreatePipBuilder(new Operation[] {
                Operation.WriteFile(outputFile, doNotInfer: true),
                Operation.WriteFile(outputFile, doNotInfer: true, changePathToAllUpperCase: varyPath),
                Operation.WriteFile(outputFile, doNotInfer: true, useLongPathPrefix: varyPath),
            });
            pipBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, SharedOpaqueDirectoryRoot)), SealDirectoryKind.SharedOpaque);
            pipBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var result = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void WritingToExistentFileProducedBySamePipIsAllowedInChildProcess()
        {
            // Run a pip that writes into a file twice, but the second time it happens on a child process.
            var outputFile = CreateOutputFileArtifact(SharedOpaqueDirectoryRoot);
            var pipBuilder = CreatePipBuilder(new Operation[] {
                Operation.WriteFile(outputFile, doNotInfer: true),
                Operation.Spawn(Context.PathTable, waitToFinish: true, Operation.WriteFile(outputFile, doNotInfer: true)),
            });

            pipBuilder.AddOutputDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, SharedOpaqueDirectoryRoot)), SealDirectoryKind.SharedOpaque);
            pipBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var result = SchedulePipBuilder(pipBuilder);

            RunScheduler().AssertSuccess();
        }

        private ProcessWithOutputs ScheduleProcessWithUndeclaredReads(
            FileArtifact source,
            bool probeInsteadOfRead = false,
            IEnumerable<FileArtifact> fileDependencies = null,
            IEnumerable<StaticDirectory> directoryDependencies = null)
        {
            var builder = CreatePipBuilder(
                new[]
                {
                    probeInsteadOfRead ? Operation.Probe(source, doNotInfer: true) : Operation.ReadFile(source, doNotInfer: true),
                    Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                });

            if (fileDependencies != null)
            {
                foreach (var dependency in fileDependencies)
                {
                    builder.AddInputFile(dependency);
                }
            }

            if (directoryDependencies != null)
            {
                foreach (var directoryDependency in directoryDependencies.Select(staticDirectory => staticDirectory.Root))
                {
                    builder.AddInputDirectory(directoryDependency);
                }
            }

            builder.Options = Process.Options.AllowUndeclaredSourceReads;

            return SchedulePipBuilder(builder);
        }
    }
}
