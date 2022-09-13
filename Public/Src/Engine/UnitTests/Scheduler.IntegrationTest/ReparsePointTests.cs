// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Feature(Features.Symlink)]
    [TestClassIfSupported(requiresSymlinkPermission: true)]
    [Trait("Category", "ReparsePointTests")]
    public class ReparsePointTests : SchedulerIntegrationTestBase
    {
        public ReparsePointTests(ITestOutputHelper output) : base(output)
        {
            // Enable full symbolic link resolving for testing
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;
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
            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(differentAbsentFile), isTargetFile: true));

            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Create previously absent /absentFile
            File.WriteAllText(ArtifactToString(absentFile), "absentFile");

            // Point /symlinkFile back to /absentFile
            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(absentFile), isTargetFile: true));
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Modify /symlinkFile target /absentFile
            File.WriteAllText(ArtifactToString(absentFile), "modify absentFile");
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Point /symlinkFile to /existingFile
            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));
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
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingSymlinkDirectoryEnumerationReadonlyMount(bool readOnlyTargetDir)
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
            RunScheduler().AssertCacheMiss(pip.PipId);
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
                AssertErrorEventLogged(global::BuildXL.Pips.Tracing.LogEventId.InvalidOutputUnderNonWritableRoot);
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
            FileUtilities.DeleteFile(ArtifactToString(outA));
            FileUtilities.DeleteFile(ArtifactToString(outB));

            var deleteFileHit = RunScheduler().AssertSuccess();
            deleteFileHit.AssertCacheHit(pipA.PipId);
            deleteFileHit.AssertCacheHit(pipB.PipId);

            // Double check that cache replay worked
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(outA)));
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(outB)));

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

            FileUtilities.DeleteFile(symlinkFilePath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the symlinkFile was replayed as a symlink
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(symlinkFilePath));
            XAssert.IsTrue(IsFileSymlink(symlinkFile));

            // Check that symlink still points to the same target (and also that the target's content was not changed)
            string cacheOutput = File.ReadAllText(symlinkFilePath);
            XAssert.AreEqual(output, cacheOutput);

            string replayedSymlinkTarget = GetReparsePointTarget(symlinkFilePath);
            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);
        }

        [Fact]
        public void ValidateCachingProducingDirectorySymlink()
        {
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            FileArtifact directorySymlink = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateSymlink(directorySymlink, targetDir, Operation.SymbolicLinkFlag.DIRECTORY)
            });

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // save symlink target to compare to later
            string directorySymlinkPath = ArtifactToString(directorySymlink);
            string directorySymlinkTarget = GetReparsePointTarget(directorySymlinkPath);

            FileUtilities.DeleteFile(directorySymlinkPath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the directory symlink was replayed as a proper directory symlink
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(directorySymlinkPath));
            XAssert.IsTrue(IsDirectorySymlink(directorySymlink));

            // Check that symlink still points to the same target and the target is a directory
            string replayedDirectorySymlinkTarget = GetReparsePointTarget(directorySymlinkPath);
            XAssert.AreEqual(directorySymlinkTarget, replayedDirectorySymlinkTarget);

            var existenceCheck = FileUtilities.TryProbePathExistence(directorySymlinkPath, followSymlink: true);
            XAssert.IsTrue(existenceCheck.Succeeded);
            XAssert.IsTrue(existenceCheck.Result == PathExistence.ExistsAsDirectory);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ValidateCachingProducingJunctions()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = true;
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            FileArtifact junction = new FileArtifact(CreateUniqueSourcePath("junction")).CreateNextWrittenVersion();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateJunction(junction, targetDir)
            });

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertCacheHit(pip.PipId);

            // save junction target to compare to later
            string junctionPath = ArtifactToString(junction);
            string junctionPathTarget = GetReparsePointTarget(junctionPath);

            Directory.Delete(junctionPath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the junction was replayed
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(junctionPath));
            XAssert.IsTrue(IsJunction(junction));

            // Check that junction still points to the same target
            string replayedJunctionTarget = GetReparsePointTarget(junctionPath);
            XAssert.AreEqual(junctionPathTarget, replayedJunctionTarget);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ValidateCachingProducingJunctionToNonExistentTargetOnCacheReplay()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = true;
            AbsolutePath targetDirectoryPath = CreateUniqueDirectory();
            DirectoryArtifact targetDir = DirectoryArtifact.CreateWithZeroPartialSealId(targetDirectoryPath);
            FileArtifact junction = new FileArtifact(CreateUniqueSourcePath("junction")).CreateNextWrittenVersion();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateJunction(junction, targetDir)
            });

            var pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);

            // save junction target to compare to later
            string junctionPath = ArtifactToString(junction);
            string junctionPathTarget = GetReparsePointTarget(junctionPath);

            // Delete both the junction and the directory
            Directory.Delete(junctionPath);
            FileUtilities.DeleteDirectoryContents(targetDirectoryPath.ToString(Context.PathTable), deleteRootDirectory: true);

            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the junction was replayed, even if the target is non-existent
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(junctionPath));
            XAssert.IsTrue(IsJunction(junction));
        }

        [Fact]
        public void ValidateDeleteFileWorksForDirectorySymlinksAndJunctions()
        {
            DirectoryArtifact targetDirForSymlink = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            FileArtifact directorySymlink = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();
            var directorySymlinkPath = ArtifactToString(directorySymlink);
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(directorySymlinkPath, ArtifactToString(targetDirForSymlink), isTargetFile: false));

            FileUtilities.DeleteFile(directorySymlinkPath, retryOnFailure: true);
            var symDirExistence = FileUtilities.TryProbePathExistence(directorySymlinkPath, followSymlink: false);
            XAssert.IsTrue(Directory.Exists(ArtifactToString(targetDirForSymlink)) && symDirExistence.Succeeded && symDirExistence.Result == PathExistence.Nonexistent);

            if (!OperatingSystemHelper.IsUnixOS)
            {
                DirectoryArtifact targetDirFoJunction = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
                FileArtifact junction = new FileArtifact(CreateUniqueSourcePath("junction")).CreateNextWrittenVersion();
                var junctionPath = ArtifactToString(junction);
                FileUtilities.CreateJunction(junctionPath, ArtifactToString(targetDirForSymlink));

                FileUtilities.DeleteFile(junctionPath, retryOnFailure: true);
                var junctionExistence = FileUtilities.TryProbePathExistence(junctionPath, followSymlink: false);
                XAssert.IsTrue(Directory.Exists(ArtifactToString(targetDirFoJunction)) && junctionExistence.Succeeded && junctionExistence.Result == PathExistence.Nonexistent);
            }
        }

        [Theory]
        [InlineData(Operation.SymbolicLinkFlag.FILE)]
        [InlineData(Operation.SymbolicLinkFlag.DIRECTORY)]
        public void ValidateCachingProducingRelativeSymlinks(Operation.SymbolicLinkFlag flag)
        {
            bool usingDirectorySymlinks = flag != Operation.SymbolicLinkFlag.FILE;

            string requestedTarget = usingDirectorySymlinks
                ? R("..", "..", "NonExistentDirectory")
                : R("..", "..", "NonExistentDirectory", "SomeFile");

            FileArtifact symlinkFile = CreateOutputFileArtifact(prefix: "sym-out");

            string symlinkFilePath = ArtifactToString(symlinkFile);

            // Process creates directory / file symlink
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact(prefix: "regular-out")),
                Operation.CreateSymlink(symlinkFile, requestedTarget, flag)
            }).Process;

            RunScheduler().AssertSuccess();

            // save symlink target to compare to later
            string symlinkTarget = GetReparsePointTarget(symlinkFilePath);

            // sanity check that the test created a proper symlink
            XAssert.AreEqual(requestedTarget, symlinkTarget);

            // delete the symlink and replay it from cache
            FileUtilities.DeleteFile(symlinkFilePath);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Check that the symlinkFile was replayed as a symlink
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(symlinkFilePath));
            XAssert.IsTrue(usingDirectorySymlinks ? IsDirectorySymlink(symlinkFile) : IsFileSymlink(symlinkFile));

            // Check that the target path was replayed as a relative path
            string replayedSymlinkTarget = GetReparsePointTarget(symlinkFilePath);
            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);
        }

        [Theory]
        [InlineData(Operation.SymbolicLinkFlag.FILE)]
        [InlineData(Operation.SymbolicLinkFlag.DIRECTORY)]
        public void ValidateCachingProcessProducingChainOfSymlinks(Operation.SymbolicLinkFlag flag)
        {
            bool usingDirectorySymlinks = flag != Operation.SymbolicLinkFlag.FILE;

            FileOrDirectoryArtifact target = usingDirectorySymlinks
                ? FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory()))
                : FileOrDirectoryArtifact.Create(new FileArtifact(CreateSourceFile()));

            // Save content to compare to later
            string targetContent = usingDirectorySymlinks ? string.Empty : File.ReadAllText(ArtifactToString(target)); // src_0

            FileArtifact symlinkA = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_1
            FileArtifact symlinkB = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_2
            FileArtifact symlinkC = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion(); // obj_3

            // pip produces chain of symlinks /symlinkC -> /symlinkB -> symlinkA -> targetFile / targetDir
            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkA, target, flag, doNotInfer: true),
                Operation.CreateSymlink(symlinkB, symlinkA, flag, doNotInfer: true),
                Operation.CreateSymlink(symlinkC, symlinkB, flag, doNotInfer: true)
            });

            if (!usingDirectorySymlinks)
            {
                builder.AddInputFile(target.FileArtifact);
            }

            builder.AddOutputFile(symlinkA.Path);
            builder.AddOutputFile(symlinkB.Path);
            builder.AddOutputFile(symlinkC.Path);

            Process pip = SchedulePipBuilder(builder).Process;
            RunScheduler().AssertSuccess();

            if (!usingDirectorySymlinks)
            {
                string symlinkContent = File.ReadAllText(ArtifactToString(symlinkC));

                // Check symlink chain creation worked
                XAssert.AreEqual(targetContent, symlinkContent);
            }

            // Delete /symlinkB from middle of chain
            // Before second run directory state: obj_1 (symlink), obj_3 (symlink), src_0
            FileUtilities.DeleteFile(ArtifactToString(symlinkB), true);
            RunScheduler().AssertCacheHit(pip.PipId);

            // Validate each of the files are still there and contain the correct content

            XAssert.IsTrue(usingDirectorySymlinks
                ? FileUtilities.DirectoryExistsNoFollow(ArtifactToString(target))
                : FileUtilities.FileExistsNoFollow(ArtifactToString(target)));

            FileOrDirectoryArtifact[] artifacts = new FileOrDirectoryArtifact[] { symlinkA, symlinkB, symlinkC };
            foreach (var entry in artifacts)
            {
                XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(entry)));
                if (!usingDirectorySymlinks)
                {
                    XAssert.AreEqual(targetContent, File.ReadAllText(ArtifactToString(entry)));
                }
            }

            // /targetFile, /symlinkA, and /symlinkC should be in their original state
            XAssert.IsFalse(IsSymlink(ArtifactToString(target)));
            XAssert.IsTrue(usingDirectorySymlinks ? IsDirectorySymlink(symlinkA) : IsFileSymlink(symlinkA));
            XAssert.IsTrue(usingDirectorySymlinks ? IsDirectorySymlink(symlinkC) : IsFileSymlink(symlinkC));

            // /symlinkB is replayed as a symlink
            XAssert.IsTrue(usingDirectorySymlinks ? IsDirectorySymlink(symlinkB) : IsFileSymlink(symlinkB));
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
            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));
            ScheduleRunResult hit = RunScheduler().AssertSuccess();
            hit.AssertCacheHit(pipInit.PipId);
            hit.AssertCacheHit(pipA.PipId);
            hit.AssertCacheHit(pipB.PipId);

            // Make sure end result is the same
            string replayedContent = File.ReadAllText(ArtifactToString(targetFile));
            XAssert.AreEqual(replayedContent, rewrittenContent);
        }

        [Theory]
        [InlineData(Operation.SymbolicLinkFlag.FILE)]
        [InlineData(Operation.SymbolicLinkFlag.DIRECTORY)]
        public void ValidateCachingProcessRewritingSymlink(Operation.SymbolicLinkFlag flag)
        {
            bool usingDirectorySymlinks = flag != Operation.SymbolicLinkFlag.FILE;

            // pipA creates /symlinkFile that points to /targetA
            // pipB rewrites /symlinkFile so it now points to /targetB
            FileOrDirectoryArtifact targetA = usingDirectorySymlinks
                ? FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(prefix: "targetA")))
                : FileOrDirectoryArtifact.Create(CreateSourceFileWithPrefix(prefix: "targetA"));

            FileOrDirectoryArtifact targetB = usingDirectorySymlinks
                ? FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(prefix: "targetB")))
                : FileOrDirectoryArtifact.Create(CreateSourceFileWithPrefix(prefix: "targetB"));

            string targetBContent = usingDirectorySymlinks ? string.Empty : File.ReadAllText(ArtifactToString(targetB));

            FileArtifact symlinkFile = CreateOutputFileArtifact();

            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
               Operation.CreateSymlink(symlinkFile, targetA, flag)
            });

            var pipA = SchedulePipBuilder(pipBuilderA).Process;

            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                Operation.DeleteFile(symlinkFile, doNotInfer: true),
                Operation.CreateSymlink(symlinkFile, targetB, flag, doNotInfer: true)
            });

            if (!usingDirectorySymlinks)
            {
                pipBuilderB.AddInputFile(targetA.FileArtifact);
            }

            pipBuilderB.AddRewrittenFileInPlace(symlinkFile);
            var pipB = SchedulePipBuilder(pipBuilderB).Process;

            RunScheduler().AssertSuccess();

            string symlinkPath = ArtifactToString(symlinkFile);
            var symlinkTarget = GetReparsePointTarget(symlinkPath);

            XAssert.AreEqual(ArtifactToString(targetB), symlinkTarget);

            // check that symlink properly points to targetB
            if (!usingDirectorySymlinks)
            {
                string symlinkTargetContent = File.ReadAllText(symlinkPath);
                XAssert.AreEqual(targetBContent, symlinkTargetContent);
            }

            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));

            var result = RunScheduler().AssertSuccess();
            result.AssertCacheHit(pipA.PipId);
            result.AssertCacheHit(pipB.PipId);

            // check that symlinkFile is a symlink and that it properly points to targetB
            XAssert.IsTrue(usingDirectorySymlinks ? IsDirectorySymlink(symlinkFile) : IsFileSymlink(symlinkFile));
            XAssert.AreEqual(ArtifactToString(targetB), symlinkTarget);
            var replayedSymlinkTarget = GetReparsePointTarget(ArtifactToString(symlinkFile));

            XAssert.AreEqual(symlinkTarget, replayedSymlinkTarget);

            if (!usingDirectorySymlinks)
            {
                var symlinkTargetContent = File.ReadAllText(symlinkPath);
                XAssert.AreEqual(targetBContent, symlinkTargetContent);
            }
        }

        [Feature(Features.OpaqueDirectory)]
        [Theory]
        [InlineData(Operation.SymbolicLinkFlag.FILE)]
        [InlineData(Operation.SymbolicLinkFlag.DIRECTORY)]
        public void ValidateBehaviorProcessProducingSymlinkInOpaqueDirectory(Operation.SymbolicLinkFlag flag)
        {
            bool usingDirectorySymlinks = flag != Operation.SymbolicLinkFlag.FILE;

            string opaqueDir = "opaqueDir";
            AbsolutePath opaqueDirPath = CreateUniquePath(opaqueDir, ObjectRoot);
            Directory.CreateDirectory(opaqueDirPath.ToString(Context.PathTable));

            // /opaqueDir/symlink -> /src/target
            FileArtifact symlinkFile = new FileArtifact(CreateUniquePath("symlink", opaqueDirPath.ToString(Context.PathTable))).CreateNextWrittenVersion();
            FileOrDirectoryArtifact target = usingDirectorySymlinks
                ? FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory()))
                : FileOrDirectoryArtifact.Create(CreateSourceFile());

            var targetFile = CreateOutputFileArtifact(usingDirectorySymlinks ? target.Path : target.Path.GetParent(Context.PathTable));

            // pipA declares output of /opaqueDir and creates /opaqueDir/symlinkFile
            var pipBuilderA = CreatePipBuilder(new Operation[]
            {
                Operation.CreateSymlink(symlinkFile, target, flag, doNotInfer: true),
                Operation.WriteFile(targetFile),
            });

            // /symlinkFile does not need to be declared as an output since it is produced within the declared, produced opaque directory
            pipBuilderA.AddOutputDirectory(opaqueDirPath);

            var paoA = SchedulePipBuilder(pipBuilderA);
            Process pipA = paoA.Process;

            // When using a directory symlink read a file in the target directory thorugh the symlink
            var targetThroughSymlink = symlinkFile.Path.Combine(Context.PathTable, targetFile.Path.GetName(Context.PathTable));
            var op = usingDirectorySymlinks ? Operation.ReadFile(new FileArtifact(targetThroughSymlink), doNotInfer: true) : Operation.ReadFile(symlinkFile, doNotInfer: true);

            // pipB declares dependency on /opaqueDir and consumes /opaqueDir/symlinkFile or /opaqueDir/symlinkFile/targetOutput
            var pipBuilderB = CreatePipBuilder(new Operation[]
            {
                op,
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            // /symlinkFile does not need to be declared as an input since it is within the declared, consumed opaque directory
            pipBuilderB.AddInputDirectory(paoA.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath));
            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            ScheduleRunResult fail = RunScheduler().AssertFailure();

            // pipA successfully creates the symlink
            XAssert.AreEqual(PipResultStatus.Succeeded, fail.PipResults[pipA.PipId]);
            ValidateFilesExistProcessWithSymlinkOutput(target, symlinkFile, usingDirectorySymlinks);

            // pipB fails on consuming symlink file or a file through a directory symlink since it does not declare /target as intermediate a dependency
            XAssert.AreEqual(PipResultStatus.Failed, fail.PipResults[pipB.PipId]);
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess, ArtifactToString(usingDirectorySymlinks ? targetFile : target));

            if (!usingDirectorySymlinks)
            {
                AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
            }

            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void AllowProducingReparsePointsToExistingEntries()
        {
            FileArtifact file = new FileArtifact(CreateSourceFile());
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());

            DirectoryArtifact anotherDir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory());
            FileArtifact junction = new FileArtifact(CreateUniqueSourcePath("junction")).CreateNextWrittenVersion();

            FileArtifact fileSymlinkFile = new FileArtifact(CreateUniqueSourcePath("file_sym")).CreateNextWrittenVersion();
            FileArtifact directorySymlink = new FileArtifact(CreateUniqueSourcePath("dir_sym")).CreateNextWrittenVersion();

            var ops = new System.Collections.Generic.List<Operation>()
            {
                Operation.WriteFile(CreateOutputFileArtifact(prefix: "out")),
                Operation.CreateSymlink(fileSymlinkFile, file, Operation.SymbolicLinkFlag.FILE),
                Operation.CreateSymlink(directorySymlink, dir, Operation.SymbolicLinkFlag.DIRECTORY)
            };

            if (!OperatingSystemHelper.IsUnixOS)
            {
                ops.Add(Operation.CreateJunction(junction, anotherDir));
            }

            // Process creates symlinks
            Process pip = CreateAndSchedulePipBuilder(ops.ToArray()).Process;
            RunScheduler().AssertSuccess();

            ValidateFilesExistProcessWithSymlinkOutput(file, fileSymlinkFile);
            ValidateFilesExistProcessWithSymlinkOutput(dir, directorySymlink, isDirectorySymlinkOrJunction: true);

            if (!OperatingSystemHelper.IsUnixOS)
            {
                ValidateFilesExistProcessWithSymlinkOutput(anotherDir, junction, isDirectorySymlinkOrJunction: true);
            }
        }

        [Feature(Features.SealedSourceDirectory)]
        [Theory]
        [InlineData(true, Operation.SymbolicLinkFlag.FILE)]
        [InlineData(false, Operation.SymbolicLinkFlag.FILE)]
        [InlineData(true, Operation.SymbolicLinkFlag.DIRECTORY)]
        public void AllowProcessCopyingSymlink(bool sourceSealedDirectory, Operation.SymbolicLinkFlag flag)
        {
            bool usingDirectorySymlinks = flag != Operation.SymbolicLinkFlag.FILE;

            // Create symlink from /symlinkFile to /target
            FileOrDirectoryArtifact target = usingDirectorySymlinks
                ? FileOrDirectoryArtifact.Create(DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory()))
                : FileOrDirectoryArtifact.Create(CreateSourceFile());

            FileArtifact symlinkFile = new FileArtifact(CreateUniqueSourcePath(SourceRootPrefix));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(target), isTargetFile: !usingDirectorySymlinks));

            // Get content of /targetFile to compare later
            string targetContent = usingDirectorySymlinks ? string.Empty : File.ReadAllText(ArtifactToString(target));

            // Set up pip
            FileArtifact copiedSymlink = CreateOutputFileArtifact();

            // Process copies a symlink
            Operation[] ops = new Operation[]
            {
                Operation.CopySymlink(symlinkFile, copiedSymlink, symLinkFlag: flag, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact()),
            };

            var builder = CreatePipBuilder(ops);
            builder.AddOutputFile(copiedSymlink.Path, FileExistence.Required);
            builder.AddInputFile(symlinkFile);

            if (!usingDirectorySymlinks)
            {
                builder.AddInputFile(target.FileArtifact);
            }

            if (usingDirectorySymlinks || sourceSealedDirectory)
            {
                // Seal /src directory
                DirectoryArtifact srcDir = SealDirectory(SourceRootPath, SealDirectoryKind.SourceAllDirectories);
                builder.AddInputDirectory(srcDir);
            }

            Process pip = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertSuccess();

            if (!usingDirectorySymlinks)
            {
                string copiedContent = File.ReadAllText(ArtifactToString(copiedSymlink));
                XAssert.AreEqual(targetContent, copiedContent);
            }
            else
            {
                // Check that the copied symlink points to the original target and is a directory symlink
                var copiedSymlinkTarget = GetReparsePointTarget(ArtifactToString(copiedSymlink));
                XAssert.AreEqual(copiedSymlinkTarget, ArtifactToString(target));
                XAssert.IsTrue(IsDirectorySymlink(copiedSymlink));
            }
        }

        /// <summary>
        /// Processes can produce symlinks to absent directories / files because we cache the target paths of the symlink.
        /// </summary>
        [Fact]
        public void AllowProducingSymlinksToAbsentEntries()
        {
            FileArtifact absentFile = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix));
            DirectoryArtifact absentDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueSourcePath(ObjectRootPrefix));

            FileArtifact fileSymlink = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();
            FileArtifact directorySymlink = new FileArtifact(CreateUniqueSourcePath(ObjectRootPrefix)).CreateNextWrittenVersion();

            // Process creates a directory and a file symlink
            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.CreateSymlink(fileSymlink, absentFile, Operation.SymbolicLinkFlag.FILE),
                Operation.CreateSymlink(directorySymlink, absentDirectory, Operation.SymbolicLinkFlag.DIRECTORY)
            });

            var pip = SchedulePipBuilder(builder);

            // Symlinks to absent files and directories should cause no errors
            RunScheduler().AssertSuccess();

            // Validate symlinks were created
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(fileSymlink)));
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(directorySymlink)));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
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
            FileUtilities.DeleteFile(ArtifactToString(targetFile));
            RunScheduler().AssertCacheHit(pip.PipId);
            XAssert.AreEqual(checkpoint1, File.ReadAllText(ArtifactToString(copiedFile)));

            // Redirect /symlinkFile to /newTargetFile
            FileArtifact newTargetFile = CreateSourceFile();
            FileUtilities.DeleteFile(ArtifactToString(symlinkFile));
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFile), ArtifactToString(newTargetFile), isTargetFile: true));
            RunScheduler().AssertCacheMiss(pip.PipId);
            XAssert.AreNotEqual(checkpoint1, File.ReadAllText(ArtifactToString(copiedFile)));
        }

        public enum IgnoreReparsePointMode
        {
            // Ignore all reparse point.
            All,

            // Ignore only non CreateFile API.
            NonCreateFile,

            // Do not ignore any reparse point.
            None
        }

        /// <summary>
        /// Validates that Detours does not follow symlinks when IgnoreReparsePoints and IgnoreNonCreateFileReparsePoints options are enabled
        /// </summary>
        /// <param name="ignoreReparsePointMode"/>
        /// <param name="useCreateFileAPI">
        /// When true, a pip is created that passes a symlink into a CreateFile or NtCreateFile/OpenFile API
        /// When false, a pip is created that passes a symlink into a filemanagement API other than CreateFile or NtCreateFile/OpenFile
        /// </param>
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(IgnoreReparsePointMode.All, true)]
        [InlineData(IgnoreReparsePointMode.All, false)]
        [InlineData(IgnoreReparsePointMode.NonCreateFile, true)]
        [InlineData(IgnoreReparsePointMode.NonCreateFile, false)]
        [InlineData(IgnoreReparsePointMode.None, true)]
        [InlineData(IgnoreReparsePointMode.None, false)]
        public void ValidateCachingUnsafeIgnoreReparsePointReadFile(IgnoreReparsePointMode ignoreReparsePointMode, bool useCreateFileAPI)
        {
            // Allows pips to input and output symlink files without declaring the corresponding target files
            if (ignoreReparsePointMode == IgnoreReparsePointMode.NonCreateFile)
            {
                Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreNonCreateFileReparsePoints = true;
            }
            else if (ignoreReparsePointMode == IgnoreReparsePointMode.All)
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

            if (ignoreReparsePointMode == IgnoreReparsePointMode.None
                || (ignoreReparsePointMode == IgnoreReparsePointMode.NonCreateFile && useCreateFileAPI))
            {
                // Detours is only ignoring symlinks for APIs other than CreateFile and NtCreateFile/OpenFile
                // Since ReadFile calls CreateFile, expect a disallowed file access on undeclared underlying /targetFile

                RunScheduler().AssertFailure();
                AssertVerboseEventLogged(
                    ProcessesLogEventId.PipProcessDisallowedFileAccess,
                    count: useCreateFileAPI
                    ? 1 
                    : 2 /* File.Copy internally calls CreateFileW upon failure */);
                AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
                AssertWarningEventLogged(
                    LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations,
                    count: useCreateFileAPI ? 1 : 0 /* The process execution exit with non 0 exit code. */);
                AssertErrorEventLogged(LogEventId.FileMonitoringError);

                if (!useCreateFileAPI)
                {
                    AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);
                }
            }
            else
            {
                RunScheduler().AssertSuccess();
            }
        }

        [Fact]
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
            XAssert.IsFalse(IsFileSymlink(originalLocation));
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
            AssertErrorEventLogged(LogEventId.PipCopyFileFailed);
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
            AssertErrorEventLogged(LogEventId.PipCopyFileFailed);
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

            AssertErrorEventLogged(LogEventId.PipCopyFileFailed);
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
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(output)));
            XAssert.IsFalse(IsFileSymlink(output));
            XAssert.AreEqual(File.ReadAllText(ArtifactToString(targetFile)), File.ReadAllText(ArtifactToString(output)));
        }

        [Fact]
        public void DeleteSelfLoopSymlink()
        {
            // TODO: enable on other platforms
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return;
            }

            // Symlink chain:
            // symlinkFile1 -> symlinkFile1
            FileArtifact symlinkFile1 = CreateOutputFileArtifact();

            CreateAndSchedulePipBuilder(new Operation[]
            {
                // create a self-looping symlink
                Operation.CreateSymlink(symlinkFile1, symlinkFile1, Operation.SymbolicLinkFlag.FILE, doNotInfer: true),

                // delete that symlink (this used to not terminate before handling symlink loops in the Linux sandbox was implemented)
                Operation.DeleteFile(symlinkFile1.CreateNextWrittenVersion(), doNotInfer: true),

                // write a dummy output at "symlinkFile1" location (just to avoid DFA's)
                Operation.WriteFile(symlinkFile1)
            });

            RunScheduler().AssertSuccess();
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(symlinkFile1)));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void AccessFileSymlinkThroughDirectorySymlinkOrJunction(bool useJunction, bool ignoreFullReparsePointResolving)
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

            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = ignoreFullReparsePointResolving;

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

            if (!ignoreFullReparsePointResolving || OperatingSystemHelper.IsUnixOS)
            {
                builder.AddInputFile(FileArtifact.CreateSourceFile(linkFile));

                // Specify /Enlist/Source/linkFile and /Enlist/Source as input files, they are both reparse points
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

            // When full reparse point resolving is enabled and junctions are being used, we have to add a
            // directory translation for each reparse point (in this case a junction) we don't want to resolve
            if (!ignoreFullReparsePointResolving && useJunction)
            {
                DirectoryTranslator.AddTranslation(currentIntermediateDirectory.ToString(pathTable), sourceDirectory.ToString(pathTable));
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

            if (useJunction && ignoreFullReparsePointResolving)
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

        [FactIfSupported(requiresSymlinkPermission: true, requiresWindowsBasedOperatingSystem: true)]
        public void ResolvedSymlinkCachingBehavior()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            string symlinkDir = Path.Combine(SourceRoot, "symlinkDir");
            FileArtifact symlinkDirArtifact = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, symlinkDir));

            string targetDirectory = Path.Combine(SourceRoot, "targetDir");
            FileUtilities.CreateDirectory(targetDirectory);

            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkDir, targetDirectory, isTargetFile: false));

            FileArtifact targetFile = CreateSourceFile(targetDirectory);
            FileArtifact symlinkFile = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, Path.Combine(symlinkDir, targetFile.Path.GetName(Context.PathTable).ToString(Context.StringTable))));

            FileArtifact outFile = CreateOutputFileArtifact();

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(symlinkFile),
                Operation.WriteFile(outFile),
            });

            builder.AddInputFile(symlinkDirArtifact);
            builder.AddInputFile(targetFile);

            var pip = SchedulePipBuilder(builder).Process;

            // First run should be a miss, second one a hit
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
            RunScheduler().AssertSuccess().AssertCacheHit(pip.PipId);

            // Now delete the symlinked directory. Observe this operation does not remove the target directory.
            Directory.Delete(symlinkDir);
            // Let's make sure the target is still there
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(Path.Combine(targetDirectory, symlinkFile.Path.GetName(Context.PathTable).ToString(Context.StringTable))));

            // Next run should be a cache miss
            RunScheduler().AssertSuccess().AssertCacheMiss(pip.PipId);
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void ManifestOfResolvedAccessIsProperlyComputed()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            // Create the following layout
            // sodA
            //  -- nestedDir
            //    -- output.txt
            // sodB
            //  -- junction -> nestedDir

            AbsolutePath sodA = CreateUniqueDirectory(prefix: "sodA");
            AbsolutePath sodB = CreateUniqueDirectory(prefix: "sodB");
            DirectoryArtifact nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodA.Combine(Context.PathTable, "nested"));
            DirectoryArtifact junctionDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodB.Combine(Context.PathTable, "junction"));

            FileArtifact outputViaJunction = CreateOutputFileArtifact(root: junctionDir, prefix:"output.txt");
            FileArtifact outputViaRealPath = CreateOutputFileArtifact(root: nestedDir, prefix: "output.txt");

            // Create a file via the junction
            var writerBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.CreateDir(nestedDir),
                Operation.CreateJunction(junctionDir, nestedDir, doNotInfer: true),
                Operation.WriteFile(outputViaJunction, doNotInfer: true),
            });

            writerBuilder.AddOutputDirectory(sodA, SealDirectoryKind.SharedOpaque);
            writerBuilder.AddOutputDirectory(sodB, SealDirectoryKind.SharedOpaque);

            var writer = SchedulePipBuilder(writerBuilder);

            // Read the previously created file via the real path
            var readerBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputViaRealPath, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact()), // dummy output
            });

            // Make sure sodA is the one containing the output by only specifying that dependency when reading the file
            // The manifest path is used for determining SOD membership, so if the output file ended up under sodA, that means
            // the manifest path was properly resolved
            readerBuilder.AddInputDirectory(writer.ProcessOutputs.GetOpaqueDirectory(sodA));

            var reader = SchedulePipBuilder(readerBuilder);

            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void DirectoryRemovalInvalidatesTheCache()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            // Create the following layout
            // sodA 
            //  -- nestedDir
            //    -- output.txt 
            // sodB 
            //  -- junction -> sodA 
            // Then change the layout to:
            // sodA 
            //  -- nestedDir
            //    -- output.txt 
            // sodB 
            //  -- nestedDir
            //    -- symlink -> output.txt


            AbsolutePath sodA = CreateUniqueDirectory(prefix: "sodA");
            AbsolutePath sodB = CreateUniqueDirectory(prefix: "sodB");

            var nestedDirPath = sodA.Combine(Context.PathTable, "nestedDir").ToString(Context.PathTable);
            FileUtilities.CreateDirectory(nestedDirPath);

            DirectoryArtifact nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodA.Combine(Context.PathTable, "nestedDir"));
            DirectoryArtifact junctionDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodB.Combine(Context.PathTable, "junction"));
            DirectoryArtifact nestedDirViaJunction = DirectoryArtifact.CreateWithZeroPartialSealId(junctionDir.Path.Combine(Context.PathTable, "nestedDir"));

            FileArtifact outputViaJunction = FileArtifact.CreateOutputFile(nestedDirViaJunction.Path.Combine(Context.PathTable, "output.txt"));
            FileArtifact outputViaRealPath = FileArtifact.CreateOutputFile(nestedDir.Path.Combine(Context.PathTable, "output.txt"));


            FileUtilities.CreateDirectory(ArtifactToString(nestedDir));
            File.WriteAllText(ArtifactToString(outputViaRealPath), System.Guid.NewGuid().ToString());

            var writerBuilder = CreatePipBuilder(new Operation[]
            {
                // absent, and therefore cached as not needing reparse point resolution 
                Operation.Probe(outputViaJunction, doNotInfer: true), 
                // the junction creation should invalidate the reparse point cache, and also invalidate its
                // descendants, which includes the above probe
                Operation.CreateJunction(junctionDir, DirectoryArtifact.CreateWithZeroPartialSealId(sodA), doNotInfer: true),
                // If not done correctly, the mapping in the cache for the juntion will still exist, and writing under this path will be like writing under sodA and produce an error.
                Operation.DeleteDir(junctionDir, doNotInfer: true),
                Operation.CreateDir(junctionDir, doNotInfer: true),
                Operation.WriteFile(outputViaJunction, System.Guid.NewGuid().ToString(), doNotInfer: true)
            });

            writerBuilder.AddOutputDirectory(sodB, SealDirectoryKind.Opaque);
            writerBuilder.Options |= Process.Options.AllowPreserveOutputs;

            var writer = SchedulePipBuilder(writerBuilder);

            // If this fails, then detours likely thinks the junction is still there.
            RunScheduler().AssertSuccess();
        }

        [FactIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        public void ReparsePointCreationInvalidatesTheCache()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            // Create the following layout
            // sodA 
            //  -- nestedDir
            //    -- output.txt 
            // sodB 
            //  -- junction -> sodA 

            AbsolutePath sodA = CreateUniqueDirectory(prefix: "sodA");
            AbsolutePath sodB = CreateUniqueDirectory(prefix: "sodB");

            var nestedDirPath = sodA.Combine(Context.PathTable, "nestedDir").ToString(Context.PathTable);
            FileUtilities.CreateDirectory(nestedDirPath);

            DirectoryArtifact nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodA.Combine(Context.PathTable, "nestedDir"));
            DirectoryArtifact junctionDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodB.Combine(Context.PathTable, "junction"));
            DirectoryArtifact nestedDirViaJunction = DirectoryArtifact.CreateWithZeroPartialSealId(junctionDir.Path.Combine(Context.PathTable, "nestedDir"));

            FileArtifact outputViaJunction = FileArtifact.CreateOutputFile(nestedDirViaJunction.Path.Combine(Context.PathTable, "output.txt"));
            FileArtifact outputViaRealPath = FileArtifact.CreateOutputFile(nestedDir.Path.Combine(Context.PathTable, "output.txt"));

            var writerBuilder = CreatePipBuilder(new Operation[]
            {
                // absent, and therefore cached as not needing reparse point resolution 
                Operation.Probe(outputViaJunction, doNotInfer: true), 
                // the junction creation should invalidate the reparse point cache, and also invalidate its
                // descendants, which includes the above probe
                Operation.CreateJunction(junctionDir, DirectoryArtifact.CreateWithZeroPartialSealId(sodA), doNotInfer: true),  
                Operation.WriteFile(outputViaRealPath, doNotInfer: true),
                // present now via the creation of the junction and the output. Since the cache should have 
                // invalidated this entry, now this should be flagged as needing resolution
                Operation.ReadFile(outputViaJunction, doNotInfer: true), 
            });

            writerBuilder.AddOutputDirectory(sodA, SealDirectoryKind.SharedOpaque); 
            writerBuilder.AddOutputDirectory(sodB, SealDirectoryKind.SharedOpaque);
            writerBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            var writer = SchedulePipBuilder(writerBuilder);

            RunScheduler().AssertSuccess();

            // Simulate scrubbing
            FileUtilities.DeleteFile(junctionDir.Path.ToString(Context.PathTable));
            FileUtilities.DeleteFile(outputViaRealPath.Path.ToString(Context.PathTable));

            // A cache hit guarantees that all paths that need resolution are actually resolved
            RunScheduler().AssertCacheHit(writer.Process.PipId);
        }

        [TheoryIfSupported(requiresAdmin: true, requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void IndividualPipsCanTurnOffReparsePointResolution(bool pipDisablesFullReparsePointResolution)
        {
            // Enable reparse point resolution globally
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnoreFullReparsePointResolving = false;

            // Create the following layout
            // sodA 
            //  -- nestedDir
            //    -- output.txt 
            // sodB 
            //  -- junction -> sodA 

            AbsolutePath sodA = CreateUniqueDirectory(prefix: "sodA");
            AbsolutePath sodB = CreateUniqueDirectory(prefix: "sodB");

            var nestedDirPath = sodA.Combine(Context.PathTable, "nestedDir").ToString(Context.PathTable);
            FileUtilities.CreateDirectory(nestedDirPath);

            DirectoryArtifact nestedDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodA.Combine(Context.PathTable, "nestedDir"));
            DirectoryArtifact junctionDir = DirectoryArtifact.CreateWithZeroPartialSealId(sodB.Combine(Context.PathTable, "junction"));
            DirectoryArtifact nestedDirViaJunction = DirectoryArtifact.CreateWithZeroPartialSealId(junctionDir.Path.Combine(Context.PathTable, "nestedDir"));

            FileArtifact outputViaJunction = FileArtifact.CreateOutputFile(nestedDirViaJunction.Path.Combine(Context.PathTable, "output.txt"));
            FileArtifact outputViaRealPath = FileArtifact.CreateOutputFile(nestedDir.Path.Combine(Context.PathTable, "output.txt"));

            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(junctionDir), sodA.ToString(Context.PathTable), isTargetFile: false));

            // The pip writes via a junction, and the final target lands outside of the cone of the opaque
            var writerBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outputViaJunction, doNotInfer: true),
            });

            writerBuilder.AddOutputDirectory(sodB, SealDirectoryKind.SharedOpaque);
            writerBuilder.Options |= Process.Options.AllowUndeclaredSourceReads;

            if (pipDisablesFullReparsePointResolution)
            {
                writerBuilder.Options |= Process.Options.DisableFullReparsePointResolving;
            }

            var writer = SchedulePipBuilder(writerBuilder);

            var result = RunScheduler();

            // If the pip ignores full reparse point resolution, then we shouldn't notice the junction
            if (pipDisablesFullReparsePointResolution)
            {
                result.AssertSuccess();
            }
            else
            {
                // Otherwise, a DFA is expected.
                result.AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
                IgnoreWarnings();
            }
        }

        /// <summary>
        /// Pips delete their outputs before running. When a symlink is declared as output,
        /// we should delete the symlink itself and not the underlying target file.
        /// This is called post-running the pip to validate the state of the symlink and target file.
        /// </summary>
        /// <param name="targetFile">underlying target file symlinkFile points to</param>
        /// <param name="symlinkFile">symlinkFile that is declared as output of pip</param>
        protected void ValidateFilesExistProcessWithSymlinkOutput(FileOrDirectoryArtifact target, FileArtifact symlinkFile, bool isDirectorySymlinkOrJunction = false)
        {
            // Check that /targetFile still exists as a file, not a symlink
            XAssert.IsTrue(isDirectorySymlinkOrJunction ? FileUtilities.DirectoryExistsNoFollow(ArtifactToString(target)) : FileUtilities.FileExistsNoFollow(ArtifactToString(target)));
            XAssert.IsFalse(new FileInfo(ArtifactToString(target)).Attributes.HasFlag(FileAttributes.ReparsePoint));

            // Sanity check that symlink path still exists. It's possible for this to be replaced with a copied file or remain a symlink.
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(ArtifactToString(symlinkFile)));

            var attrs = new FileInfo(ArtifactToString(symlinkFile)).Attributes;
            XAssert.IsTrue(attrs.HasFlag(FileAttributes.ReparsePoint));
            XAssert.IsTrue(isDirectorySymlinkOrJunction ? attrs.HasFlag(FileAttributes.Directory) : !attrs.HasFlag(FileAttributes.Directory));
        }

        protected bool IsFileSymlink(FileArtifact file) => ReparsePointTests.IsSymlink(ArtifactToString(file));
        protected bool IsDirectorySymlink(FileArtifact dir) => ReparsePointTests.IsSymlink(ArtifactToString(dir), true);
        protected bool IsJunction(FileArtifact dir)
        {
            var reparsePointType = FileUtilities.TryGetReparsePointType(ArtifactToString(dir));
            if (reparsePointType.Succeeded)
            {
                return reparsePointType.Result == ReparsePointType.Junction;
            }

            return false;
        }

        internal static bool IsSymlink(string file, bool isDirectorySymlink = false)
        {
            var attr = new FileInfo(file).Attributes;

            if (OperatingSystemHelper.IsUnixOS)
            {
                return attr.HasFlag(FileAttributes.ReparsePoint);
            }

            return attr.HasFlag(FileAttributes.ReparsePoint) && (isDirectorySymlink ? attr.HasFlag(FileAttributes.Directory) : !attr.HasFlag(FileAttributes.Directory));
        }

        private string GetReparsePointTarget(string filePath)
        {
            var result = FileUtilities.TryCreateOrOpenFile(
                filePath,
                FileDesiredAccess.GenericRead,
                FileShare.Read | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagOpenReparsePoint | FileFlagsAndAttributes.FileFlagBackupSemantics,
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
