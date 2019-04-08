// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Scheduler;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Feature(Features.Symlink)]
    [TestClassIfSupported(requiresSymlinkPermission: true)]
    public class SymlinkTests : SchedulerIntegrationTestBase
    {
        public SymlinkTests(ITestOutputHelper output) : base(output)
        {
        }

        [Feature(Features.AbsentFileProbe)]
        [Feature(Features.SealedSourceDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingConsumingSymlinkToAbsentFile(bool sourceSealedDirectory)
        {
            // Create symlink from /src/symlinkFile to /src/absentFile
            FileArtifact absentFile = new FileArtifact(CreateUniqueSourcePath("absent"));
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath("sym"));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(absentFile), isTargetFile: true));

            // Make a separate existing file to point to later
            FileArtifact existingFile = CreateSourceFile();

            // Set up pip
            FileArtifact outFile = CreateOutputFileArtifact();
            Operation[] ops = new Operation[]
            {
                Operation.ReadFile(symlinkFile, doNotInfer:true),
                Operation.WriteFile(outFile, doNotInfer:true)
            };

            var builder = CreatePipBuilder(ops);
            builder.AddOutputFile(outFile.Path);
            builder.AddInputFile(symlinkFile);
            builder.AddInputFile(absentFile);
            builder.AddInputFile(existingFile);

            if (sourceSealedDirectory)
            {
                // Seal /src directory
                DirectoryArtifact srcDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);

                builder.AddInputDirectory(srcDir);
            }

            Process pip = SchedulePipBuilder(builder).Process;
            XAssert.IsTrue(PipGraphBuilder.AddProcess(pip));

            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Point /symlinkFile to /differentAbsentFile
            FileArtifact differentAbsentFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix));
            File.Delete(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(differentAbsentFile), isTargetFile: true));

            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Create previously absent /absentFile
            File.WriteAllText(ArtifactToString(absentFile), "absentFile");

            // Point /symlinkFile back to /absentFile
            File.Delete(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(absentFile), isTargetFile: true));
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Modify /symlinkFile target /absentFile
            File.WriteAllText(ArtifactToString(absentFile), "modify absentFile");
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Point /symlinkFile to /existingFile
            File.Delete(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(existingFile), isTargetFile: true));

            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);
        }

        /// <param name="readOnlyTargetDir">
        /// Specifies the read/write permissions of mount containing the symlink's target directory
        /// When true, /readonly/symlinkDir -> /readyonly/targetDir
        /// When false, /readonly/symlinkDir -> /readwrite/targetDir
        /// </param>
        [Feature(Features.Mount)]
        [Theory(Skip = "Bug #10959069")]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingSymlinkDirectoryEnumerationReadonlyMount_Bug1095069(bool readOnlyTargetDir)
        {
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath(SourceRootPrefix, readOnlyTargetDir ? ReadonlyRoot : ObjectRoot));
            DirectoryArtifact symlinkDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath(SourceRootPrefix, ReadonlyRoot));

            // Create symlink from /symlinkDir to /targetDir, start with absent /targetDir
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(targetDir), isTargetFile: false));

            // Make a separate existing directory to point to later
            DirectoryArtifact existingDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));

            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(symlinkDir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Point /symlinkDir to /differentAbsentDir
            DirectoryArtifact differentAbsentDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(SourceRootPrefix));
            Directory.Delete(ArtifactToString(symlinkDir));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(differentAbsentDir), isTargetFile: false));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Create previously absent /targetDir
            Directory.CreateDirectory(ArtifactToString(targetDir));

            // Point /symlinkDir back to /targetDir
            Directory.Delete(ArtifactToString(symlinkDir));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(targetDir), isTargetFile: false));
            RunScheduler().AssertCacheHit(pip.PipId);

            // Modify /symlinkDir target by creating /newFile in /targetDir
            FileArtifact newFile = CreateSourceFile(ArtifactToString(targetDir));
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Point /symlinkDir to /existingDir
            Directory.Delete(ArtifactToString(symlinkDir));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(existingDir), isTargetFile: false));
            RunScheduler().AssertCacheMiss(pip.PipId); // Bug 10959069, this line should pass, but currently fails
            RunScheduler().AssertCacheHit(pip.PipId);
        }

        /// <param name="readOnlyTargetDir">
        /// Specifies the read/write permissions of mount containing the symlink's target directory
        /// When true, /readwrite/symlinkDir -> /readonly/targetDir
        /// When false, /readwrite/symlinkDir -> /readwrite/targetDir
        /// </param>
        [Feature(Features.Mount)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingSymlinkDirectoryEnumerationReadWriteMount(bool readOnlyTargetDir)
        {
            // ReadWrite mounts are only aware of the virtual pip graph filesystem, not the actual filesystem

            // Create symlink from /symlinkDir to /targetDir
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(readOnlyTargetDir ? ReadonlyRoot : ObjectRoot));
            DirectoryArtifact symlinkDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniquePath("sym-dir-src", ObjectRoot));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(targetDir), isTargetFile: false));
            
            // Make a separate existing directory to point to latter
            DirectoryArtifact existingDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));

            FileArtifact outA = CreateOutputFileArtifact(ArtifactToString(targetDir));

            Process pipA;
            try
            {
                var pipABuilder = CreatePipBuilder(new Operation[]
                {
                    Operation.EnumerateDir(symlinkDir),
                    Operation.WriteFile(outA)
                });
                pipABuilder.AddInputFile(symlinkDir.Path);
                pipA = SchedulePipBuilder(pipABuilder).Process;
            }
            catch (BuildXLTestException)
            {
                // Only throw an exception if symlink's target directory is in non-writable mount
                XAssert.IsTrue(readOnlyTargetDir);

                // Error for pip declaring output /outA under non-writable mount
                AssertErrorEventLogged(EventId.InvalidOutputUnderNonWritableRoot);
                return;
            }

            FileArtifact outB = CreateOutputFileArtifact(ArtifactToString(targetDir));
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(outA),
                Operation.WriteFile(outB)
            }).Process;

            ScheduleRunResult miss = RunScheduler().AssertSuccess();
            miss.AssertCacheMiss(pipA.PipId);
            miss.AssertCacheMiss(pipB.PipId);

            ScheduleRunResult hit = RunScheduler().AssertSuccess();
            hit.AssertCacheHit(pipA.PipId);
            hit.AssertCacheHit(pipB.PipId);


            // Delete files in enumerated directory /symlinkDir
            File.Delete(ArtifactToString(outA));
            File.Delete(ArtifactToString(outB));

            var deleteFileHit = RunScheduler().AssertSuccess();
            deleteFileHit.AssertCacheHit(pipA.PipId);
            deleteFileHit.AssertCacheHit(pipB.PipId);

            // Double check that cache replay worked  
            XAssert.IsTrue(File.Exists(ArtifactToString(outA)));
            XAssert.IsTrue(File.Exists(ArtifactToString(outB)));

            // Point /symlinkDir to /existingDir
            Directory.Delete(ArtifactToString(symlinkDir));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkDir), ArtifactToString(existingDir), isTargetFile: false));
            RunScheduler().AssertCacheMiss(pipA.PipId);
        }

        [Fact]
        public void ValidateCachingProducingSymlinkFile()
        {
            FileArtifact file = new FileArtifact(CreateSourceFile());
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();
            string symlinkFilePath = ArtifactToString(symlinkFile);

            // Process creates file symlink
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile, file, Operation.SymbolicLinkFlag.FILE)
            }).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);

            RunScheduler().AssertCacheHit(pip.PipId);

            // save symlink target to compare to later
            string symlinkTarget = GetReparsePointTarget(symlinkFilePath);

            // Save output to compare to later
            string output = File.ReadAllText(ArtifactToString(file));
            RunScheduler().AssertCacheHit(pip.PipId);

            File.Delete(symlinkFilePath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the symlinkFile was replayed as a symlink
            XAssert.IsTrue(File.Exists(symlinkFilePath));
            XAssert.IsTrue(IsSymlink(symlinkFile));

            // Check that symlink still points to the same target (and also that the target's content was not changed)
            string cacheOutput = File.ReadAllText(symlinkFilePath);
            XAssert.AreEqual(output, cacheOutput);

            string replayedSymlinkTarget = GetReparsePointTarget(symlinkFilePath);
            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);
        }

        [Fact]
        public void ValidateCachingProducingRelativeSymlinkFile()
        {
            string requestedTarget = R("..", "..", "NonExistent", "SomeFile");
            FileArtifact symlinkFile = CreateOutputFileArtifact(prefix: "sym-out");
            string symlinkFilePath = ArtifactToString(symlinkFile);

            // Process creates file symlink
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(prefix: "regular-out")),
                Operation.CreateSymlink(symlinkFile, requestedTarget, Operation.SymbolicLinkFlag.FILE)
            }).Process;
            RunScheduler().AssertSuccess();

            // save symlink target to compare to later
            string symlinkTarget = GetReparsePointTarget(symlinkFilePath);

            // sanity check that the test created a proper symlink
            XAssert.AreEqual(requestedTarget, symlinkTarget);

            // delete the symlink and replay it from cache
            File.Delete(symlinkFilePath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the symlinkFile was replayed as a symlink
            XAssert.IsTrue(File.Exists(symlinkFilePath));
            XAssert.IsTrue(IsSymlink(symlinkFile));

            // Check that the target path was replayed as a relative path
            string replayedSymlinkTarget = GetReparsePointTarget(symlinkFilePath);
            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);
        }

        [Fact]
        public void ValidateCachingProcessProducingChainOfSymlinks()
        {
            FileArtifact targetFile = new FileArtifact(CreateSourceFile());

            // Save content to compare to later
            string targetContent = File.ReadAllText(ArtifactToString(targetFile)); // src_0

            FileArtifact symlinkA = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_1
            FileArtifact symlinkB = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_2
            FileArtifact symlinkC = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_3

            // pip produces chain of symlinks /symlinkC -> /symlinkB -> symlinkA -> targetFile
            var builder = CreatePipBuilder(new Operation[]
                {
                    Operation.CreateSymlink(symlinkA, targetFile, Operation.SymbolicLinkFlag.FILE, doNotInfer: true),
                    Operation.CreateSymlink(symlinkB, symlinkA, Operation.SymbolicLinkFlag.FILE, doNotInfer: true),
                    Operation.CreateSymlink(symlinkC, symlinkB, Operation.SymbolicLinkFlag.FILE, doNotInfer: true)
                });
            builder.AddInputFile(targetFile);
            builder.AddOutputFile(symlinkA.Path);
            builder.AddOutputFile(symlinkB.Path);
            builder.AddOutputFile(symlinkC.Path);
            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();

            string symlinkContent = File.ReadAllText(ArtifactToString(symlinkC));

            // Check symlink chain creation worked
            XAssert.AreEqual(targetContent, symlinkContent);

            // Delete /symlinkB from middle of chain
            // Before second run directory state: obj_1 (symlink), obj_3 (symlink), src_0
            FileUtilities.DeleteFile(ArtifactToString(symlinkB), true);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Validate each of the files are still there and contain the correct content
            FileArtifact[] files = new FileArtifact[] { targetFile, symlinkA, symlinkB, symlinkC };
            foreach (var file in files)
            {
                XAssert.IsTrue(File.Exists(ArtifactToString(file)));
                XAssert.AreEqual(targetContent, File.ReadAllText(ArtifactToString(file)));
            }

            // /targetFile, /symlinkA, and /symlinkC should be in their original state
            XAssert.IsFalse(IsSymlink(targetFile));
            XAssert.IsTrue(IsSymlink(symlinkA));
            XAssert.IsTrue(IsSymlink(symlinkC));

            // /symlinkB is replayed as a symlink
            XAssert.IsTrue(IsSymlink(symlinkB));
        }

        [Feature(Features.RewrittenFile)]
        [Fact]
        public void ValidateCachingProcessRewritingFileViaSymlink()
        {
            // pipInit creates a /targetFile
            // pipA produces /symlinkFile to /targetFile
            // pipB writes (appends) content to /simlinkFile => content should be added to /targetFile
            FileArtifact targetFile = CreateOutputFileArtifact();
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix)).CreateNextWrittenVersion();

            // Save target file's content to compare to later
            string targetContent = System.Guid.NewGuid().ToString();

            Process pipInit = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(targetFile, targetContent)
            }).Process;

            // Process creates file symlink
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile, targetFile, Operation.SymbolicLinkFlag.FILE)
            }).Process;

            // pipB consumes and rewrites /symlinkFile
            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(symlinkFile, doNotInfer: true)
            });
            builder.AddRewrittenFileInPlace(symlinkFile);
            // we require that users add the symlink target (the physical file the symlink points to) to pip outputs
            builder.AddRewrittenFileInPlace(targetFile);
            Process pipB = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();

            ValidateFilesExistProcessWithSymlinkOutput(targetFile, symlinkFile);

            // Check the rewrite appended new content to targetContent
            string rewrittenContent = File.ReadAllText(ArtifactToString(targetFile));
            XAssert.IsTrue(rewrittenContent.StartsWith(targetContent));
            XAssert.IsTrue(rewrittenContent.Length > targetContent.Length);

            // Delete /symlinkFile
            File.Delete(ArtifactToString(symlinkFile));
            ScheduleRunResult hit = RunScheduler().AssertSuccess();
            hit.AssertCacheHit(pipInit.PipId);
            hit.AssertCacheHit(pipA.PipId);
            hit.AssertCacheHit(pipB.PipId);

            // Make sure end result is the same
            string replayedContent = File.ReadAllText(ArtifactToString(targetFile));
            XAssert.AreEqual(replayedContent, rewrittenContent);
        }

        [Fact]
        public void ValidateCachingProcessRewritingSymlink()
        {
            // pipA creates /symlinkFile that points to /targetA
            // pipB rewrites /symlinkFile so it now points to /targetB
            FileArtifact targetA = CreateSourceFileWithPrefix(prefix: "targetA");
            FileArtifact targetB = CreateSourceFileWithPrefix(prefix: "targetB");

            string targetBContent = File.ReadAllText(ArtifactToString(targetB));

            FileArtifact symlinkFile = CreateOutputFileArtifact();

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
               Operation.CreateSymlink(symlinkFile, targetA, Operation.SymbolicLinkFlag.FILE)
            });
            var pipA = SchedulePipBuilder(pipBuilderA).Process;

            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.DeleteFile(symlinkFile, doNotInfer: true),
                Operation.CreateSymlink(symlinkFile, targetB, Operation.SymbolicLinkFlag.FILE, doNotInfer: true)
            });
            pipBuilderB.AddRewrittenFileInPlace(symlinkFile);
            var pipB = SchedulePipBuilder(pipBuilderB).Process;

            RunScheduler().AssertSuccess();
            string symlinkPath = ArtifactToString(symlinkFile);
            var symlinkTarget = GetReparsePointTarget(symlinkPath);
            XAssert.AreEqual(ArtifactToString(targetB), GetReparsePointTarget(symlinkPath));

            // check that symlink properly points to targetB
            string symlinkTargetContent = File.ReadAllText(symlinkPath);
            XAssert.AreEqual(targetBContent, symlinkTargetContent);

            File.Delete(ArtifactToString(symlinkFile));

            var result = RunScheduler().AssertSuccess();
            result.AssertCacheHit(pipA.PipId);
            result.AssertCacheHit(pipB.PipId);

            // check that symlinkFile is a symlink and that it properly points to targetB
            XAssert.IsTrue(IsSymlink(symlinkFile));
            XAssert.AreEqual(ArtifactToString(targetB), GetReparsePointTarget(symlinkPath));
            var replayedSymlinkTarget = GetReparsePointTarget(ArtifactToString(symlinkFile));
            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);
            symlinkTargetContent = File.ReadAllText(symlinkPath);
            XAssert.AreEqual(targetBContent, symlinkTargetContent);
        }

        [Feature(Features.OpaqueDirectory)]
        [Fact]
        public void ValidateBehaviorProcessProducingSymlinkInOpaqueDirectory()
        {
            string opaqueDir = "opaqueDir";
            AbsolutePath opaqueDirPath = CreateUniquePath(opaqueDir, ObjectRoot);
            Directory.CreateDirectory(opaqueDirPath.ToString(Context.PathTable));

            // /opaqueDir/symlinkFile -> /src/targetFile
            FileArtifact symlinkFile = new FileArtifact(CreateUniquePath("symlink", opaqueDirPath.ToString(Context.PathTable))).CreateNextWrittenVersion();
            FileArtifact targetFile = CreateSourceFile();

            // pipA declares output of /opaqueDir and creates /opaqueDir/symlinkFile
            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile, targetFile, Operation.SymbolicLinkFlag.FILE, doNotInfer: true)
            });

            // /symlinkFile does not need to be declared as an output since it is produced within the declared, produced opaque directory
            pipBuilderA.AddOutputDirectory(opaqueDirPath);
            var paoA = SchedulePipBuilder(pipBuilderA);
            Process pipA = paoA.Process;

            // pipB declares dependency on /opaqueDir and consumes /opaqueDir/symlinkFile
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(symlinkFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            // /symlinkFile does not need to be declared as an input since it is within the declared, consumed opaque directory
            pipBuilderB.AddInputDirectory(paoA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));
            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            ScheduleRunResult fail = RunScheduler().AssertFailure();

            // pipA successfully creates the symlink
            XAssert.AreEqual(PipResultStatus.Succeeded, fail.PipResults[pipA.PipId]);
            ValidateFilesExistProcessWithSymlinkOutput(targetFile, symlinkFile);

            // pipB fails on consuming /symlinkFile since it does not declare /targetFile as a dependency
            XAssert.AreEqual(PipResultStatus.Failed, fail.PipResults[pipB.PipId]);
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, ArtifactToString(targetFile));
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void AllowProducingSymlinkToExistingFile()
        {
            FileArtifact file = new FileArtifact(CreateSourceFile());
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath("sym")).CreateNextWrittenVersion();

            // Process creates file symlink
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(prefix: "out")),
                Operation.CreateSymlink(symlinkFile, file, Operation.SymbolicLinkFlag.FILE)
            }).Process;

            RunScheduler().AssertSuccess();

            ValidateFilesExistProcessWithSymlinkOutput(file, symlinkFile);
        }

        [Feature(Features.SealedSourceDirectory)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllowProcessCopyingSymlink(bool sourceSealedDirectory)
        {
            // Create symlink from /symlinkFile to /targetFile
            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(targetFile), isTargetFile: true));

            // Get content of /targetFile to compare later
            string targetContent = File.ReadAllText(ArtifactToString(targetFile));

            // Set up pip
            FileArtifact copiedFile = CreateOutputFileArtifact();

            // Process copies a symlink
            Operation[] ops = new Operation[]
            {
                Operation.CopyFile(symlinkFile, copiedFile, doNotInfer:true)
            };
            var builder = CreatePipBuilder(ops);
            builder.AddOutputFile(copiedFile.Path, FileExistence.Required);
            builder.AddInputFile(symlinkFile);
            builder.AddInputFile(targetFile);
            
            if (sourceSealedDirectory)
            {
                // Seal /src directory
                DirectoryArtifact srcDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);
                builder.AddInputDirectory(srcDir);
            }
            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();

            string copiedContent = File.ReadAllText(ArtifactToString(copiedFile));
            XAssert.AreEqual(targetContent, copiedContent);
        }

        /// <summary>
        /// Producing symlinks to directories is never allowed and should always cause failures.
        /// </summary> 
        [Fact]
        public void AlwaysFailOnProducingSymlinkDirectory()
        {
            // Start with symlink to absent directory
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(ObjectRootPrefix));
            DirectoryArtifact symlinkDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(ObjectRootPrefix));

            // Process creates directory symlinks
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateSymlink(symlinkDir, dir, Operation.SymbolicLinkFlag.FILE)
            }).Process;

            FailOnProducingSymlink();

            // Create previously absent driectory 
            Directory.CreateDirectory(ArtifactToString(dir));
            FailOnProducingSymlink();
        }

        /// <summary>
        /// Processes can produce symlinks to absent files because we cache the target paths of the symlink.
        /// </summary>
        [Fact]
        public void AllowProducingSymlinkToAbsentFile()
        {
            FileArtifact absentFile = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix));
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();

            // Process creates file symlink
            Process pip = CreateAndSchedulePipBuilder(new Operation[] 
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateSymlink(symlinkFile, absentFile, Operation.SymbolicLinkFlag.FILE)
            }).Process;

            // Symlinks to absent files cause caching errors
            RunScheduler().AssertSuccess();

            // Validate symlink was created
            XAssert.IsTrue(File.Exists(ArtifactToString(symlinkFile)));
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void ValidateCachingUnsafeIgnoreReparsePoint()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreReparsePoints = true;

            // Create symlink from /symlinkSourceFile to /targetSourceFile
            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix + "-sym"));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(targetFile), isTargetFile: true));

            FileArtifact copiedFile = CreateOutputFileArtifact();

            // Process declares dependency on /symlinkSourceFile without declaring dependency on /targetSourceFile
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CopyFile(symlinkFile, copiedFile),
                Operation.WriteFile(CreateOutputFileArtifact()),
            }).Process;
            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            string checkpoint1 = File.ReadAllText(ArtifactToString(targetFile));
            
            // Modify /targetSourceFile
            File.WriteAllText(ArtifactToString(targetFile), "target source file");

            // Ignore changes to undeclared target source file
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(checkpoint1, File.ReadAllText(ArtifactToString(copiedFile)));

            // Delete /targetSourceFile
            File.Delete(ArtifactToString(targetFile));
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(checkpoint1, File.ReadAllText(ArtifactToString(copiedFile)));

            // Redirect /symlinkFile to /newTargetFile
            FileArtifact newTargetFile = CreateSourceFile();
            File.Delete(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(newTargetFile), isTargetFile: true));
            RunScheduler().AssertCacheMiss(pip.PipId);
            XAssert.AreNotEqual(checkpoint1, File.ReadAllText(ArtifactToString(copiedFile)));
        }

        /// <summary>
        /// Validates that Detours does not follow symlinks when IgnoreReparsePoints and IgnoreNonCreateFileReparsePoints options are enabled
        /// </summary>
        /// <param name="ignoreOnlyNonCreateFileReparsePoints">
        /// When true, enables IgnoreNonCreateFileReparse which ignores symlinks for CreateFile and NtCreateFile/OpenFile APIs
        /// When false, enables IgnoreReparsePoints which ignores symlinks for all file management APIs
        /// </param>
        /// <param name="useCreateFileAPI">
        /// When true, a pip is created that passes a symlink into a CreateFile or NtCreateFile/OpenFile API
        /// When false, a pip is created that passes a symlink into a filemanagement API other than CreateFile or NtCreateFile/OpenFile
        /// </param>
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        [Trait("Category", "WindowsOSOnly")]
        public void ValidateCachingUnsafeIgnoreReparsePointReadFile(bool ignoreOnlyNonCreateFileReparsePoints, bool useCreateFileAPI)
        {
            // Allows pips to input and output symlink files without declaring the corresponding target files
            if (ignoreOnlyNonCreateFileReparsePoints)
            {
                Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreNonCreateFileReparsePoints = true;
            }
            else
            {
                Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreReparsePoints = true;
            }

            // Create symlink from /symlinkSourceFile to /targetSourceFile
            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(targetFile), isTargetFile: true));

            FileArtifact copiedFile = CreateOutputFileArtifact();

            // Process declares dependency on /symlinkSourceFile without declaring dependency on /targetSourceFile
            FileArtifact outFile = CreateOutputFileArtifact();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                // ReadFile will calls CreateFile to obtain the file handle
                // CopyFile will not use any CreateFile APIs
                useCreateFileAPI ? Operation.ReadFile(symlinkFile) : Operation.CopyFile(symlinkFile, copiedFile),
                Operation.WriteFile(outFile),
            }).Process;
            
            if (ignoreOnlyNonCreateFileReparsePoints && useCreateFileAPI)
            {
                // Detours is only ignoring symlinks for APIs other than CreateFile and NtCreateFile/OpenFile
                // Since ReadFile calls CreateFile, expect a disallowed file access on undeclared underlying /targetFile
                RunScheduler().AssertFailure();
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
                AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
            else
            {
                RunScheduler().AssertSuccess();
            }
        }

        [Fact]
        [Trait("Category", "StoreNoOutputsToCacheTests")]
        public void VerifyOutputBeingReplacedWithSymlink()
        {
            Configuration.Schedule.StoreOutputsToCache = false;

            FileArtifact originalLocation = CreateOutputFileArtifact();
            FileArtifact newLocation = CreateOutputFileArtifact();
            string fileContent = System.Guid.NewGuid().ToString();

            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(originalLocation, fileContent)
            }).Process;

            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(originalLocation),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertSuccess();

            // move the file to a new place
            File.Move(ArtifactToString(originalLocation), ArtifactToString(newLocation));
            // create a symlink from the original place to the new one
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(originalLocation), ArtifactToString(newLocation), isTargetFile: true));

            // run again --- BuildXL should detect that the file was replaced with a symlink to that file and should "undo" this
            RunScheduler().AssertSuccess();
            XAssert.IsFalse(IsSymlink(originalLocation));
            XAssert.AreEqual(fileContent, File.ReadAllText(ArtifactToString(originalLocation)));
        }

        [Fact]
        public void CopyOutputSymlinkFailOnInvalidChain()
        {
            Configuration.Schedule.AllowCopySymlink = true;

            // Symlink chain:
            // symlinkFile1 -> symlinkFile2 -> targetFile

            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();
            FileArtifact symlinkFile2 = CreateOutputFileArtifact();

            // the chain contains an element (other than the head) that is created during the build -> invalid chain
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile1, symlinkFile2, Operation.SymbolicLinkFlag.FILE),
                Operation.CreateSymlink(symlinkFile2, targetFile, Operation.SymbolicLinkFlag.FILE)
            });

            // CopyFilePip : copy symlinkFile1 to output
            FileArtifact output = CopyFile(symlinkFile1, CreateOutputFileArtifact());

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipCopyFileFailed);
        }

        [Fact]
        public void CopyOutputSymlinkFailOnInvalidChainMissingElement()
        {
            Configuration.Schedule.AllowCopySymlink = true;

            // Symlink chain:
            // symlinkFile1 -> symlinkFile2 -> targetFile (missing)

            FileArtifact targetFile = CreateOutputFileArtifact();
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();
            FileArtifact symlinkFile2 = CreateOutputFileArtifact();

            // an element of the symlink chain does not exist -> invalid chain
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile2), ArtifactToString(targetFile), isTargetFile: true));
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile1, symlinkFile2, Operation.SymbolicLinkFlag.FILE),
            });

            // CopyFilePip : copy symlinkFile1 to output
            FileArtifact output = CopyFile(symlinkFile1, CreateOutputFileArtifact());

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipCopyFileFailed);
        }

        [Feature(Features.OpaqueDirectory)]
        [Fact]
        public void CopyOutputSymlinkFailOnInvalidChainElementInOpaqueDirectory()
        {
            Configuration.Schedule.AllowCopySymlink = true;

            // Symlink chain:
            // obj/symlinkFile1 -> obj/opaqueDir/opaqueSubDir/symlinkFile2 -> src/targetFile

            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();

            string opaqueDir = "opaqueDir";
            AbsolutePath opaqueDirPath = CreateUniquePath(opaqueDir, ObjectRoot);
            Directory.CreateDirectory(opaqueDirPath.ToString(Context.PathTable));

            string opaqueSubDir = "opaqueSubDir";
            AbsolutePath opaqueSubDirPath = CreateUniquePath(opaqueSubDir, opaqueDirPath.ToString(Context.PathTable));
            Directory.CreateDirectory(opaqueSubDirPath.ToString(Context.PathTable));

            FileArtifact symlinkFile2 = new FileArtifact(CreateUniquePath("symlink", opaqueSubDirPath.ToString(Context.PathTable))).CreateNextWrittenVersion();
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile2), ArtifactToString(targetFile), isTargetFile: true));

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
               Operation.CreateSymlink(symlinkFile1, symlinkFile2, Operation.SymbolicLinkFlag.FILE)
            });

            // only 'opaqueDir' is declared as an opaque directory
            pipBuilderA.AddOutputDirectory(opaqueDirPath);
            var pipA = SchedulePipBuilder(pipBuilderA);

            // CopyFilePip : copy symlinkFile1 to output
            FileArtifact output = CopyFile(symlinkFile1, CreateOutputFileArtifact());

            // should fail because an element of a symlink chain (symlinkFile2) is inside of an opaque directory
            var result = RunScheduler().AssertFailure();

            // check that init pip finished without any errors
            XAssert.AreEqual(PipResultStatus.Succeeded, result.PipResults[pipA.Process.PipId]);

            AssertErrorEventLogged(EventId.PipCopyFileFailed);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CopyOutputSymlink(bool storeOutputsToCache)
        {
            Configuration.Schedule.AllowCopySymlink = true;
            Configuration.Schedule.StoreOutputsToCache = storeOutputsToCache;

            // Symlink chain:
            // symlinkFile1 -> symlinkFile2 -> targetFile

            FileArtifact targetFile = CreateSourceFile();
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();
            FileArtifact symlinkFile2 = CreateOutputFileArtifact();

            // only the head of the symlink chain is created during the build -> valid chain
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile2), ArtifactToString(targetFile), isTargetFile: true));
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile1, symlinkFile2, Operation.SymbolicLinkFlag.FILE),
            });

            // CopyFilePip : copy symlinkFile1 to output
            FileArtifact output = CopyFile(symlinkFile1, CreateOutputFileArtifact());

            RunScheduler().AssertSuccess();
            XAssert.IsTrue(File.Exists(ArtifactToString(output)));
            XAssert.IsFalse(IsSymlink(output));
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(targetFile)), File.ReadAllText(ArtifactToString(output)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AccessFileSymlinkThroughDirectorySymlinkOrJunction(bool useJunction)
        {
            // File and directory layout:
            //    Enlist
            //    |
            //    +---Intermediate
            //    |   |
            //    |   +---Current
            //    |   |       linkFile ==> ..\..\Target\targetFile (later ..\..\Target\targetFileX), or
            //    |   |                ==> ..\Target\targetFile (later ..\Target\targetFileX) -- if use junction
            //    |   |
            //    |   \---CurrentY
            //    |           linkFile ==> ..\..\Target\targetFileY, or
            //    |                        ..\Target\targetFileY -- if use junction
            //    |
            //    +---Source ==> Intermediate\Current (directory symlink or junction)
            //    |
            //    \---Target
            //            targetFile
            //            targetFileX
            //            targetFileY

            if (useJunction && OperatingSystemHelper.IsUnixOS)
            {
                // Mac/Unix does not support junctions.
                return;
            }

            // If junction, then the prefix should be '..\' instead of '..\..\'
            string relativePrefix = useJunction ? ".." : Path.Combine("..", "..");

            PathTable pathTable = Context.PathTable;
            StringTable stringTable = pathTable.StringTable;

            FileArtifact dummyOutput = CreateOutputFileArtifact();

            // Create concrete file /Enlist/Target/targetFile.
            AbsolutePath enlistDirectory = CreateUniqueDirectory(prefix: "Enlist");
            AbsolutePath targetDirectory = CreateUniqueDirectory(root: enlistDirectory, prefix: "Target");

            FileArtifact targetFile = CreateSourceFile(root: targetDirectory, prefix: "targetFile");
            SealDirectory sealedTargetDirectory = CreateAndScheduleSealDirectory(targetDirectory, SealDirectoryKind.SourceAllDirectories);

            // Get relative target ../../Target/targetFile (or ../Target/targetFile).
            PathAtom targetFileName = targetFile.Path.GetName(pathTable);
            PathAtom targetDirectoryName = targetFile.Path.GetParent(pathTable).GetName(pathTable);
            string relativeTarget = Path.Combine(relativePrefix, targetDirectoryName.ToString(stringTable), targetFileName.ToString(stringTable));

            // Create file symlink /Enlist/Intermediate/Current/linkFile --> ../../Target/targetFile.
            AbsolutePath intermediateDirectory = CreateUniqueDirectory(root: enlistDirectory, prefix: "Intermediate");
            AbsolutePath currentIntermediateDirectory = CreateUniqueDirectory(root: intermediateDirectory, prefix: "Current");
            AbsolutePath linkFile = currentIntermediateDirectory.Combine(pathTable, "linkFile");

            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(linkFile.ToString(pathTable), relativeTarget, isTargetFile: true));

            // Create directory symlink /Enlist/Source --> Intermediate/Current
            AbsolutePath sourceDirectory = CreateUniqueDirectory(root: enlistDirectory, prefix: "Source");

            if (useJunction)
            {
                FileUtilities.CreateJunction(sourceDirectory.ToString(pathTable), currentIntermediateDirectory.ToString(pathTable));
            }
            else
            {
                FileUtilities.DeleteDirectoryContents(sourceDirectory.ToString(pathTable), deleteRootDirectory: true);
                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceDirectory.ToString(pathTable), currentIntermediateDirectory.ToString(pathTable), isTargetFile: false));
            }

            FileArtifact sourceFile = FileArtifact.CreateSourceFile(sourceDirectory.Combine(pathTable, "linkFile"));

            ProcessBuilder builder = CreatePipBuilder(new[]
            {
                // Read /Enlist/Source/linkFile.
                Operation.ReadFile(sourceFile, doNotInfer: true),

                // Write dummy.
                Operation.WriteFile(dummyOutput, content: nameof(AccessFileSymlinkThroughDirectorySymlinkOrJunction))
            });

            builder.AddInputDirectory(sealedTargetDirectory.Directory);

            if (OperatingSystemHelper.IsUnixOS)
            {
                // Mac's implementation may access /Enlist/Intermediate/Current/linkFile. Windows' implementation
                // doesn't include that path because it is a different form of /Enlist/Source/linkFile that is
                // accessed by the pip. Note that Windows' implementation does not report or track all possible
                // incarnations of path because it will be too expensive to do so, i.e., need to call repeatedly
                // DeviceIoControl on path prefixes until fixed point.
                //
                // TODO: Reconcile this difference between Mac and Windows.
                builder.AddInputFile(FileArtifact.CreateSourceFile(linkFile));

                // Mac may also access /Enlist/Source as a file, and specifying /Enlist/Source as an input directory
                // seems to be not sufficient. However, if we additionally specify /Enlist/Source as both input directory
                // and input file, the pip graph validation will fail.
                // Instead we specify /Enlist/Source/linkFile and /Enlist/Source as input files for Mac.
                builder.AddInputFile(FileArtifact.CreateSourceFile(sourceDirectory));
                builder.AddInputFile(sourceFile);
            }
            else
            {
                SealDirectory sealedSourceDirectory = CreateAndScheduleSealDirectory(sourceDirectory, SealDirectoryKind.SourceAllDirectories);
                builder.AddInputDirectory(sealedSourceDirectory.Directory);
            }

            ProcessWithOutputs processWithOutputs = SchedulePipBuilder(builder);

            if (TryGetSubstSourceAndTarget(out string substSource, out string substTarget))
            {
                // Directory translation is needed because the pip to execute, on Windows, 
                // will call undetoured <code>GetFinalPathNameByHandle</code> which in turn will return
                // the real path instead of the subst path.
                DirectoryTranslator = new DirectoryTranslator();
                DirectoryTranslator.AddTranslation(substSource, substTarget);
            }

            RunScheduler().AssertSuccess().AssertCacheMiss(processWithOutputs.Process.PipId);

            // Modify /Enlist/Target/targetFile should result in cache miss.
            File.WriteAllText(ArtifactToString(targetFile), System.Guid.NewGuid().ToString());

            RunScheduler().AssertSuccess().AssertCacheMiss(processWithOutputs.Process.PipId);

            // Re-route /Enlist/Intermediate/Current/linkFile ==> ../../Target/targetFileX should result in cache miss.
            FileUtilities.DeleteFile(linkFile.ToString(pathTable));
            FileArtifact targetFileX = CreateSourceFile(root: targetDirectory, prefix: "targetFileX");
            PathAtom targetFileNameX = targetFileX.Path.GetName(pathTable);
            relativeTarget = Path.Combine(relativePrefix, targetDirectoryName.ToString(stringTable), targetFileNameX.ToString(stringTable));

            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(linkFile.ToString(pathTable), relativeTarget, isTargetFile: true));

            RunScheduler().AssertSuccess().AssertCacheMiss(processWithOutputs.Process.PipId);

            if (!OperatingSystemHelper.IsUnixOS)
            {
                // Re-route /Enlist/Source ==> Intermediate/CurrentY should result in cache miss.
                // Create /Enlist/Target/targetFileY and /Enlist/Intermediate/CurrentY/linkFile
                FileArtifact targetFileY = CreateSourceFile(root: targetDirectory, prefix: "targetFileY");
                PathAtom targetFileNameY = targetFileY.Path.GetName(pathTable);
                relativeTarget = Path.Combine(relativePrefix, targetDirectoryName.ToString(stringTable), targetFileNameY.ToString(stringTable));

                AbsolutePath currentIntermediateDirectoryY = CreateUniqueDirectory(root: intermediateDirectory, prefix: "CurrentY");
                AbsolutePath linkFileY = currentIntermediateDirectoryY.Combine(pathTable, "linkFile");

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(linkFileY.ToString(pathTable), relativeTarget, isTargetFile: true));
                FileUtilities.DeleteDirectoryContents(sourceDirectory.ToString(pathTable), deleteRootDirectory: true);

                XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(sourceDirectory.ToString(pathTable), currentIntermediateDirectoryY.ToString(pathTable), isTargetFile: false));

                RunScheduler().AssertSuccess().AssertCacheMiss(processWithOutputs.Process.PipId);

                // Mac test requires /Enlist/Intermediate/CurrentY/linkFile to be specified as an input file. Such an addition essentially
                // modifies the pip, and thus obviously the pip will be a cache miss.
            }
        }

        /// <summary>
        /// Pips delete their outputs before running. When a symlink is declared as output,
        /// we should delete the symlink itself and not the underlying target file.
        /// This is called post-running the pip to validate the state of the symlink and target file. 
        /// </summary>
        /// <param name="targetFile">underlying target file symlinkFile points to</param>
        /// <param name="symlinkFile">symlinkFile that is declared as output of pip</param>
        protected void ValidateFilesExistProcessWithSymlinkOutput(FileArtifact targetFile, FileArtifact symlinkFile)
        {
            // Check that /targetFile still exists as a file, not a symlink
            XAssert.IsTrue(File.Exists(ArtifactToString(targetFile)));
            XAssert.IsFalse(new FileInfo(ArtifactToString(targetFile)).Attributes.HasFlag(FileAttributes.ReparsePoint));

            // Sanity check that symlink path still exists. It's possible for this to be replaced with a copied file or remain a symlink.
            XAssert.IsTrue(File.Exists(ArtifactToString(symlinkFile)));
            XAssert.IsTrue(new FileInfo(ArtifactToString(symlinkFile)).Attributes.HasFlag(FileAttributes.ReparsePoint));
        }

        public void FailOnProducingSymlink()
        {
            // Producing symlinks is not allowed by default, so failure
            RunScheduler().AssertFailure();

            // Block creating symlink
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, allowMore: true);
            AssertErrorEventLogged(EventId.FileMonitoringError);

            // Test process fails when an operation cannot be completed
            AssertErrorEventLogged(EventId.PipProcessError);

            // Expected symlink as output
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput, allowMore: true);
        }

        protected bool IsSymlink(FileArtifact file) => IsSymlink(ArtifactToString(file));

        internal static bool IsSymlink(string file) => new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint);

        private string GetReparsePointTarget(string filePath)
        {
            var result = FileUtilities.TryCreateOrOpenFile(
                filePath,
                FileDesiredAccess.GenericRead,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out var handle);

            XAssert.IsTrue(result.Succeeded, $"Failed to create a handle to file '{filePath}'");

            using (handle)
            {
                var possibleTarget = FileUtilities.TryGetReparsePointTarget(handle, filePath);
                XAssert.IsTrue(possibleTarget.Succeeded);

                return possibleTarget.Result;
            }
        }
    }
}
