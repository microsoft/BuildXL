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
using BuildXL.Utilities;
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
    public class SharedOpaqueDirectoryTests : SchedulerIntegrationTestBase
    {
        public SharedOpaqueDirectoryTests(ITestOutputHelper output) : base(output)
        {
            // TODO: remove when the default changes.
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).IgnoreUndeclaredAccessesUnderSharedOpaques = false;
        }

        /// <summary>
        /// Creates a shared opaque directory producer & consumer and verifies their usage and caching behavior
        /// </summary>
        [Fact]
        public void SharedOpaqueDirectoryConsumptionCachingBehavior()
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

            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);

            // Make sure we can replay the file in the opaque directory
            File.Delete(ArtifactToString(outputInSharedOpaque));
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            XAssert.IsTrue(File.Exists(ArtifactToString(outputInSharedOpaque)));

            // Modify the input and make sure both are rerun
            File.WriteAllText(ArtifactToString(source), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
        }

        [Theory]
        [InlineData(true, Operation.Type.Probe)]
        [InlineData(true, Operation.Type.ReadFile)]
        [InlineData(false, Operation.Type.Probe)]
        [InlineData(false, Operation.Type.ReadFile)]
        public void SharedOpaqueDirectoryBehaviorUnderLazyMaterialization(bool enableLazyOutputMaterialization, Operation.Type readType)
        {
            XAssert.IsTrue(readType == Operation.Type.Probe || readType == Operation.Type.ReadFile);

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            var outputInSharedOpaqueDir = CreateOutputFileArtifact(root: sharedOpaqueDir, prefix: "sod-file");

            FileArtifact source = CreateSourceFile();

            // pipA: CreateDir('sod'), WriteFile('sod/sod-file')
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(sharedOpaqueDirArtifact, doNotInfer: true),
                Operation.WriteFile(outputInSharedOpaqueDir, doNotInfer: true)
            });
            builderA.ToolDescription = StringId.Create(Context.StringTable, "PipA-Producer");
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            // pipB: Read/Probe('sod/sod-file'), WriteFile('pip-b-out')
            var pipBOutFile = CreateOutputFileArtifact(prefix: "pip-b-out");
            var builderB = CreatePipBuilder(new Operation[]
            {
                readType == Operation.Type.Probe
                    ? Operation.Probe(outputInSharedOpaqueDir, doNotInfer: true)
                    : Operation.ReadFile(outputInSharedOpaqueDir, doNotInfer: true),
                Operation.WriteFile(pipBOutFile)
            });
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirArtifact.Path));
            builderB.ToolDescription = StringId.Create(Context.StringTable, "PipB-Consumer");
            var pipB = SchedulePipBuilder(builderB);

            // set filter output='*/pip-b-out'
            Configuration.Schedule.EnableLazyOutputMaterialization = enableLazyOutputMaterialization;
            Configuration.Filter = $"output='*{Path.DirectorySeparatorChar}{pipBOutFile.Path.GetName(Context.PathTable).ToString(Context.StringTable)}'";

            // run1 -> cache misses
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // run2 -> cache hits
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
        }

        [Fact]
        public void SharedOpaqueDirectoryConsumptionCachingBehaviorWithUndeclaredReadMode()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            DirectoryArtifact sharedOpaqueRoot = CreateOutputDirectoryArtifact(sharedOpaqueDir);
            AbsolutePath nestedDirUnderSharedOpaque = sharedOpaqueRoot.Path.Combine(Context.PathTable, "nested");
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(nestedDirUnderSharedOpaque);

            // Create a pip that writes a file in a nested directory under a shared opaque, with allowed undeclared
            // reads enabled
            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.CreateDir(DirectoryArtifact.CreateWithZeroPartialSealId(nestedDirUnderSharedOpaque), doNotInfer: true),
                    Operation.WriteFile(outputInSharedOpaque, doNotInfer: true),
                });
            builder.AddOutputDirectory(sharedOpaqueRoot, SealDirectoryKind.SharedOpaque);
            builder.Options |= Process.Options.AllowUndeclaredSourceReads;

            // First run should be a miss, second one a hit
            var processWithOutputs = SchedulePipBuilder(builder);
            RunScheduler().AssertCacheMiss(processWithOutputs.Process.PipId);
            RunScheduler().AssertCacheHit(processWithOutputs.Process.PipId);

            // Assert the output was produced.
            XAssert.IsTrue(File.Exists(outputInSharedOpaque.Path.ToString(Context.PathTable)));

            // Run the pip again. It should still be a hit. This makes sure that
            // accesses related to outputs don't end up as part of the fingerprint. In this
            // particular case, we should be skipping an access for the directory creation and
            // another one for the file creation
            RunScheduler().AssertCacheHit(processWithOutputs.Process.PipId);
        }

        [Theory]
        [InlineData(PreserveOutputsMode.Enabled)]
        [InlineData(PreserveOutputsMode.Reset)]
        public void WarningIsDisplayedWhenPreserveOutputsIsOnAndThereAreSharedOpaques(PreserveOutputsMode enabledMode)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = enabledMode;
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");

            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: FileArtifact.Invalid, CreateOutputFileArtifact(sharedOpaqueDir));

            RunScheduler().AssertSuccess();
            AssertWarningEventLogged(global::BuildXL.Pips.Tracing.LogEventId.PreserveOutputsDoNotApplyToSharedOpaques);
        }

        /// <summary>
        /// Creates a pip that writes a directory on a shared opaque dir and makes sure it is *not* cached
        /// </summary>
        [Fact]
        public void SharedOpaqueDirectoryWriting()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            // We output one file and one directory
            var outputFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var outputDirectory = CreateOutputFileArtifact(sharedOpaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
                                        {
                                            Operation.CreateDir(outputDirectory, doNotInfer: true),
                                            Operation.WriteFile(outputFile, doNotInfer: true)
                                        });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();

            // Remove the whole directory
            FileUtilities.DeleteDirectoryContents(ArtifactToString(sharedOpaqueDirArtifact));

            // Replay from the cache
            RunScheduler().AssertSuccess();

            // The output file should exist, the directory shouldn't
            XAssert.IsTrue(File.Exists(ArtifactToString(outputFile)));
            XAssert.IsTrue(!File.Exists(ArtifactToString(outputDirectory)));
        }

        [Fact]
        public void SharedOpaqueDirectoryContentIsCorrectlyCachedOnDeletion()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            var sharedOpaqueDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var outputArtifact = CreateOutputFileArtifact(sharedOpaqueDir);
            var outputArtifactLaterDeleted = CreateOutputFileArtifact(sharedOpaqueDir);

            var builder = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.WriteFile(outputArtifact, doNotInfer: true),
                                 Operation.WriteFile(outputArtifactLaterDeleted, doNotInfer: true),
                                 Operation.DeleteFile(outputArtifactLaterDeleted, doNotInfer: true)
                             });
            builder.AddOutputDirectory(sharedOpaqueDirectoryArtifact, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            File.Delete(ArtifactToString(outputArtifact));
            File.Delete(ArtifactToString(outputArtifactLaterDeleted));

            // Replay from cache. We should only get the file that existed when the pip finished
            RunScheduler().AssertSuccess();
            XAssert.IsTrue(File.Exists(ArtifactToString(outputArtifact)));
            XAssert.IsFalse(File.Exists(ArtifactToString(outputArtifactLaterDeleted)));
        }

        [Fact]
        public void MoveDirectoryUnderASharedOpaqueBehavior()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            AbsolutePath subDirSrc = sharedOpaqueDirPath.Combine(Context.PathTable, "subdir-src");
            AbsolutePath subDirTarget = sharedOpaqueDirPath.Combine(Context.PathTable, "subdir-target");

            var sharedOpaqueDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            var subDirSourceDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(subDirSrc);
            var subDirTargetDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(subDirTarget);

            var outputArtifact = CreateOutputFileArtifact(subDirSourceDirectoryArtifact);

            // Create a directory, a file under it and then move the directory to a sibling place
            var builderA = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.CreateDir(subDirSourceDirectoryArtifact, doNotInfer: true),
                                 Operation.WriteFile(outputArtifact, doNotInfer: true),
                                 Operation.MoveDir(subDirSourceDirectoryArtifact, subDirTargetDirectoryArtifact),
                             });
            builderA.AddOutputDirectory(sharedOpaqueDirectoryArtifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            // Create a file with the same name as the directory originally created by pip A
            var fileExDirectory = new FileArtifact(subDirSrc);
            var builderB = CreatePipBuilder(new List<Operation>
                             {
                                 Operation.WriteFile(fileExDirectory, doNotInfer: true),
                             });
            builderB.AddOutputDirectory(sharedOpaqueDirectoryArtifact, SealDirectoryKind.SharedOpaque);
            // Avoid races
            builderB.AddOrderDependency(pipA.Process.PipId);
            SchedulePipBuilder(builderB);

            // Everything should be fine. The directory move operation coming from pip A should be discarded for the directory itself,
            // so pip B should be able to create a file with the same path without triggering a double write.
            RunScheduler().AssertSuccess();
        }

        /// <summary>
        /// Consumers can only read files in an opaque directoy that were produced by its declared producers
        /// </summary>
        [Fact]
        public void ConsumersCanOnlyReadFromProducersInSharedOpaque()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA produces outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            // Dummy output, just to force an order between pips and avoid races
            FileArtifact dummyOutputA = CreateOutputFileArtifact();
            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: dummyOutputA, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactA, null));

            // PipB produces outputArtifactB in the same shared opaque directory
            FileArtifact outputArtifactB = CreateOutputFileArtifact(sharedOpaqueDir);
            CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactB, null));

            // PipC reads outputArtifactA, declaring a dependency on PipA's shared opaque
            var dummyOutputC = CreateOutputFileArtifact();
            var builderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputArtifactA, doNotInfer: true),
                Operation.WriteFile(dummyOutputC)
            });
            builderC.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            SchedulePipBuilder(builderC);

            RunScheduler().AssertSuccess();

            ResetPipGraphBuilder();

            // Re-create pipA and pipB as before
            pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: dummyOutputA, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactA, null));
            var pipB = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactB, null));

            // PipD reads outputArtifactA declaring a dependency on PipB's shared opaque - this should be disallowed
            var builderD = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(outputArtifactA, doNotInfer: true),
                                                       Operation.ReadFile(pipA.ProcessOutputs.GetOutputFile(dummyOutputA)), // just force a dependency, and infer it, to avoid races
                                                       Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            builderD.AddInputDirectory(pipB.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            SchedulePipBuilder(builderD);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        /// <summary>
        /// Make sure that a DisallowedFileAccess on a shared opaque produced file yields the appropriate related pip information
        /// in the error log
        /// </summary>
        [Fact]
        public void UndeclaredConsumersAreCorrectlyReferencedInViolationError()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA produces outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            // Dummy output, just to force an order between pips and avoid races
            FileArtifact dummyOutputA = CreateOutputFileArtifact();
            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: dummyOutputA, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactA, null));

            // PipB consumed outputArtifactA without declaring it.
            FileArtifact outputArtifactB = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(dummyOutputA),
                Operation.ReadFile(outputArtifactA, doNotInfer: true),
                Operation.WriteFile(outputArtifactB),
            });

            SchedulePipBuilder(builderB);

            // Build should fail with a Disallowed File Access
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FileMonitoringError, 1);

            // And we should have a related pip
            AssertLogContains(caseSensitive: false, "Violations related to pip");
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
        }

        /// <summary>
        /// Consumers which produce to a shared opaque can only read files in that opaque directory which were produced by its declared producers
        /// </summary>
        [Fact]
        public void ConsumerProducersCanOnlyReadFromProducersInSharedOpaqueWithSameRoot()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA produces outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            // Dummy output, just to force an order between pips and avoid races
            FileArtifact dummyOutputA = CreateOutputFileArtifact();
            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: dummyOutputA, CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactA, null));

            // PipB produces outputArtifactB in the same shared opaque directory root, but reads from A without
            // declaring any dependency on it
            FileArtifact outputArtifactB = CreateOutputFileArtifact(sharedOpaqueDir);
            var pipB = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: dummyOutputA,
                Operation.WriteFile(outputArtifactB),
                Operation.ReadFile(outputArtifactA, doNotInfer: true));

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void DirectoryDoubleWriteIsAllowedUnderASharedOpaque()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA writes outputArtifactA in a shared opaque directory
            FileArtifact directory = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.CreateDir(directory, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderA);

            // PipB writes the same directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.CreateDir(directory, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void DirectoryDoubleDeletionIsAllowedUnderASharedOpaque()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA creates and removes a directory
            FileArtifact directory = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.CreateDir(directory, doNotInfer: true),
                                                       Operation.DeleteDir(directory, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            // PipB creates and remove the same directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.CreateDir(directory, doNotInfer: true),
                                                       Operation.DeleteDir(directory, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Order pips just to avoid races
            builderB.AddOrderDependency(pipA.Process.PipId);
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void TwoPipsProducingTheSameFileDynamicallyAndStaticallyUnderASharedOpaqueDirectoryIsBlocked()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA writes outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);

            // PipB produces the same artifact outputArtifactA but statically
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA),
                                                   });

            // Let's make B depend on A so we avoid potential file locks on the double write
            builderB.AddInputDirectory(resA.Process.DirectoryOutputs.Single());

            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a double write
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            // We might get a put content failed event if the file was being written by one pip while being cached by the second
            AllowErrorEventMaybeLogged(LogEventId.StorageCachePutContentFailed);
            AllowErrorEventMaybeLogged(LogEventId.ProcessingPipOutputFileFailed);
        }

        [Fact]
        public void TwoPipsProducingTheSameFileDynamicallyUnderASharedOpaqueDirectoryIsBlocked()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA writes outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);

            // PipB writes outputArtifactA in a shared opaque directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Let's make B depend on A so we avoid potential file locks on the double write
            builderB.AddInputFile(resA.ProcessOutputs.GetOutputFiles().Single());
            SchedulePipBuilder(builderB);


            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a double write
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            // We might get a put content failed event if the file was being written by one pip while being cached by the second
            AllowErrorEventMaybeLogged(LogEventId.StorageCachePutContentFailed);
            AllowErrorEventMaybeLogged(LogEventId.ProcessingPipOutputFileFailed);
        }

        [Fact]
        public void SharedOpaqueFileWriteInsideTempDirectory()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            var tempDirUnderSharedPath = CreateUniqueDirectory(sharedOpaqueDirPath);

            // PipA writes outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(tempDirUnderSharedPath);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);


            var sharedOpaqueDirB = Path.Combine(ObjectRoot, "sharedopaquedirB");
            AbsolutePath sharedOpaqueDirPathB = AbsolutePath.Create(Context.PathTable, sharedOpaqueDirB);

            // PipB writes outputArtifactA in a shared opaque directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(outputArtifactA, doNotInfer: true),
                                                   });

            builderB.TempDirectory = tempDirUnderSharedPath;

            builderB.AddOutputDirectory(sharedOpaqueDirPathB, SealDirectoryKind.SharedOpaque);

            // Let's make B depend on A so the write happens before setting the temp directory
            builderB.AddInputDirectory(resA.Process.DirectoryOutputs.Single());
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler(tempCleaner: new global::BuildXL.Scheduler.TempCleaner(LoggingContext, ToString(tempDirUnderSharedPath))).AssertFailure();

            AssertVerboseEventLogged(LogEventId.DependencyViolationSharedOpaqueWriteInTempDirectory);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            // We might get a put content failed event if the file was being written by one pip while being cached by the second
            AllowErrorEventMaybeLogged(LogEventId.ProcessingPipOutputFileFailed);
        }

        [Fact]
        public void TwoPipsProducingTheSameFileDynamicallyUnderASharedOpaqueDirectoryWhenViolationsAreWarningsDoNotCrashBuildXL()
        {
            ((UnsafeSandboxConfiguration)Configuration.Sandbox.UnsafeSandboxConfiguration).UnexpectedFileAccessesAreErrors = false;

            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // PipA writes outputArtifactA in a shared opaque directory
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);

            // PipB writes outputArtifactA in a shared opaque directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Let's make B depend on A so we avoid potential file locks on the double write
            builderB.AddInputFile(resA.ProcessOutputs.GetOutputFiles().Single());
            SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();

            // We are expecting a double write as a verbose message.
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertWarningEventLogged(LogEventId.FileMonitoringWarning);

            // We inform about a mismatch in the file content (due to the ignored double write)
            AssertVerboseEventLogged(LogEventId.FileArtifactContentMismatch);

            // Verify the process not stored to cache event is raised
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
        }

        [Fact]
        public void UntrackedPathsUnderSharedOpaqueAreHonored()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            var nested = Path.Combine(sharedOpaqueDir, "nested");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            AbsolutePath nestedPath = AbsolutePath.Create(Context.PathTable, nested);

            // PipA writes outputArtifactA in a shared opaque directory
            // and an untracked file underneath
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);
            FileArtifact untrackedArtifact = CreateOutputFileArtifact(nested);

            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.CreateDir(DirectoryArtifact.CreateWithZeroPartialSealId(nestedPath), doNotInfer: true),
                                                       Operation.WriteFile(outputArtifactA, doNotInfer: true),
                                                       Operation.WriteFile(untrackedArtifact, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderA.AddUntrackedDirectoryScope(untrackedArtifact.Path.GetParent(Context.PathTable));
            SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void RewritingSourceFilesUnderSharedOpaqueIsNotAllowed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // Let's write a file that will serve as a source file for the pip below
            var sourceFile = CreateSourceFile(sharedOpaqueDir);

            // PipA reads from the source file. That means the source file becomes known to the build
            var dummyOutput = CreateOutputFileArtifact();
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(sourceFile),
                                                       Operation.WriteFile(dummyOutput),
                                                   });
            SchedulePipBuilder(builderA);

            // PipB writes to the source file as part of a shared opaque
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFile(sourceFile, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // We don't actually need this dependency, but this forces pipB to run after pipA, avoiding write locks on the source file
            builderB.AddInputFile(dummyOutput);
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a file monitor violation
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void RewritingSourceFilesUnderSharedOpaqueWithSamePipIsNotAllowed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // Let's write a file that will serve as a source file for the pip below
            var sourceFile = CreateSourceFile(sharedOpaqueDir);

            // PipA writes to the source file under a shared opaque.
            var dummyOutput = CreateOutputFileArtifact();
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFile(sourceFile, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // And it is declared as a source file
            builderA.AddInputFile(sourceFile);

            SchedulePipBuilder(builderA);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a file monitor violation
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            // It gets reported as a double write (between the writing pip and the hash source file pip)
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
        }

        [Theory]
        [InlineData(DoubleWritePolicy.AllowSameContentDoubleWrites)]
        [InlineData(DoubleWritePolicy.DoubleWritesAreErrors)]
        public void RewritingDirectoryDependencyUnderSharedOpaqueIsNotAllowed(DoubleWritePolicy doubleWritePolicy)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            const string ContentToWrite = "content";

            // The first pip writes a file under a shared opaque
            var dependencyInOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFile(dependencyInOpaque, content: ContentToWrite, doNotInfer: true),
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderA.DoubleWritePolicy = doubleWritePolicy;
            
            var resA = SchedulePipBuilder(builderA);

            // The second pip depends on the shared opaque of the first pip, and tries to write to that same file (with same content)
            var operations = new List<Operation>();
            if (doubleWritePolicy == DoubleWritePolicy.AllowSameContentDoubleWrites)
            {
                // Delete the file first, since write file operation implies append
                // and we want to be sure we write the same content than the first time
                operations.Add(Operation.DeleteFile(dependencyInOpaque, doNotInfer: true));
            }
            operations.Add(Operation.WriteFile(dependencyInOpaque, content: ContentToWrite, doNotInfer: true));

            var builderB = CreatePipBuilder(operations);
            builderB.AddInputDirectory(resA.ProcessOutputs.GetOutputDirectories().Single().Root);
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.DoubleWritePolicy = doubleWritePolicy;

            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            var result = RunScheduler();

            if (doubleWritePolicy == DoubleWritePolicy.DoubleWritesAreErrors)
            {
                result.AssertFailure();
                // We are expecting a file monitor violation
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
                // It gets reported as a double write
                AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            }
            else
            {
                // Given the same content was written, if the double write policy is to allow it, we should be good
                result.AssertSuccess();
            }
        }

        [Fact]
        public void WritingInASourceSealNestedInAShardOpaqueIsNotAllowed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            var nestedSourceSeal = Path.Combine(sharedOpaqueDir, "nestedSourceSeal");
            AbsolutePath nestedSourceSealPath = AbsolutePath.Create(Context.PathTable, nestedSourceSeal);
            FileUtilities.CreateDirectory(nestedSourceSeal);
            CreateSourceFile(root: nestedSourceSealPath, prefix: "source-seal-file"); // add at least one source file to prevent the scrubber from deleting it
            PipConstructionHelper.SealDirectorySource(nestedSourceSealPath, SealDirectoryKind.SourceTopDirectoryOnly);

            FileArtifact outputUnderSharedOpaqueAndSourceSealed = CreateOutputFileArtifact(nestedSourceSeal);

            // PipA writes under the shared opaque, but also under the source sealed underneath. This shouldn't be allowed
            var builderA = CreatePipBuilder(new []
                                            {
                                                Operation.WriteFile(outputUnderSharedOpaqueAndSourceSealed, doNotInfer: true),
                                            });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderA);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a file monitor violation
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void WritingInTheConeOfAPartiallySealedDirectoryIsAllowed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            var partialDir = Path.Combine(sharedOpaqueDir, "partialDir");
            AbsolutePath partialDirPath = AbsolutePath.Create(Context.PathTable, partialDir);
            FileUtilities.CreateDirectory(partialDir);
            FileArtifact outputUnderSharedOpaqueAndPartialSealed = CreateOutputFileArtifact(partialDir);

            // PipA writes under the shared opaque, but also under the partial sealed underneath, as an explicitly defined output
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFile(outputUnderSharedOpaqueAndPartialSealed),
                                                   });
            SchedulePipBuilder(builderA);

            // We create a partial seal directory containing the written file
            var partialDirectory = PipConstructionHelper.SealDirectoryPartial(
                partialDirPath,
                new[] {outputUnderSharedOpaqueAndPartialSealed});

            // PipB writes under the partial sealed as well, and takes a dependency on the partial seal (this last read is not really needed, but
            // it increases the chances of something going wrong)
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(outputUnderSharedOpaqueAndPartialSealed, doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact(partialDir), doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddInputDirectory(partialDirectory);
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertSuccess();
        }

        /// <summary>
        /// If a pip consumes a SOD that is a subDir of a produced SOD, that pip is allowed to write in the cone 
        /// of the consumed SOD because the produced SOD subsumes it.
        /// </summary>
        [Fact]
        public void WritingInTheConeOfAnInputSharedOpaqueSubdirectoryIsAllowed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sod-subDirInput-test");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            var sharedOpaqueSubDir = Path.Combine(sharedOpaqueDir, "subDir");
            var sharedOpaqueSubDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir);

            // PipA produces 'sod-subDirInput-test\subDir' shared opaque
            var outputArtifactA = CreateOutputFileArtifact(sharedOpaqueSubDir);
            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueSubDir, fileToProduceStatically: CreateOutputFileArtifact(), CreateSourceFile(), new KeyValuePair<FileArtifact, string>(outputArtifactA, null));

            //PipB consumes 'sod-subDirInput-test\subDir'
            //     produces 'sod-subDirInput-test'
            //       writes 'sod-subDirInput-test\subDir\outputArtifactB'
            var outputArtifactB = CreateOutputFileArtifact(sharedOpaqueSubDir);
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputArtifactB, doNotInfer: true),
            });
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueSubDirPath));
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void TwoPipsProducingTheSameFileDynamicallyUnderASharedOpaqueDirectoryIsBlockedEvenWhenRunFromCache()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputArtifactB = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFile(outputArtifactB, "CONTENT")
                                                   });
            var resA = SchedulePipBuilder(builderA);

            // PipB writes outputArtifactA into a shared opaque directory sharedopaquedir.
            FileArtifact outputArtifactA = CreateOutputFileArtifact(sharedOpaqueDir);

            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(outputArtifactB),
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resB = SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertSuccess().AssertCacheMiss(resA.Process.PipId, resB.Process.PipId);

            ResetPipGraphBuilder();

            // PipA now writes outputArtifactA into sharedopaquedir.
            builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                       Operation.WriteFile(outputArtifactB, "CONTENT")
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderA);

            // Pip B should run from cache, but would fail because of double writes.
            builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(outputArtifactB),
                                                       Operation.WriteFileWithRetries(outputArtifactA, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            RunScheduler().AssertFailure();

            // We are expecting a double write
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void AbsentFileProbeFollowedByDynamicWriteIsBlockedWhenPipsAreIndependent(bool forceDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.Probe(absentFile, doNotInfer: true),
                                                       Operation.WriteFile(dummyOut) // dummy output
                                                   });
            var pipA = SchedulePipBuilder(builderA);

            // PipB writes absentFile into a shared opaque directory sharedopaquedir.
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       forceDependency
                                                           ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                           : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that writing to 'absentFile' happens after pipA
                                                       Operation.WriteFile(absentFile, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddUntrackedFile(pipAoutput);
            var pipB = SchedulePipBuilder(builderB);

            if (forceDependency)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                RunScheduler().AssertFailure();

                // We are expecting a write after an absent path probe
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                AssertVerboseEventLogged(LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void DynamicWriteFollowedByAbsentPathFileProbeIsBlockedWhenPipsAreIndependent(bool forceDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");

            // Write and delete 'absentFile' under a shared opaque.
            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.WriteFile(absentFile, doNotInfer: true),
                                                Operation.DeleteFile(absentFile, doNotInfer: true),
                                                Operation.WriteFile(dummyOut) // dummy output
                                            },
                                            description: "PipA");
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);

            // Probe the absent file. Even though it was deleted by the previous pip, we should get a absent file probe violation
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                forceDependency
                                                    ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                    : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that probing 'absentFile' happens after pipA
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                            },
                                            description: "PipB");
            builderB.AddUntrackedFile(pipAoutput);
            var resB = SchedulePipBuilder(builderB);

            FileUtilities.DeleteFile(pipAoutput.Path.ToString(Context.PathTable));

            if (forceDependency)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                RunScheduler().AssertFailure();

                // We are expecting a write on an absent path probe
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
        }

        [Fact]
        public void DynamicTemporaryFileWriteFollowedByAbsentPathFileProbeIsAllowedForDependencies()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(sharedOpaqueDir);

            // Write and delete 'absentFile' under a shared opaque.
            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                        Operation.WriteFile(absentFile, doNotInfer: true),
                                                        Operation.DeleteFile(absentFile, doNotInfer: true),
                                                        Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);

            // Probe the absent file. Even though there was a write access to that path, we do not block probes to that path
            // because we take a dependency on the directory that produced that path (the probe is guaranteed to always be absent).
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                        Operation.Probe(absentFile, doNotInfer: true),
                                                        Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            builderB.AddInputDirectory(resA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            var resB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void AbsentFileProbeFollowedByDynamicWriteIsBlockedOnProbeCacheReplayWhenPipsAreIndependent(bool forceDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFileUnderSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");

            // PipA probes absentFileUnderSharedOpaque under an opaque directory.
            var builderA = CreatePipBuilder(new Operation[]
                {
                    Operation.Probe(absentFileUnderSharedOpaque, doNotInfer: true),
                    Operation.WriteFile(dummyOut) // dummy output
                });
            var pipA = SchedulePipBuilder(builderA);

            // PipB writes absentFileUnderSharedOpaque into a shared opaque directory sharedopaquedir.
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);
            var builderB = CreatePipBuilder(new Operation[]
                {
                    forceDependency
                        ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                        : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that writing to 'absentFile' happens after pipA
                    Operation.WriteFile(absentFileUnderSharedOpaque, doNotInfer: true),
                });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddUntrackedFile(pipAoutput);
            var pipB = SchedulePipBuilder(builderB);

            FileUtilities.DeleteFile(pipAoutput.Path.ToString(Context.PathTable));

            // run once to cache pipA
            var firstResult = RunScheduler();

            FileUtilities.DeleteDirectoryContents(sharedOpaqueDir, deleteRootDirectory: true);
            FileUtilities.DeleteFile(pipAoutput.Path.ToString(Context.PathTable));

            // run second time -- PipA should come from cache, PipB should run but still hit the same violation
            var secondResult = RunScheduler();

            if (forceDependency)
            {
                firstResult.AssertSuccess();
                secondResult.AssertSuccess();
                secondResult.AssertCacheHitWithoutAssertingSuccess(pipA.Process.PipId);
            }
            else
            {
                firstResult.AssertFailure();
                secondResult.AssertFailure();

                // We are expecting a write after an absent path probe (one message per run)
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe, 2);
                AssertErrorEventLogged(LogEventId.FileMonitoringError, 2);
            }
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void DynamicWriteFollowedByAbsentPathFileProbeIsBlockedOnWriterCacheReplayWhenPipsAreIndependent(bool forceDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFileUnderSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummy-out");
            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.WriteFile(absentFileUnderSharedOpaque, doNotInfer: true),
                                                Operation.DeleteFile(absentFileUnderSharedOpaque, doNotInfer: true),
                                                Operation.WriteFile(dummyOut) // dummy output
                                            },
                                            description: "PipA");
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);

            // Probe the absent file. Even though it was deleted by the previous pip, we should get a absent file probe violation
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                forceDependency
                                                    ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                    : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that probing 'absentFile' happens after pipA
                                                Operation.Probe(absentFileUnderSharedOpaque, doNotInfer: true),
                                                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                            },
                                            description: "PipB");
            builderB.AddUntrackedFile(pipAoutput);
            var pipB = SchedulePipBuilder(builderB);

            // run two times
            var firstResult = RunScheduler();
            var secondResult = RunScheduler();

            if (forceDependency)
            {
                firstResult.AssertSuccess();
                secondResult.AssertSuccess();
            }
            else
            {
                // We are expecting a write on an absent path probe (one message per run)
                firstResult.AssertFailure();
                secondResult.AssertFailure();
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe, 2);
                AssertErrorEventLogged(LogEventId.FileMonitoringError, 2);
                IgnoreWarnings();
            }
        }

        [Feature(Features.DirectoryEnumeration)]
        [Feature(Features.GraphFileSystem)]
        [Fact]
        public void EnumerateSharedOpaqueDirectory()
        {
            Configuration.Sandbox.FileSystemMode = FileSystemMode.RealAndPipGraph;

            // producerA and producerB contribute to the same shared opaque root. Consumer enumerates the root of the shared opaque.
            FileArtifact inputA = CreateSourceFile();
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var firstFileAndOutput = new KeyValuePair<FileArtifact, string>(CreateOutputFileArtifact(sharedOpaqueDir), "1");
            var secondFileAndOutput = new KeyValuePair<FileArtifact, string>(CreateOutputFileArtifact(sharedOpaqueDir), "2");
            FileArtifact consumerOutput = CreateOutputFileArtifact();

            var producerA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: inputA, firstFileAndOutput);
            var producerB = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: inputA, secondFileAndOutput);
            var consumer = CreateAndScheduleConsumingPip(consumerOutput, producerA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), producerB.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));

            // Ensure the correct baseline behavior
            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(producerA.Process.PipId, producerB.Process.PipId, consumer.Process.PipId);

            // Modify A's and B's input. Its output is the same so B should still be a hit
            File.WriteAllText(ArtifactToString(inputA), "asdf");
            ResetPipGraphBuilder();

            var run2Result = RunScheduler();
            run2Result.AssertCacheHit(consumer.Process.PipId);
            run2Result.AssertCacheMiss(producerA.Process.PipId, producerB.Process.PipId);

            // Now, modify A such that it produces an additional file in the opaque directory
            ResetPipGraphBuilder();
            producerA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: inputA, firstFileAndOutput,
                new KeyValuePair<FileArtifact, string>(CreateOutputFileArtifact(sharedOpaqueDir), "2"));
            producerB = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, fileToProduceStatically: FileArtifact.Invalid, sourceFileToRead: inputA, secondFileAndOutput);
            consumer = CreateAndScheduleConsumingPip(consumerOutput, producerA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), producerB.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));

            // A is a cache miss because its command line changes. B should be a hit because nothing changed for it. The consumer should be a miss because the result of the enumeration changed.
            // This is the case even if RealAndPipGraph was selected
            var run3Result = RunScheduler();
            run3Result.AssertCacheMiss(producerA.Process.PipId);
            run3Result.AssertCacheHit(producerB.Process.PipId);
            run3Result.AssertCacheMiss(consumer.Process.PipId);
        }

        [Fact]
        public void FirstDoubleWriteWinsMakesPipUnCacheable()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath =
                AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            IEnumerable<Operation> writeOperations =
                new Operation[]
                {
                    Operation.WriteFileWithRetries(
                        CreateOutputFileArtifact(sharedOpaqueDir),
                        doNotInfer: true)
                };

            FirstDoubleWriteWinsMakesPipUnCacheableRunner(
                writeOperations,
                sharedOpaqueDirPath,
                originalProducerPipCacheHitExpected: false,
                doubleWriteProducerPipCacheHitExpected: false);

            ResetPipGraphBuilder();

            FirstDoubleWriteWinsMakesPipUnCacheableRunner(
                writeOperations,
                sharedOpaqueDirPath,
                originalProducerPipCacheHitExpected: true,
                doubleWriteProducerPipCacheHitExpected: false);
        }

        [Fact]
        public void CacheConvergenceCanTriggerAdditionalOutputContentAwareViolations()
        {
            // Make cache look-ups always result in cache misses. This allows us to store outputs in the cache but get cache misses for the same pip next time.
            // This enables us to recreate cache convergence
            Configuration.Cache.ArtificialCacheMissOptions = new ArtificialCacheMissConfig()
            {
                Rate = ushort.MaxValue,
                Seed = 0,
                IsInverted = false
            };

            // We need to simulate cache convergence, and for that we will make a pip produce outputs based on undeclared inputs, so
            // we need to turn off DFAs as errors
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;

            // Shared opaque to produce outputs
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // We will generate a double write over these files
            var output = CreateOutputFileArtifact(sharedOpaqueDirPath);
            var outputAsSource = FileArtifact.CreateSourceFile(output.Path);

            Directory.CreateDirectory(sharedOpaqueDir);
            File.WriteAllText(outputAsSource.Path.ToString(Context.PathTable), "content");

            // This pip reads output and rewrites it.
            var builderA = CreatePipBuilder(new Operation[] {
                Operation.ReadAndWriteFile(FileArtifact.CreateSourceFile(output.Path), output, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))});
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderA.DoubleWritePolicy = DoubleWritePolicy.AllowSameContentDoubleWrites;
            var processA = SchedulePipBuilder(builderA);

            // And the same for this one. The previous one has a static dummy output so just they don't collide in weak fingerprint
            var builderB = CreatePipBuilder(new Operation[] { Operation.ReadAndWriteFile(outputAsSource, output, doNotInfer: true) });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.DoubleWritePolicy = DoubleWritePolicy.AllowSameContentDoubleWrites;
            var processB = SchedulePipBuilder(builderB);

            // We only want to run processB, but we need to add both pips in the graph so we can get cache hits the second time
            RunScheduler(filter: new RootFilter(new PipIdFilter(processB.Process.SemiStableHash))).AssertSuccess();

            ResetPipGraphBuilder();

            // Change the content of the source file. This will make both pips change the output they generate (without changing their fingerprints)
            File.WriteAllText(outputAsSource.Path.ToString(Context.PathTable), "modified content");

            // This will write 'modified content'
            processA = SchedulePipBuilder(builderA);
            // This will writes 'modified content' as well, and therefore this will be a same-content double write
            // But then the pip will converge into the cached outputs on the first run (i.e. 'content')
            processB = SchedulePipBuilder(builderB);

            // Run both pips now making sure they run in order. Check convergence actually happened for process B
            var result = RunScheduler(constraintExecutionOrder: new (Pip, Pip)[] { (processA.Process, processB.Process) });
            result.AssertPipExecutorStatCounted(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged, 1);

            // The pre-convergence analysis will find an allowed same-content double write
            AssertVerboseEventLogged(LogEventId.AllowedSameContentDoubleWrite);

            // The post-convergence analysis should find the double write
            // Since we are running with DFAs as warnings, it won't be logger as an error
            AssertWarningEventLogged(LogEventId.FileMonitoringWarning);
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
        }

        private void FirstDoubleWriteWinsMakesPipUnCacheableRunner(
            IEnumerable<Operation> writeOperations,
            AbsolutePath sharedOpaqueDirPath,
            bool originalProducerPipCacheHitExpected,
            bool doubleWriteProducerPipCacheHitExpected)
        {
            // originalProducerPip writes an artifact in a shared opaque directory
            ProcessBuilder originalProducerPipBuilder = CreatePipBuilder(
                writeOperations.Append(Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "dep")))));
            originalProducerPipBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            originalProducerPipBuilder.DoubleWritePolicy |= DoubleWritePolicy.UnsafeFirstDoubleWriteWins;
            ProcessWithOutputs originalProducerPipResult = SchedulePipBuilder(originalProducerPipBuilder);

            // doubleWriteProducerPip writes the same artifact in a shared opaque directory
            ProcessBuilder doubleWriteProducerPipBuilder = CreatePipBuilder(writeOperations);
            doubleWriteProducerPipBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            // Let's make doubleWriteProducerPip depend on originalProducerPip so we avoid potential file locks on the double write
            doubleWriteProducerPipBuilder.AddInputFile(originalProducerPipResult.ProcessOutputs.GetOutputFiles().Single());

            // Set UnsafeFirstDoubleWriteWins
            doubleWriteProducerPipBuilder.DoubleWritePolicy |= DoubleWritePolicy.UnsafeFirstDoubleWriteWins;
            ProcessWithOutputs doubleWriteProducerPipResult = SchedulePipBuilder(doubleWriteProducerPipBuilder);

            var result = RunScheduler().AssertSuccess();

            if (originalProducerPipCacheHitExpected)
            {
                result.AssertCacheHit(originalProducerPipResult.Process.PipId);
            }
            else
            {
                result.AssertCacheMiss(originalProducerPipResult.Process.PipId);
            }

            if (doubleWriteProducerPipCacheHitExpected)
            {
                result.AssertCacheHit(doubleWriteProducerPipResult.Process.PipId);
            }
            else
            {
                result.AssertCacheMiss(doubleWriteProducerPipResult.Process.PipId);
            }

            // We are expecting a double write as a verbose message.
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertWarningEventLogged(LogEventId.FileMonitoringWarning);

            // We inform about a mismatch in the file content (due to the ignored double write)
            AssertVerboseEventLogged(LogEventId.FileArtifactContentMismatch);

            // Verify the process not stored to cache event is raised
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        //[InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
                              // NOTE: this is difficult to test reliably because the test depends on pips running in a particular order
        public void AbsentPathProbeInUndeclaredOpaquesUnsafeModeCachedPip(bool forceDependency)
        {
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");

            var builderA = CreatePipBuilder(new Operation[]
                                            {
                                                Operation.Probe(absentFile, doNotInfer: true),
                                                Operation.WriteFile(dummyOut)
                                            });
            builderA.AbsentPathProbeUnderOpaquesMode = Process.AbsentPathProbeInUndeclaredOpaquesMode.Unsafe;
            var pipA = SchedulePipBuilder(builderA);
            var pipAoutput = pipA.ProcessOutputs.GetOutputFile(dummyOut);
            var builderB = CreatePipBuilder(new Operation[]
                                            {
                                                forceDependency
                                                    ? Operation.ReadFile(pipAoutput)                                // force a BuildXL dependency
                                                    : Operation.WaitUntilFileExists(pipAoutput, doNotInfer: true),  // force that writing to 'absentFile' happens after pipA
                                                Operation.WriteFile(absentFile, doNotInfer: true),
                                                Operation.DeleteFile(absentFile, doNotInfer: true),
                                                Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                            });
            builderB.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddUntrackedFile(pipAoutput);
            SchedulePipBuilder(builderB);

            if (forceDependency)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                // first run -- cache pipA, pipB should fail
                RunScheduler().AssertFailure();
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                AssertVerboseEventLogged(LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);

                // second run -- in Unsafe mode, the outcome of the build (pass/fail) currently depends on
                // the fact whether a pip was incrementally skipped or not:
                var result = RunScheduler();
                if (Configuration.Schedule.IncrementalScheduling)
                {
                    result.AssertSuccess();
                    result.AssertCacheHit(pipA.Process.PipId);
                }
                else
                {
                    result.AssertFailure();
                    AssertErrorEventLogged(LogEventId.FileMonitoringError);
                    AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                }
            }
        }

        [Fact]
        public void ReadWrittenFileUnderSharedOpaqueIsAllowed()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            FileArtifact outputInSharedOpaqueDir = CreateOutputFileArtifact(root: sharedOpaqueDir, prefix: "sod-file");
            FileArtifact sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(sourceFile),
                Operation.WriteFile(outputInSharedOpaqueDir, doNotInfer: true),
                Operation.Probe(outputInSharedOpaqueDir, doNotInfer: true),
                Operation.ReadFile(outputInSharedOpaqueDir, doNotInfer: true)
            });
            builder.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertCacheMiss().AssertSuccess();
        }

        [Theory]
        [Trait("Category", "SkipLinux")] // TODO(BUG)
        [MemberData(
            nameof(CrossProduct),
            new object[] { true, false },
            new object[] { true, false })]
        public void ProbeBeforeWriteIsAllowedWhenLazyDeletionIsDisabled(bool isLazyDeletionEnabled, bool areDependent)
        {
            Configuration.Schedule.UnsafeLazySODeletion = isLazyDeletionEnabled;

            var sod = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "sod-read-cycle"));
            var outFile = CreateOutputFileArtifact(root: sod, prefix: "sod-file");

            var proberPip = CreateAndSchedulePipBuilder(new[]
            {
                // probe the file produced by its downstream pip
                Operation.Probe(outFile, doNotInfer: true),

                // some output
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var producerBuilder = CreatePipBuilder(new[]
            {
                // establish dependency to prober
                Operation.ReadFile(areDependent 
                    ? proberPip.ProcessOutputs.GetOutputFiles().First()
                    : CreateSourceFile()),

                // produce the file the prober probed
                Operation.WriteFile(outFile),
            });
            producerBuilder.AddOutputDirectory(sod, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(producerBuilder);

            var result = RunScheduler();
            if (!isLazyDeletionEnabled && areDependent)
            {
                result.AssertSuccess();
                result.AssertPipExecutorStatCounted(PipExecutorCounter.ExistingFileProbeReclassifiedAsAbsentForNonExistentSharedOpaqueOutput, 1);
            }
            else
            {
                result.AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ProbingDirectoryUnderSharedOpaque(bool enableLazyOutputMaterialization)
        {
            /*
             PipA:
                Declares output SharedOpaque to Dir1
                Produces files in Dir1
                Produces files in Dir1\SubDir
             PipB:
                Declares output SharedOpaque to Dir1
                Depends on Pip1
                Probes Dir1\SubDir
                Produces files in Dir1 (not overlapping with PipA output)

            The probe of Dir1\SubDir should be allowed.
             */

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            var outputInSharedOpaqueDir = CreateOutputFileArtifact(root: sharedOpaqueDir, prefix: "sod-file");

            var sharedOpaqueSubDir = Path.Combine(sharedOpaqueDir, "subDir");
            var sharedOpaqueSubDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir);
            var sharedOpaqueSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDirPath);
            var outputInSharedOpaqueSubDir = CreateOutputFileArtifact(sharedOpaqueSubDir);

            // pipA: CreateDir('sod'), WriteFile('sod/sod-file'), CreateDirectory('sod/subDir'), WriteFile('sod/subDir/file')
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(sharedOpaqueDirArtifact, doNotInfer: true),
                Operation.WriteFile(outputInSharedOpaqueDir, doNotInfer: true),
                Operation.CreateDir(sharedOpaqueSubDirArtifact, doNotInfer:true),
                Operation.WriteFile(outputInSharedOpaqueSubDir, doNotInfer: true),
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            // pipB: WriteFile('pip-b-out'), Probe('sod/subDir')
            var pipBOutFile = CreateOutputFileArtifact(prefix: "pip-b-out");
            var pipBOutFileUnderOpaque = CreateOutputFileArtifact(prefix: "pip-b-out", root: sharedOpaqueDir);
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(pipBOutFile),
                Operation.Probe(sharedOpaqueSubDirArtifact, doNotInfer: true),
                Operation.WriteFile(pipBOutFileUnderOpaque, doNotInfer: true),
            });
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirArtifact.Path));
            builderB.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pipB = SchedulePipBuilder(builderB);

            // set filter output='*/pip-b-out'
            Configuration.Schedule.EnableLazyOutputMaterialization = enableLazyOutputMaterialization;
            Configuration.Filter = $"output='*{Path.DirectorySeparatorChar}{pipBOutFile.Path.GetName(Context.PathTable).ToString(Context.StringTable)}'";

            // run1 -> cache misses
            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // run2 -> cache hits
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            // if lazy materialization is on, PipA's output should not exist
            XAssert.AreEqual(enableLazyOutputMaterialization, !Directory.Exists(sharedOpaqueSubDir));
        }

        /// <summary>
        /// <see cref="SharedOpaqueOutputHelper.IsSharedOpaqueOutput"/> unconditionally returns true if
        /// the path given to it points to a directory or a non-existent file.
        ///
        /// This unit test tests this behavior in presence of symbolic links.
        /// </summary>
        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData(/*isSymlink*/  true, /*isDir*/  true, /*expected*/ false)] // symlink to dir     --> no (because symlink is a file)
        [InlineData(/*isSymlink*/  true, /*isDir*/ false, /*expected*/ false)] // symlink to file    --> no
        [InlineData(/*isSymlink*/  true, /*isDir*/  null, /*expected*/ false)] // symlink to missing --> no (because symlink is a file that exists)
        [InlineData(/*isSymlink*/ false, /*isDir*/  true, /*expected*/ true)]  // dir                --> yes
        [InlineData(/*isSymlink*/ false, /*isDir*/ false, /*expected*/ false)] // file               --> no
        [InlineData(/*isSymlink*/ false, /*isDir*/  null, /*expected*/ true)]  // missing            --> yes
        public void IsSharedOpaqueOutputTests(bool isSymlink, bool? isDir, bool expected)
        {
            AbsolutePath targetPath =
                isDir == null ? CreateUniqueSourcePath("missing") :
                isDir == true ? CreateUniqueDirectory(prefix: "dir") :
                CreateSourceFile();

            if (isDir == true)
            {
                XAssert.IsTrue(Directory.Exists(ToString(targetPath)));
            }
            else if (isDir == false)
            {
                XAssert.IsTrue(File.Exists(ToString(targetPath)));
            }

            AbsolutePath finalPath;
            if (isSymlink)
            {
                var suffix = isDir == null ? "missing" : isDir == true ? "dir" : "file";
                AbsolutePath linkPath = CreateUniqueSourcePath(prefix: "sym-to-" + suffix);
                var maybe = FileUtilities.TryCreateSymbolicLink(ToString(linkPath), ToString(targetPath), isTargetFile: isDir != true);
                XAssert.IsTrue(maybe.Succeeded);
                finalPath = linkPath;
            }
            else
            {
                finalPath = targetPath;
            }

            XAssert.AreEqual(expected, SharedOpaqueOutputHelper.IsSharedOpaqueOutput(ToString(finalPath)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestTimestampsForFilesInSharedOpaqueDirectories(bool storeOutputsToCache)
        {
            Configuration.Schedule.StoreOutputsToCache = storeOutputsToCache;

            AbsolutePath sodPath = AbsolutePath.Create(Context.PathTable, X($"{ObjectRoot}/sod"));
            AbsolutePath odPath = AbsolutePath.Create(Context.PathTable, X($"{ObjectRoot}/od"));

            // sod process pip
            var sodFile = CreateOutputFileArtifact(root: sodPath, prefix: "sod-file");
            var odFile = CreateOutputFileArtifact(root: odPath, prefix: "od-file");
            var sodPipBuilder = CreatePipBuilder(new []
            {
                Operation.WriteFile(sodFile, doNotInfer: true),
                Operation.WriteFile(odFile, doNotInfer: true),
            });
            sodPipBuilder.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
            sodPipBuilder.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);
            SchedulePipBuilder(sodPipBuilder);

            // regular process: writes one file into the same shared opaque directory, and one file elsewhere
            var processSodOutFile = CreateOutputFileArtifact(root: sodPath, prefix: "proc-sod-out");
            var processNonSodOutFile = CreateOutputFileArtifact(prefix: "proc-out");
            CreateAndSchedulePipBuilder(new[]
            {
                Operation.WriteFile(processSodOutFile),
                Operation.WriteFile(processNonSodOutFile)
            });

            // write file pips: one writes into the same shared opaque directory, one writes elsewhere
            var writePipSodOutFile = CreateOutputFileArtifact(root: sodPath, prefix: "write-sod-out");
            var writePipNonSodOutFile = CreateOutputFileArtifact(prefix: "write-out");
            CreateAndScheduleWriteFile(writePipSodOutFile, " ", new[] { "write pip sod" });
            CreateAndScheduleWriteFile(writePipNonSodOutFile, " ", new[] { "write pip" });

            // copy file pips: one copies into the same shared opaque directory, one copies elsewhere
            var copySourceFile = CreateSourceFileWithPrefix(prefix: "copy-source");
            var copyPipSodDestFile = CreateOutputFileArtifact(root: sodPath, prefix: "copy-sod-out");
            var copyPipNonSodDestFile = CreateOutputFileArtifact(prefix: "copy-out");
            CreateAndScheduleCopyFile(copySourceFile, copyPipSodDestFile);
            CreateAndScheduleCopyFile(copySourceFile, copyPipNonSodDestFile);

            // collect files in and not in shared opaque directories
            FileArtifact[] sodOutFiles                   = new[] { sodFile, processSodOutFile, copyPipSodDestFile, writePipSodOutFile };
            FileArtifact[] nonSodOutFiles                = new[] { odFile, processNonSodOutFile, copyPipNonSodDestFile, writePipNonSodOutFile };
            (AbsolutePath path, bool isInSod)[] outPaths = sodOutFiles
                .Select(f => (f.Path, true))
                .Concat(nonSodOutFiles.Select(f => (f.Path, false)))
                .ToArray();

            // 1st run
            RunScheduler().AssertSuccess();
            AssertTimestamps();

            // 2nd run: don't clear outputs beforehand
            RunScheduler().AssertSuccess();
            AssertTimestamps();

            // 3rd run: clear outputs beforehand
            foreach (var tuple in outPaths)
            {
                FileUtilities.DeleteFile(ToString(tuple.path));
            }
            RunScheduler().AssertSuccess();
            AssertTimestamps();

            AssertWarningEventLogged(LogEventId.ConvertToRunnableFromCacheFailed, count: 0, allowMore: true);

            // helper inner functions

            void AssertTimestamps()
            {
                foreach (var tuple in outPaths)
                {
                    var expandedPath = ToString(tuple.path);
                    XAssert.AreEqual(
                        tuple.isInSod,
                        SharedOpaqueOutputHelper.IsSharedOpaqueOutput(expandedPath),
                        "File: " + expandedPath);
                }
            }
        }

        [Fact]
        public void ProbesAndEnumerationsOnPathsLeadingToProducedFile()
        {
            /*
                pipA->sod\subDir\file1
                pipB->sod\subDir\file2

                1st run: begin->pipB->pipA->end

                2nd run: begin->pipA->pipB->end

                Note: Operation.CreateDir -- even though it's a 'write' access, it will be converted into either
                                             AbsentPathProbe or ExistingDirectoryProbe in ObserveInputProcessor

                We use Priority, Weight, and OrderDependency to force pips to run in a particular order without
                declaring an artifact dependency between them.
            */

            string sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(ProbesAndEnumerationsOnPathsLeadingToProducedFile)}");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var sharedOpaqueSubDir = Path.Combine(sharedOpaqueDir, "subDir");
            var sharedOpaqueSubDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir);
            var sharedOpaqueSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDirPath);

            var firstOutputInSharedOpaqueSubDir = CreateOutputFileArtifact(sharedOpaqueSubDir);
            var secondOutputInSharedOpaqueSubDir = CreateOutputFileArtifact(sharedOpaqueSubDir);

            var fileContent = "content";

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(sharedOpaqueDirArtifact, doNotInfer: true),
                Operation.CreateDir(sharedOpaqueSubDirArtifact, doNotInfer: true),
                Operation.WriteFile(firstOutputInSharedOpaqueSubDir, content: fileContent, doNotInfer: true),

                Operation.Probe(sharedOpaqueSubDirArtifact, doNotInfer: true),
                Operation.EnumerateDir(sharedOpaqueSubDirArtifact, doNotInfer: true)
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            builderA.Weight = 99;
            builderA.Priority = 0;

            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(sharedOpaqueDirArtifact, doNotInfer: true),
                Operation.CreateDir(sharedOpaqueSubDirArtifact, doNotInfer:true),
                Operation.WriteFile(secondOutputInSharedOpaqueSubDir, content: fileContent, doNotInfer: true),
            });
            builderB.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            builderB.Weight = 99;
            builderB.Priority = 99;

            // We do not need order dependency here because there is no prior content,
            // so no meaningful cache check will be happening.
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            // reset the graph and add the pips in a different order
            ResetPipGraphBuilder();

            builderA.Priority = 99;
            builderB.Priority = 0;

            pipA = SchedulePipBuilder(builderA);

            // without order dependency both pips will do the cache lookup at the same time, and we do not want that
            builderB.AddOrderDependency(pipA.Process.PipId);
            pipB = SchedulePipBuilder(builderB);

            var result = RunScheduler();

            // the following assert captures the current logic of how we handle the Operation.EnumerateDir
            // if you change that logic, you might need to change the assert as well
            result.AssertCacheHit(pipA.Process.PipId);

            // this asserts checks that the probes do not affect the cacheability of the pip
            result.AssertCacheHit(pipB.Process.PipId);
        }

        [Fact]
        public void NestedSharedOpaqueDirectories()
        {
            Configuration.Engine.AllowDuplicateTemporaryDirectory = true;
            /*
                PipA -> [sod] \ sodFile
                        [sod\subDir1] \ sodSubDir1File
                        [sod\sibDir2] \ sodSubDir2File
                
                Allowed accesses:
                PipB <- [sod] && sodFile
                PipC <- [sod\subDir1] && sodSubDir1File
                PipD <- [sod\sibDir2] && sodSubDir2File

                Disallowed accesses:
                PipE <- [sod] && sodSubDir2File
             */
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(NestedSharedOpaqueDirectories)}");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var sharedOpaqueSubDir1 = Path.Combine(sharedOpaqueDir, "subDir1");
            var sharedOpaqueSubDir1Path = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir1);
            var sharedOpaqueSubDir1Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDir1Path);

            var sharedOpaqueSubDir2 = Path.Combine(sharedOpaqueDir, "subDir2");
            var sharedOpaqueSubDir2Path = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir2);
            var sharedOpaqueSubDir2Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDir2Path);

            var sodFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var sodSubDir1File = CreateOutputFileArtifact(sharedOpaqueSubDir1);
            var sodSubDir2File = CreateOutputFileArtifact(sharedOpaqueSubDir2);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(sodFile, doNotInfer: true),
                Operation.WriteFile(sodSubDir1File, doNotInfer: true),
                Operation.WriteFile(sodSubDir2File, doNotInfer: true)
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            builderA.AddOutputDirectory(sharedOpaqueSubDir1Artifact, SealDirectoryKind.SharedOpaque);
            builderA.AddOutputDirectory(sharedOpaqueSubDir2Artifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), sodFile);
            var pipB = SchedulePipBuilder(builderB);

            var builderC = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueSubDir1Path), sodSubDir1File);
            var pipC = SchedulePipBuilder(builderC);

            var builderD = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueSubDir2Path), sodSubDir2File);
            var pipD = SchedulePipBuilder(builderD);

            RunScheduler().AssertSuccess();

            ResetPipGraphBuilder();

            pipA = SchedulePipBuilder(builderA);
            pipB = SchedulePipBuilder(builderB);
            pipC = SchedulePipBuilder(builderC);
            pipD = SchedulePipBuilder(builderD);

            // takes dependency on the root directory artifact, but tries to read a file from a subdirectory
            // this should result in a DFA (since that artifact does not contain that file)
            var builderE = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), sodSubDir2File);
            var pipE = SchedulePipBuilder(builderE);

            RunScheduler().AssertFailure().AssertCacheHitWithoutAssertingSuccess(
                pipA.Process.PipId,
                pipB.Process.PipId,
                pipC.Process.PipId,
                pipD.Process.PipId);

            AssertErrorEventLogged(LogEventId.FileMonitoringError, 1);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 1);
        }

        [Fact]
        public void NestedSharedOpaquesProperContentMaterialization()
        {
            /*
                PipA -> [sod] \ sodFile
                        [sod\subDir1] \ sodSubDir1File
                        [sod\sibDir2] \ sodSubDir2File                

                PipB <- [sod] && sodFile && inputB
                PipC <- [sod\subDir1] && sodSubDir1File && inputC
                PipD <- [sod\sibDir2] && sodSubDir2File && inputD    
             */
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            Configuration.Schedule.RequiredOutputMaterialization = RequiredOutputMaterialization.Minimal;

            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(Test)}");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var sharedOpaqueSubDir1 = Path.Combine(sharedOpaqueDir, "subDir1");
            var sharedOpaqueSubDir1Path = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir1);
            var sharedOpaqueSubDir1Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDir1Path);

            var sharedOpaqueSubDir2 = Path.Combine(sharedOpaqueDir, "subDir2");
            var sharedOpaqueSubDir2Path = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir2);
            var sharedOpaqueSubDir2Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDir2Path);

            var sodFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var sodSubDir1File = CreateOutputFileArtifact(sharedOpaqueSubDir1);
            var sodSubDir2File = CreateOutputFileArtifact(sharedOpaqueSubDir2);
            var inputB = CreateSourceFile();
            var inputC = CreateSourceFile();
            var inputD = CreateSourceFile();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(sodFile, doNotInfer: true),
                Operation.WriteFile(sodSubDir1File, doNotInfer: true),
                Operation.WriteFile(sodSubDir2File, doNotInfer: true)
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            builderA.AddOutputDirectory(sharedOpaqueSubDir1Artifact, SealDirectoryKind.SharedOpaque);
            builderA.AddOutputDirectory(sharedOpaqueSubDir2Artifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), inputB, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), sodFile);
            var pipB = SchedulePipBuilder(builderB);            
                       
            var builderC = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), inputC, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueSubDir1Path), sodSubDir1File);
            var pipC = SchedulePipBuilder(builderC);
                       
            var builderD = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), inputD, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueSubDir2Path), sodSubDir2File);
            var pipD = SchedulePipBuilder(builderD);

            // populate the cache
            RunScheduler().AssertSuccess();

            FileUtilities.DeleteDirectoryContents(sharedOpaqueDir, deleteRootDirectory: true);

            // change inputB -> pipB is executed -> only sodFile is materialized
            File.WriteAllText(ArtifactToString(inputB), Guid.NewGuid().ToString());

            RunScheduler().AssertSuccess()
                .AssertCacheHit(pipA.Process.PipId, pipC.Process.PipId, pipD.Process.PipId)
                .AssertCacheMiss(pipB.Process.PipId);

            XAssert.IsTrue(File.Exists(ArtifactToString(sodFile)));
            XAssert.IsFalse(File.Exists(ArtifactToString(sodSubDir1File)) || File.Exists(ArtifactToString(sodSubDir2File)));

            FileUtilities.DeleteDirectoryContents(sharedOpaqueDir, deleteRootDirectory: true);

            // change inputC -> pipC is executed -> only sodSubDir1File is materialized
            File.WriteAllText(ArtifactToString(inputC), Guid.NewGuid().ToString());

            RunScheduler().AssertSuccess()
                .AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipD.Process.PipId)
                .AssertCacheMiss(pipC.Process.PipId);

            XAssert.IsTrue(File.Exists(ArtifactToString(sodSubDir1File)));
            XAssert.IsFalse(File.Exists(ArtifactToString(sodFile)) || File.Exists(ArtifactToString(sodSubDir2File)));

            FileUtilities.DeleteDirectoryContents(sharedOpaqueDir, deleteRootDirectory: true);

            // change inputD -> pipD is executed -> only sodSubDir2File is materialized
            File.WriteAllText(ArtifactToString(inputD), Guid.NewGuid().ToString());

            RunScheduler().AssertSuccess()
                .AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId)
                .AssertCacheMiss(pipD.Process.PipId);

            XAssert.IsTrue(File.Exists(ArtifactToString(sodSubDir2File)));
            XAssert.IsFalse(File.Exists(ArtifactToString(sodFile)) || File.Exists(ArtifactToString(sodSubDir1File)));
        }

        [Theory]
        [InlineData("od/unrelated", 0, SealDirectoryKind.SharedOpaque)]
        [InlineData("od/nested", 1, SealDirectoryKind.SharedOpaque)]
        [InlineData(".", 2, SealDirectoryKind.SharedOpaque)]
        [InlineData("od/unrelated", 0, SealDirectoryKind.Opaque)]
        [InlineData("od/nested", 1, SealDirectoryKind.Opaque)]
        [InlineData(".", 2, SealDirectoryKind.Opaque)]
        public void OpaqueWithExclusionsIsHonored(string exclusionRelativePath, int expectedExcludedFiles, SealDirectoryKind outputDirectoryKind)
        {
            // Create two output files: od/o1 and od/nested/o2
            var opaqueDir = Path.Combine(ObjectRoot, "od");
            var nestedOpaqueDir = Path.Combine(opaqueDir, "nested");
            var opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            var nestedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, nestedOpaqueDir);
            var opaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(opaqueDirPath);
            var outputFile1 = CreateOutputFileArtifact(opaqueDir);
            var outputFile2 = CreateOutputFileArtifact(nestedOpaqueDir);

            // Write both files under the opaque
            var builderA = CreatePipBuilder(new Operation[]
                            {
                                Operation.WriteFile(outputFile1, doNotInfer: true),
                                Operation.CreateDir(DirectoryArtifact.CreateWithZeroPartialSealId(nestedOpaqueDirPath), doNotInfer: true),
                                Operation.WriteFile(outputFile2, doNotInfer: true)
                            });
            builderA.AddOutputDirectory(opaqueDirArtifact, outputDirectoryKind);

            // Set up an exclusion
            var exclusion = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, exclusionRelativePath));
            builderA.AddOutputDirectoryExclusion(exclusion);
            
            SchedulePipBuilder(builderA);

            var result = RunScheduler();

            // Validate the expected excluded files, which should manifest in DFAs for writing undeclared outputs
            if (expectedExcludedFiles == 0)
            { 
                result.AssertSuccess(); 
            }
            else 
            { 
                result.AssertFailure(); 
            }

            // On error, this event is logged once
            AssertErrorEventLogged(LogEventId.FileMonitoringError, expectedExcludedFiles > 0 ? 1 : 0);
            // This event is logged once per undeclared output
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput, expectedExcludedFiles);
            IgnoreWarnings();
        }

        private string ToString(AbsolutePath path) => path.ToString(Context.PathTable);
    }
}
