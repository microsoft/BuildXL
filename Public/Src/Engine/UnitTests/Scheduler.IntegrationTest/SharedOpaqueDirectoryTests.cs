// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            // It gets reported as a write in a statically declared source file
            AssertVerboseEventLogged(LogEventId.DependencyViolationWriteInStaticallyDeclaredSourceFile);
        }

        [Theory]
        [InlineData(RewritePolicy.AllowSameContentDoubleWrites)]
        [InlineData(RewritePolicy.DoubleWritesAreErrors)]
        public void RewritingDirectoryDependencyUnderSharedOpaqueIsNotAllowed(RewritePolicy doubleWritePolicy)
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
            builderA.RewritePolicy = doubleWritePolicy;

            var resA = SchedulePipBuilder(builderA);

            // The second pip depends on the shared opaque of the first pip, and tries to write to that same file (with same content)
            var operations = new List<Operation>();
            if (doubleWritePolicy == RewritePolicy.AllowSameContentDoubleWrites)
            {
                // Delete the file first, since write file operation implies append
                // and we want to be sure we write the same content than the first time
                operations.Add(Operation.DeleteFile(dependencyInOpaque, doNotInfer: true));
            }
            operations.Add(Operation.WriteFile(dependencyInOpaque, content: ContentToWrite, doNotInfer: true));

            var builderB = CreatePipBuilder(operations);
            builderB.AddInputDirectory(resA.ProcessOutputs.GetOutputDirectories().Single().Root);
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.RewritePolicy = doubleWritePolicy;

            SchedulePipBuilder(builderB);

            IgnoreWarnings();
            var result = RunScheduler();

            if (doubleWritePolicy == RewritePolicy.DoubleWritesAreErrors)
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
        [InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
        [Trait("Category", "WindowsOSOnly")] // TODO: investigate why this is flaky on Linux
        public void AbsentFileProbeFollowedByDynamicWriteIsBlockedWhenPipsAreIndependent(bool forceDependency)
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            
            FileArtifact absentFile = CreateOutputFileArtifact(sharedOpaqueDir);
            Analysis.IgnoreResult(FileUtilities.TryDeleteFile(absentFile.Path.ToString(Context.PathTable)));
            
            var dummyOut = CreateOutputFileArtifact(prefix: "dummyOut");
            Analysis.IgnoreResult(FileUtilities.TryDeleteFile(dummyOut.Path.ToString(Context.PathTable)));

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
                                                           ? Operation.ReadFile(pipAoutput) // force a BuildXL dependency
                                                           : Operation.Echo("Test"),        // represent no-op
                                                       Operation.WriteFile(absentFile, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddUntrackedFile(pipAoutput);
            var pipB = SchedulePipBuilder(builderB);

            //// We want to force the Pips to complete in a particular order so we utilze an external signaling file to delay
            //// the second process until the first has completed writing its output.
            //Thread t = new Thread(() =>
            //{
            //    while (!File.Exists(dummyOut.Path.ToString(Context.PathTable)))
            //    {
            //        Thread.Sleep(20);
            //    }

            //    File.WriteAllText(signalingFile.Path.ToString(Context.PathTable), "signaled");
            //});
            //t.Start();

            if (forceDependency)
            {
                RunScheduler().AssertSuccess();
            }
            else
            {
                RunScheduler(constraintExecutionOrder: new (Pip, Pip)[] {(pipA.Process, pipB.Process)}).AssertFailure();

                // We are expecting a write after an absent path probe
                AssertVerboseEventLogged(LogEventId.DependencyViolationWriteOnAbsentPathProbe);
                AssertVerboseEventLogged(LogEventId.AbsentPathProbeInsideUndeclaredOpaqueDirectory);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
        }

        [Theory]
        [InlineData(true)]  // when there is an explicit dependency between the two pips --> allowed
        [InlineData(false)] // when there is NO explicit dependency between the two pips --> DependencyViolationWriteOnAbsentPathProbe error
        public void DynamicWriteFollowedByAbsentPathFileProbeIsBlockedWhenPipsAreIndependent(bool forceDependency)
        {
            var signaligFile = CreateSourceFile();
            File.Delete(signaligFile.Path.ToString(Context.PathTable));
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
                                                Operation.WaitUntilFileExists(signaligFile),
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

            // We want to force the Pips to complete in a particular order so we utilze an external signaling file to delay
            // the second process until the first has completed writing its output.
            Thread t = new Thread(() =>
            {
                while (!File.Exists(dummyOut.Path.ToString(Context.PathTable)))
                {
                    Thread.Sleep(20);
                }
                File.WriteAllText(signaligFile.Path.ToString(Context.PathTable), "signaled");
            });
            t.Start();

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
            t.Join();
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
        public void AssumeCleanOutputsCompatibilityWithCacheConvergence()
        {
            // This unit test is to ensure that we clean up the shared opaque directory for the pips that are executed but cache-converged when /assumeCleanOutputs is enabled.
            // If we do not clean up the shared opaque, the non-deterministic behavior by the producer can cause DFAs for the consumer pips.  
            // We can potentially have different files in each run. If we do not clean up the shared opaque, the consumer pip can see all those files from different runs.

            Configuration.Schedule.RequiredOutputMaterialization = RequiredOutputMaterialization.Minimal;

            // Make cache look-ups always result in cache misses. This allows us to store outputs in the cache but get cache misses for the same pip next time.
            // This enables us to recreate cache convergence
            Configuration.Cache.ArtificialCacheMissOptions = new ArtificialCacheMissConfig()
            {
                Rate = ushort.MaxValue,
                Seed = 0,
                IsInverted = false
            };

            Configuration.Engine.AssumeCleanOutputs = true;
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.SkipFlaggingSharedOpaqueOutputs = true;

            // We will control the pip non-determinism with this untracked file
            string untracked = Path.Combine(ObjectRoot, "untracked.txt");

            // Pip has opaqueDir as a shared opaque output
            // Let's make the pip produce one of two outputs "non-deterministically":
            //  Scenario A: writes opaqueDir\write-if-A.out
            //  Scenario B: writes opaqueDir\write-if-B.out
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir"));

            var fileA = CreateOutputFileArtifact(opaqueDirPath, prefix: "write-if-A");
            var fileB = CreateOutputFileArtifact(opaqueDirPath, prefix: "write-if-B");
            FileArtifact pipAOutputPath = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(pipAOutputPath),
                Operation.WriteFileIfInputEqual(fileA, untracked, "A", "deterministic-content"), // Scenario A
                Operation.WriteFileIfInputEqual(fileB, untracked, "B", "deterministic-content"), // Scenario B
            }, description: "pipA");

            builderA.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderA.AddUntrackedFile(AbsolutePath.Create(Context.PathTable, untracked));
            builderA.RewritePolicy |= RewritePolicy.UnsafeFirstDoubleWriteWins;
            builderA.Options |= Process.Options.AllowUndeclaredSourceReads;
            var pipA = SchedulePipBuilder(builderA);

            var pipAOutput = pipA.ProcessOutputs.GetOutputFile(pipAOutputPath);
            var opaqueDirArtifact = pipA.ProcessOutputs.GetOutputDirectories().Single().Root;
            var builderB = CreatePipBuilder(new Operation[] {
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(opaqueDirPath), doNotInfer: true, readFiles: true),
                Operation.ReadFile(pipAOutput),

                 Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
            }, description: "pipB");

            builderB.AddInputDirectory(opaqueDirArtifact);
            builderB.Options |= Process.Options.AllowUndeclaredSourceReads;
            var pipB = SchedulePipBuilder(builderB);

            File.WriteAllText(untracked, "A");
            RunScheduler().AssertSuccess();

            FileUtilities.DeleteDirectoryContents(opaqueDirPath.ToString(Context.PathTable), deleteRootDirectory: true);
            ResetPipGraphBuilder();

            pipA = SchedulePipBuilder(builderA);
            pipB = SchedulePipBuilder(builderB);


            File.WriteAllText(untracked, "B");
            var result = RunScheduler().AssertSuccess();
            result.AssertPipExecutorStatCounted(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged, 2);
            // fileB is written, but it gets cleaned up after pipA is converged.
            XAssert.IsFalse(File.Exists(ArtifactToString(fileB)));
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
            builderA.RewritePolicy = RewritePolicy.AllowSameContentDoubleWrites;
            var processA = SchedulePipBuilder(builderA);

            // And the same for this one. The previous one has a static dummy output so just they don't collide in weak fingerprint
            var builderB = CreatePipBuilder(new Operation[] { Operation.ReadAndWriteFile(outputAsSource, output, doNotInfer: true) });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.RewritePolicy = RewritePolicy.AllowSameContentDoubleWrites;
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

        [Fact]
        public void CacheConvergenceCanTriggerAdditionalWriteInUndeclaredFileViolations()
        {
            // Make cache look-ups always result in cache misses. This allows us to store outputs in the cache but get cache misses for the same pip next time.
            // This enables us to recreate cache convergence
            Configuration.Cache.ArtificialCacheMissOptions = new ArtificialCacheMissConfig()
            {
                Rate = ushort.MaxValue,
                Seed = 0,
                IsInverted = false
            };

            Configuration.Engine.AllowDuplicateTemporaryDirectory = true;

            // Shared opaque to produce outputs
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            // First we will generate a safe different-content write over this file
            string originalContent = "content";
            var output = CreateOutputFileArtifact(sharedOpaqueDirPath);
            var outputAsSource = FileArtifact.CreateSourceFile(output.Path);

            Directory.CreateDirectory(sharedOpaqueDir);
            File.WriteAllText(outputAsSource.Path.ToString(Context.PathTable), originalContent);

            // Read the source file in its original form before the write, so same-content rewrite can actually kick in on the second build without making the writer be a miss
            var builderA = CreatePipBuilder(new Operation[] { 
                Operation.ReadFile(outputAsSource, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))});
            builderA.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;
            builderA.Options |= Process.Options.AllowUndeclaredSourceReads;
            var processA = SchedulePipBuilder(builderA);

            Environment.SetEnvironmentVariable("FILE_CONTENT", "different content");
            // This pip writes to the output file the content specified in FILE_CONTENT, which for this first build is a different content one. 
            // However processA is reading the file in an ordered way, so this should be allowed
            var builderB = CreatePipBuilder(new Operation[] {
                Operation.DeleteFile(output), // delete it first since the write operation always appends
                Operation.WriteEnvVariableToFile(output, "FILE_CONTENT", doNotInfer: true)}) ;
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;
            builderB.Options |= Process.Options.AllowUndeclaredSourceReads;
            builderB.AddInputFile(processA.ProcessOutputs.GetOutputFiles().Single());
            // Set the variable as passthrough so we can later change it without affecting caching
            builderB.SetPassthroughEnvironmentVariable(StringId.Create(Context.StringTable, "FILE_CONTENT"));
            var processB = SchedulePipBuilder(builderB);

            RunScheduler(runNameOrDescription: "First build").AssertSuccess();

            // We should see the allowed rewrite
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);

            // Change the content of the environment variable. This will make the writer pip change the output it generates to match the content of the original file
            Environment.SetEnvironmentVariable("FILE_CONTENT", originalContent);
            // Restore the file to its original form, so process A will also be a hit
            File.WriteAllText(outputAsSource.Path.ToString(Context.PathTable), originalContent);

            ResetPipGraphBuilder();

            SchedulePipBuilder(builderA);
            SchedulePipBuilder(builderB);

            // For the second build, add a racy reader, so the write being same or different content actually matters.
            var builderC = CreatePipBuilder(new Operation[] { Operation.ReadFile(outputAsSource, doNotInfer: true) });
            builderC.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderC.RewritePolicy = RewritePolicy.SafeSourceRewritesAreAllowed;
            builderC.Options |= Process.Options.AllowUndeclaredSourceReads;
            var processC = SchedulePipBuilder(builderC);

            // Run pips now making sure they run in order (C can hit a write lock from B). Check convergence actually happened
            var result = RunScheduler(runNameOrDescription: "Second build", constraintExecutionOrder: new (Pip, Pip)[] { (processC.Process, processB.Process) }).AssertFailure();
            result.AssertPipExecutorStatCounted(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged, 2);

            // Pre-convergence we should see an allowed rewrite (even though there is a racy reader, the produced content is the same)
            AssertVerboseEventLogged(LogEventId.AllowedRewriteOnUndeclaredFile);

            // However, on cache convergence the converged content is different
            // So we should see a file monitoring error
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
            originalProducerPipBuilder.RewritePolicy |= RewritePolicy.UnsafeFirstDoubleWriteWins;
            ProcessWithOutputs originalProducerPipResult = SchedulePipBuilder(originalProducerPipBuilder);

            // doubleWriteProducerPip writes the same artifact in a shared opaque directory
            ProcessBuilder doubleWriteProducerPipBuilder = CreatePipBuilder(writeOperations);
            doubleWriteProducerPipBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            // Let's make doubleWriteProducerPip depend on originalProducerPip so we avoid potential file locks on the double write
            doubleWriteProducerPipBuilder.AddInputFile(originalProducerPipResult.ProcessOutputs.GetOutputFiles().Single());

            // Set UnsafeFirstDoubleWriteWins
            doubleWriteProducerPipBuilder.RewritePolicy |= RewritePolicy.UnsafeFirstDoubleWriteWins;
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

            RunScheduler().AssertCacheMiss(pip.Process.PipId).AssertSuccess();
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test is skipped because its falky on mac
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
        public void SharedOpaqueContainsExclusiveOpaque()
        {
            Configuration.Engine.AllowDuplicateTemporaryDirectory = true;
            /*
                PipA -> [sod] \ sodFile
                        [eod\subDir1] \ eodSubDir1File
                        [eod\sibDir2] \ eodSubDir2File

                Allowed accesses:
                PipB <- [sod] && sodFile
                PipC <- [eod\subDir1] && eodSubDir1File
                PipD <- [eod\sibDir2] && eodSubDir2File

                Disallowed accesses:
                PipE <- [sod] && eodSubDir2File
                PipF <- [eod\subDir1] && sodFile
             */
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(NestedSharedOpaqueDirectories)}");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var exclusiveOpaqueSubDir1 = Path.Combine(sharedOpaqueDir, "subDir1");
            var exclusiveOpaqueSubDir1Path = AbsolutePath.Create(Context.PathTable, exclusiveOpaqueSubDir1);
            var exclusiveOpaqueSubDir1Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(exclusiveOpaqueSubDir1Path);

            var exclusiveOpaqueSubDir2 = Path.Combine(sharedOpaqueDir, "subDir2");
            var exclusiveOpaqueSubDir2Path = AbsolutePath.Create(Context.PathTable, exclusiveOpaqueSubDir2);
            var exclusiveOpaqueSubDir2Artifact = DirectoryArtifact.CreateWithZeroPartialSealId(exclusiveOpaqueSubDir2Path);

            var sodFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var exclusiveSubDir1File = CreateOutputFileArtifact(exclusiveOpaqueSubDir1);
            var exclusiveSubDir2File = CreateOutputFileArtifact(exclusiveOpaqueSubDir2);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(sodFile, doNotInfer: true),
                Operation.WriteFile(exclusiveSubDir1File, doNotInfer: true),
                Operation.WriteFile(exclusiveSubDir2File, doNotInfer: true)
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            builderA.AddOutputDirectory(exclusiveOpaqueSubDir1Artifact, SealDirectoryKind.Opaque);
            builderA.AddOutputDirectory(exclusiveOpaqueSubDir2Artifact, SealDirectoryKind.Opaque);
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), sodFile);
            var pipB = SchedulePipBuilder(builderB);

            var builderC = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(exclusiveOpaqueSubDir1Path), exclusiveSubDir1File);
            var pipC = SchedulePipBuilder(builderC);

            var builderD = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(exclusiveOpaqueSubDir2Path), exclusiveSubDir2File);
            var pipD = SchedulePipBuilder(builderD);

            RunScheduler().AssertSuccess();

            ResetPipGraphBuilder();

            pipA = SchedulePipBuilder(builderA);
            pipB = SchedulePipBuilder(builderB);
            pipC = SchedulePipBuilder(builderC);
            pipD = SchedulePipBuilder(builderD);

            // takes dependency on the root directory artifact, but tries to read a file from a subdirectory
            // this should result in a DFA (since that artifact does not contain that file)
            var builderE = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath), exclusiveSubDir2File);
            var pipE = SchedulePipBuilder(builderE);

            // takes dependency on an exclusive subdirectory artifact, but tries to read a file from the root shared opaque
            // this should result in a DFA (since that artifact does not contain that file)
            var builderF = CreateOpaqueDirectoryConsumer(CreateOutputFileArtifact(), null, pipA.ProcessOutputs.GetOpaqueDirectory(exclusiveOpaqueSubDir1Path), sodFile);
            var pipF = SchedulePipBuilder(builderF);

            RunScheduler().AssertFailure().AssertCacheHitWithoutAssertingSuccess(
                pipA.Process.PipId,
                pipB.Process.PipId,
                pipC.Process.PipId,
                pipD.Process.PipId);

            AssertErrorEventLogged(LogEventId.FileMonitoringError, 2);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 2);
        }

        [Fact]
        public void SharedOpaqueCannotWriteInExclusiveOpaqueInDifferentPip()
        {
            Configuration.Engine.AllowDuplicateTemporaryDirectory = true;
            
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(NestedSharedOpaqueDirectories)}");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            var exclusiveOpaqueSubDir = Path.Combine(sharedOpaqueDir, "subDir");
            var exclusiveOpaqueSubDirPath = AbsolutePath.Create(Context.PathTable, exclusiveOpaqueSubDir);
            var exclusiveOpaqueSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(exclusiveOpaqueSubDirPath);

            var sodFile = CreateOutputFileArtifact(sharedOpaqueDir);
            var exclusiveSubDir1File = CreateOutputFileArtifact(exclusiveOpaqueSubDir);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(exclusiveSubDir1File, doNotInfer: true),
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[] {});
            builderB.AddOutputDirectory(exclusiveOpaqueSubDirArtifact, SealDirectoryKind.Opaque);
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertFailure();

            AllowErrorEventLoggedAtLeastOnce(LogEventId.FileMonitoringError);
            AssertVerboseEventLogged(LogEventId.DependencyViolationWriteInExclusiveOpaqueDirectory, 1);
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
            // This although looking unrelated helps relaxing observed input processor
            // to make sure violations are properly generated (otherwise if we miss blocking a write
            // it can reach the processor and it will misinterpreted as a read and flag it, so we'll
            // see a violation but it is not the right one)
            builderA.Options |= Process.Options.AllowUndeclaredSourceReads;

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

        [Fact]
        public void OpaqueOutputsAreCleanedOnRetryExitCode()
        {
            Configuration.Schedule.ProcessRetries = 1;

            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            FileArtifact untrackedFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "untracked.txt"));

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            FileArtifact sodOutput1 = CreateOutputFileArtifact(root: sharedOpaqueDir, prefix: "sod-file");
            FileArtifact sodOutput2 = CreateOutputFileArtifact(root: sharedOpaqueDir, prefix: "sod-file");

            var builder = CreatePipBuilder(new Operation[]
            {
                // a dummy output so the engine does not complain about a pip with no outputs
                Operation.WriteFile(FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "out.txt"))),
                // if untracked.txt contains "0", create sodOutput1
                Operation.WriteFileIfInputEqual(sodOutput1, ToString(untrackedFile.Path), "0"),
                // if untracked.txt contains "01", create sodOutput2 ("01" because WriteFile appends to the file)
                Operation.WriteFileIfInputEqual(sodOutput2, ToString(untrackedFile.Path), "01"),
                // Change the content of untracked.txt. This makes the pip non-deterministic.
                Operation.WriteFile(untrackedFile, content: "1", doNotInfer: true),
                Operation.SucceedOnRetry(untrackedStateFilePath: stateFile, failExitCode: 42)
            });
            builder.RetryExitCodes = global::BuildXL.Utilities.Collections.ReadOnlyArray<int>.From(new int[] { 42 });
            builder.AddUntrackedFile(stateFile.Path);
            builder.AddUntrackedFile(untrackedFile.Path);
            builder.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            SchedulePipBuilder(builder);

            // set the initial value
            File.WriteAllText(ToString(untrackedFile.Path), "0");
            // sanity check - sod outputs should not exist at this point
            XAssert.IsFalse(File.Exists(ToString(sodOutput1.Path)));
            XAssert.IsFalse(File.Exists(ToString(sodOutput2.Path)));

            var result = RunScheduler();

            result.AssertSuccess();
            AssertVerboseEventLogged(LogEventId.PipWillBeRetriedDueToExitCode, 1);

            /*
             * sodOutput1 should not exist:
             *  pip starts
             *  untrackedFile.txt contains "0" -> sodOutput1 is created
             *  untrackedFile.txt contains "0" -> sodOutput2 is NOT created
             *  "1" is written to untrackedFile.txt
             *  pip exits with a retrieable exit code
             *
             *  pip is retried (previous outputs must be cleaned)
             *  untrackedFile.txt contains "01" -> sodOutput1 is NOT created
             *  untrackedFile.txt contains "01" -> sodOutput2 is created
             */
            XAssert.IsFalse(File.Exists(ToString(sodOutput1.Path)));
            XAssert.IsTrue(File.Exists(ToString(sodOutput2.Path)));
        }

        [Fact]
        public void TreatAPathAsBothFileAndDirectoryIsHandled()
        {
            // Creates a pip that writes and deletes a file and later creates a directory using the same path
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.CreateDir(outputInSharedOpaque, doNotInfer:true)
            });

            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            var pipA = SchedulePipBuilder(builderA);

            // Just probe the directory and create a dummy file
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.Probe(outputInSharedOpaque, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            var pipB = SchedulePipBuilder(builderB);

            // Even though there is a write in a path that is a shared opaque candidate, the fact that the final artifact on that
            // path ends up being a directory should be enough to discard all writes.
            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void UntrackedPathsAsSharedOpaqueInputsAreHonored()
        {
            // First create a pip that produces a couple outputs under a shared opaque
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);

            var sharedOpaqueSubDir = Path.Combine(sharedOpaqueDir, "subDir1");
            var sharedOpaqueSubDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueSubDir);
            var sharedOpaqueSubDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueSubDirPath);

            FileArtifact outputInSharedOpaqueSubDir = CreateOutputFileArtifact(sharedOpaqueSubDirPath);
            FileArtifact dummyOutput = FileArtifact.CreateOutputFile(sharedOpaqueDirPath.Combine(Context.PathTable, "dummy.txt"));

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, "content", doNotInfer:true),
                Operation.CreateDir(sharedOpaqueSubDirArtifact, doNotInfer: true),
                Operation.WriteFile(outputInSharedOpaqueSubDir, "content", doNotInfer: true)
            });

            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            var pipA = SchedulePipBuilder(builderA);

            // Then create a pip that depends on pipA shared opaque, but untracks the consumed files
            // (as files and as cones)
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputInSharedOpaque, doNotInfer:true),
                Operation.ReadFile(outputInSharedOpaqueSubDir, doNotInfer:true),
                Operation.WriteFile(dummyOutput, "dummy output")
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            builderB.AddUntrackedFile(outputInSharedOpaque.Path);
            builderB.AddUntrackedDirectoryScope(sharedOpaqueSubDirPath);

            var pipB = SchedulePipBuilder(builderB);

            // Run once
            RunScheduler().AssertSuccess();

            // Simulate scrubbing
            File.Delete(outputInSharedOpaque.Path.ToString(Context.PathTable));
            FileUtilities.DeleteDirectoryContents(sharedOpaqueDirPath.ToString(Context.PathTable), deleteRootDirectory: true);

            // Reset and re-add pips, but now A producing files with different content
            ResetPipGraphBuilder();

            builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, "new content", doNotInfer:true),
                Operation.CreateDir(sharedOpaqueSubDirArtifact, doNotInfer: true),
                Operation.WriteFile(outputInSharedOpaqueSubDir, "new content", doNotInfer: true)
            });

            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            pipA = SchedulePipBuilder(builderA);

            builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputInSharedOpaque, doNotInfer:true),
                Operation.ReadFile(outputInSharedOpaqueSubDir, doNotInfer:true),
                Operation.WriteFile(dummyOutput, "dummy output")
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            builderB.AddUntrackedFile(outputInSharedOpaque.Path);
            builderB.AddUntrackedDirectoryScope(sharedOpaqueSubDirPath);

            pipB = SchedulePipBuilder(builderB);

            // Pip A should be a miss, but pip B should still be a hit because the output files that changed were untracked
            RunScheduler()
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertCacheHit(pipB.Process.PipId);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void MultiplePipsProduceTheSameTemporaryFile(bool dependencyBetweenPips, bool secondPipDeletesFile)
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir, "tempFileUnderSOD");

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Using weight/priority to force pipB to run after pipA
            builderA.Weight = 1000;
            builderA.Priority = Process.MaxPriority;
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(secondPipDeletesFile
                ? new Operation[]
                {
                    Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                    Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
                }
                : new Operation[] { Operation.WriteFile(outputInSharedOpaque, doNotInfer: true) });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            if (dependencyBetweenPips)
            {
                builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            }

            builderB.Weight = 1000;
            builderB.Priority = Process.MinPriority;
            var pipB = SchedulePipBuilder(builderB);

            var res = RunScheduler();
            if (dependencyBetweenPips && secondPipDeletesFile)
            {
                res.AssertSuccess();
            }
            else
            {
                res.AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError, count: 1);
            }
        }

        /// <summary>
        /// This test is different from a double-write test because the engine will interpret
        /// the deletion of a file as if the second pip created/deleted a temp file.
        /// </summary>
        [Theory]
        [Trait("Category", "SkipLinux")] // TODO(BUG 1751624)
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void DeletingPreviouslyProducedFilesIsNotAllowed(bool dependencyBetweenPips)
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir, "tempFileUnderSOD");

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Using weight/priority to force pipB to run after pipA
            builderA.Weight = 1000;
            builderA.Priority = Process.MaxPriority;
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            if (dependencyBetweenPips)
            {
                builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            }

            builderB.Weight = 1000;
            builderB.Priority = Process.MinPriority;
            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FileMonitoringError, count: 1);
            if (dependencyBetweenPips)
            {
                // If there is dependency, delete operation will be blocked by detours (due to restrictions in FileAccessManifest),
                // and as a result the pip will fail.
                AssertErrorEventLogged(ProcessesLogEventId.PipProcessError, count: 1);
            }
        }

        [Fact]
        public void DependencyChainRequiredForProducersOfTheSameTempFile()
        {
            // 1) Three pips produce the same temp file.
            // 2) There is a dependency between pipA and pipB, as well as between pipA and pipC.
            // 3) There is no dependency between pipB and pipC
            // 4) Either pipB or pipC (whichever runs last) should result in a DFA.
            //     a) To make this test deterministic, we force that the pips are run in the following order: pipA, pipB, pipC

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir, "tempFileUnderSOD");

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Using weight/priority to make pipA run first
            builderA.Weight = 1000;
            builderA.Priority = Process.MaxPriority;
            var pipA = SchedulePipBuilder(builderA);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            builderB.Weight = 1000;
            builderB.Priority = Process.MaxPriority / 2;
            var pipB = SchedulePipBuilder(builderB);

            var builderC = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputInSharedOpaque, doNotInfer:true),
                Operation.DeleteFile(outputInSharedOpaque, doNotInfer:true),
            });
            builderC.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderC.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            builderC.Weight = 1000;
            builderC.Priority = Process.MinPriority;
            var pipC = SchedulePipBuilder(builderC);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(LogEventId.FileMonitoringError, count: 1);
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void DirectoryJunctionIsInterpretedAsIsWhenResolutionIsOn()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            string targetDirString = Path.Combine(ObjectRoot, "target");

            Directory.CreateDirectory(targetDirString);

            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            DirectoryArtifact junctionDir = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath.Combine(Context.PathTable, "junction"));

            AbsolutePath targetPath = AbsolutePath.Create(Context.PathTable, targetDirString);
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(targetPath);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateJunction(junctionDir, targetDir, doNotInfer: true)
            });

            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);

            SchedulePipBuilder(builderA);

            // By asserting success we know the junction was not resolved, otherwise we would get a DFA because the target
            // falls into a directory where writes are not allowed
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void EmptyDirUnderSharedOpaqueIsConsistentlyInterpretedOnCacheReplay()
        {
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            AbsolutePath testDirPath = sharedOpaqueDirPath.Combine(Context.PathTable, "test-dir");
            DirectoryArtifact testDir = DirectoryArtifact.CreateWithZeroPartialSealId(testDirPath);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.DirProbe(testDir),
                Operation.CreateDir(testDir),
            });

            builderA.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            // Allowing undeclared reads is key here since it is what makes dir probe interpretation
            // aware of directories our different file system views don't account for
            builderA.Options |= Process.Options.AllowUndeclaredSourceReads;

            var pipA = SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();

            // Delete the created directory to simulate a fresh build
            Directory.Delete(testDirPath.ToString(Context.PathTable));
            // We should get a cache hit
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId);
        }

        [Fact]
        public void OutputMaterializationExclusionRootsAreProperlyAppliedToTheContentOfSharedOpaques()
        {
            /*
               Pip outputs:
               \sod
                   \file1
                   \subDir
                       \file2
               \file3

               Exclusion root:
               \sod\subDir 
            */

            var sod = Path.Combine(ObjectRoot, "sod");
            var sodSubDir = Path.Combine(sod, "subDir");
            var sodPath = AbsolutePath.Create(Context.PathTable, sod);
            var sodSubDirPath = AbsolutePath.Create(Context.PathTable, sodSubDir);

            var sodArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sodPath);

            var fileUnderSod = CreateOutputFileArtifact(sod);
            var fileUnderSubdirUnderSod = CreateOutputFileArtifact(sodSubDir);
            var fileOutsideSod = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(fileUnderSod, doNotInfer: true),
                Operation.WriteFile(fileUnderSubdirUnderSod, doNotInfer: true),

                Operation.WriteFile(fileOutsideSod)
            });
            builderA.AddOutputDirectory(sodArtifact, kind: SealDirectoryKind.SharedOpaque);
            
            Configuration.Schedule.RequiredOutputMaterialization = RequiredOutputMaterialization.Explicit;
            Configuration.Schedule.OutputMaterializationExclusionRoots.Add(sodSubDirPath);

            var pipA = SchedulePipBuilder(builderA);

            RunScheduler().AssertSuccess();

            // For uncached execution, all outputs will be written
            XAssert.IsTrue(Exists(fileUnderSod));
            XAssert.IsTrue(Exists(fileUnderSubdirUnderSod));
            XAssert.IsTrue(Exists(fileOutsideSod));

            // Delete the outputs in order to test whether they are materialized for cached execution
            Delete(fileUnderSod);
            Delete(fileUnderSubdirUnderSod);
            Delete(fileOutsideSod);

            RunScheduler().AssertCacheHit(pipA.Process.PipId);

            // File is not under the exclusion root, so it must be present.
            XAssert.IsTrue(Exists(fileUnderSod));            
            // File is under the exclusion root, it should not have been materialized.
            XAssert.IsFalse(Exists(fileUnderSubdirUnderSod));
            // File is neither under sod nor exclusion root, so it should be present.
            XAssert.IsTrue(Exists(fileOutsideSod));
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void UnresolvedAbsentAccessesContainingReparsePointsAreProperlyHandled(bool doProbe)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            AbsolutePath symlinkTarget = sharedOpaqueDirPath.Combine(Context.PathTable, "target");
            AbsolutePath symlink = sharedOpaqueDirPath.Combine(Context.PathTable, "symlink");
            AbsolutePath fileViaSymlink = symlink.Combine(Context.PathTable, "file");
            AbsolutePath fileViaTarget = symlinkTarget.Combine(Context.PathTable, "file");

            // Create sod/target/file
            Directory.CreateDirectory(symlinkTarget.ToString(Context.PathTable));
            File.WriteAllText(fileViaTarget.ToString(Context.PathTable), "content");

            var builder = CreatePipBuilder(new Operation[]
            {
                // Probe or read the file via the symlink. This is an absent access, the symlink is not there yet
                doProbe
                ? Operation.Probe(FileArtifact.CreateSourceFile(fileViaSymlink), doNotInfer: true)
                : Operation.ReadFile(FileArtifact.CreateSourceFile(fileViaSymlink), doNotInfer: true),
                // Create the dir symlink. That makes the path that was probed above become a present one.
                Operation.CreateSymlink(DirectoryArtifact.CreateWithZeroPartialSealId(symlink), DirectoryArtifact.CreateWithZeroPartialSealId(symlinkTarget), Operation.SymbolicLinkFlag.DIRECTORY, doNotInfer: true),
            });

            builder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            // Delete the created symlink to simulate a fresh build
            Directory.Delete(symlink.ToString(Context.PathTable));
            
            // We should get a cache hit.
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void UnresolvedAbsentProbesContainingReparsePointsIsNotReportedIfIsAlsoOutput()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            AbsolutePath symlinkTarget = sharedOpaqueDirPath.Combine(Context.PathTable, "target");
            AbsolutePath symlink = sharedOpaqueDirPath.Combine(Context.PathTable, "symlink");
            AbsolutePath fileViaSymlink = symlink.Combine(Context.PathTable, "file");
            AbsolutePath fileViaTarget = symlinkTarget.Combine(Context.PathTable, "file");

            // Create sod/target/file
            Directory.CreateDirectory(symlinkTarget.ToString(Context.PathTable));

            var builder = CreatePipBuilder(new Operation[]
            {
                // Probe the file via the symlink. This is an absent probe, the symlink is not there yet
                Operation.Probe(FileArtifact.CreateSourceFile(fileViaSymlink), doNotInfer: true),
                // Create the dir symlink. 
                Operation.CreateSymlink(DirectoryArtifact.CreateWithZeroPartialSealId(symlink), DirectoryArtifact.CreateWithZeroPartialSealId(symlinkTarget), Operation.SymbolicLinkFlag.DIRECTORY, doNotInfer: true),
                // Create the file via the real path. That makes the path that was probed above become a present one.
                Operation.WriteFile(FileArtifact.CreateSourceFile(fileViaTarget), doNotInfer: true)
            });

            builder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            builder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var pip = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            // Delete the created symlink and file to simulate a fresh build
            File.Delete(fileViaTarget.ToString(Context.PathTable));
            Directory.Delete(symlink.ToString(Context.PathTable));

            // We should get a cache hit. Without compensating for the absent probe this would be a miss.
            RunScheduler().AssertSuccess().AssertCacheHit(pip.Process.PipId);
        }

        [Fact]
        public void DirectoryProbeWithNoProducedFilesUnderneathReplayConsistently()
        {
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));
            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sodSubDir = Path.Combine(sharedOpaqueDir, "subDir");
            DirectoryArtifact sharedOpaqueSubDirPath = DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, sodSubDir));
            var source = CreateSourceFile();

            File.WriteAllText(source.Path.ToString(Context.PathTable), "input");

            // Creates a pip that only creates a directory (with no files underneath) the second time is run. The first time it does nothing.
            var dirCreator = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(source, doNotInfer: true),
                Operation.CreateDirOnRetry(stateFile, FileArtifact.CreateSourceFile(sharedOpaqueSubDirPath))
            });
            dirCreator.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            dirCreator.Options |= Process.Options.AllowUndeclaredSourceReads;
            dirCreator.AddUntrackedFile(stateFile.Path);

            var dirCreatorPip = SchedulePipBuilder(dirCreator);

            // Create a pip that probes the directory and depends on the dir creator one
            var prober = CreatePipBuilder(new Operation[]
            {
                Operation.Probe(sharedOpaqueSubDirPath),
            });
            prober.AddInputDirectory(dirCreatorPip.ProcessOutputs.GetOutputDirectories().Single().Root);
            prober.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            prober.Options |= Process.Options.AllowUndeclaredSourceReads;

            var proberPip = SchedulePipBuilder(prober);

            // Run both pips. Check the directory is not created
            // This implies the probe done by the prober pip results in an absent path probe (since the directory is missing, there is no way
            // we know this will turn into a directory on subsequent runs)
            RunScheduler().AssertSuccess(); 
            XAssert.IsFalse(Directory.Exists(sodSubDir));

            // Change the input so we force the re-run of the directory creation pip. Observe changing this
            // input file shouldn't introduce per-se a cache miss for the prober pip
            File.WriteAllText(source.Path.ToString(Context.PathTable), "modified input");

            // The second time this runs the dir creator pip should be a miss because of the modified input, and therefore the directory is created
            // However, we want the prober pip to be a hit since bxl does not replay directory creations (with no producing files underneath) so the presence
            // of this directory should be inconsequential
            RunScheduler().AssertSuccess().AssertCacheHit(proberPip.Process.PipId);
            XAssert.IsTrue(Directory.Exists(sodSubDir));

            // Simulate scrubbing/fresh machine
            FileUtilities.DeleteFile(sodSubDir);

            // We should see again a cache hit, now for both pips
            RunScheduler().AssertSuccess().AssertCacheHit(proberPip.Process.PipId, dirCreatorPip.Process.PipId);
        }

        public enum AllowListKind
        {
            None,
            Cacheable,
            UnCacheable
        }

        [Theory]
        [InlineData(AllowListKind.None)]
        [InlineData(AllowListKind.Cacheable)]
        [InlineData(AllowListKind.UnCacheable)]
        public void DoubleWritesWithAllowList(AllowListKind allowListKind)
        {
            EngineEnvironmentSettings.ApplyAllowListToDynamicOutputs.Value = true;

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact sharedOutput = FileArtifact.CreateOutputFile(sharedOpaqueDirPath.Combine(Context.PathTable, "shared.out"));
            FileArtifact source = CreateSourceFile(ObjectRoot);
            WriteSourceFile(source);
            FileArtifact outputP = CreateOutputFileArtifact(sharedOpaqueDir);
            FileArtifact outputQ = CreateOutputFileArtifact(sharedOpaqueDir);

            if (allowListKind != AllowListKind.None)
            {
                var entry = new global::BuildXL.Utilities.Configuration.Mutable.FileAccessAllowlistEntry
                {
                    Name = nameof(DoubleWritesWithAllowList),
                    ToolPath = new DiscriminatingUnion<FileArtifact, PathAtom>(TestProcessExecutable.Path.GetName(Context.PathTable)),
                    PathFragment = ArtifactToString(sharedOutput),
                };

                if (allowListKind == AllowListKind.Cacheable)
                {
                    Configuration.CacheableFileAccessAllowlist.Add(entry);
                }
                else
                {
                    Configuration.FileAccessAllowList.Add(entry);
                }
            }

            ProcessBuilder pBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(source),
                Operation.WriteFile(outputP, doNotInfer: true),
                Operation.WriteFile(sharedOutput, doNotInfer: true)
            });
            pBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            pBuilder.RewritePolicy |= RewritePolicy.DefaultSafe;
            ProcessWithOutputs p = SchedulePipBuilder(pBuilder);

            ProcessBuilder qBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputP, doNotInfer: true),
                Operation.WriteFile(outputQ, doNotInfer: true),
                Operation.WriteFile(sharedOutput, doNotInfer: true)
            });
            qBuilder.AddInputDirectory(p.ProcessOutputs.GetOpaqueDirectory(sharedOpaqueDirPath));
            qBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            qBuilder.RewritePolicy |= RewritePolicy.DefaultSafe;
            ProcessWithOutputs q = SchedulePipBuilder(qBuilder);

            if (allowListKind == AllowListKind.None)
            {
                RunScheduler().AssertFailure();
                AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
            else
            {
                RunScheduler().AssertSuccess();
                File.Delete(ArtifactToString(sharedOutput));

                var secondRunResult = RunScheduler().AssertSuccess();
                if (allowListKind == AllowListKind.Cacheable)
                {
                    secondRunResult.AssertCacheHit(p.Process.PipId, q.Process.PipId);
                    XAssert.IsFalse(File.Exists(ArtifactToString(sharedOutput)));
                }
                else
                {
                    secondRunResult.AssertCacheMiss(p.Process.PipId, q.Process.PipId);
                    XAssert.IsTrue(File.Exists(ArtifactToString(sharedOutput)));
                    AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, allowMore: true);
                }
            }
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void DoubleWritesOnStaticallyDeclaredOutputsWithAllowList(bool onExclusiveOpaque)
        {
            EngineEnvironmentSettings.ApplyAllowListToDynamicOutputs.Value = true;

            string sharedOpaqueDir = Path.Combine(ObjectRoot, "sod");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);

            AbsolutePath nestedUnderSharedOpaqueDirPath = sharedOpaqueDirPath.Combine(Context.PathTable, "nested");
            FileArtifact sharedOutput = FileArtifact.CreateOutputFile(nestedUnderSharedOpaqueDirPath.Combine(Context.PathTable, "shared.out"));
            FileArtifact source = CreateSourceFile(ObjectRoot);
            WriteSourceFile(source);

            FileArtifact outputP = CreateOutputFileArtifact(sharedOpaqueDir);
            FileArtifact outputQ = CreateOutputFileArtifact(sharedOpaqueDir);

            Configuration.CacheableFileAccessAllowlist.Add(new global::BuildXL.Utilities.Configuration.Mutable.FileAccessAllowlistEntry
            {
                Name = nameof(DoubleWritesOnStaticallyDeclaredOutputsWithAllowList),
                ToolPath = new DiscriminatingUnion<FileArtifact, PathAtom>(TestProcessExecutable.Path.GetName(Context.PathTable)),
                PathFragment = ArtifactToString(sharedOutput),
            });

            ProcessBuilder pBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(source),
                Operation.WriteFile(outputP),
                Operation.WriteFile(sharedOutput, doNotInfer: onExclusiveOpaque)
            });
            if (onExclusiveOpaque)
            {
                pBuilder.AddOutputDirectory(nestedUnderSharedOpaqueDirPath, SealDirectoryKind.Opaque);
            }
            
            pBuilder.RewritePolicy |= RewritePolicy.DefaultSafe;
            ProcessWithOutputs p = SchedulePipBuilder(pBuilder);

            ProcessBuilder qBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputP),
                Operation.WriteFile(outputQ),
                Operation.WriteFile(sharedOutput, doNotInfer: true)
            });
            qBuilder.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            qBuilder.RewritePolicy |= RewritePolicy.DefaultSafe;
            ProcessWithOutputs q = SchedulePipBuilder(qBuilder);

            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            if (!onExclusiveOpaque)
            {
                AssertWarningEventLogged(LogEventId.AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs);
            }
        }

        private string ToString(AbsolutePath path) => path.ToString(Context.PathTable);
    }
}
