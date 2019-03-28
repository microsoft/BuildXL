// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
using BuildXL.Native.IO;
using IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "WindowsOSOnly")]
    [Feature(Features.DirectoryTranslation)]
    public class DirectoryTranslationJunctionSubstTests : SchedulerIntegrationTestBase
    {
        // Flag to remove subst in the DefineDosDevice extern function.
        private const int REMOVE_DRIVE = 2;

        // Flag for adding subst in the DefineDosDevice extern function
        private const int ADD_DRIVE = 0;

        // PInvoke to subst
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DefineDosDevice(int flags, string devname, string path);

        private List<string> m_createdDrives = new List<string>();

        public DirectoryTranslationJunctionSubstTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        
            // Ensure we remove any drivers that were created during this test
            foreach (string drive in m_createdDrives)
            {
                RemoveSubst(drive);
            }
        }

        /// <summary>
        ///  Maps what is called a "virtual drive" to a path
        ///  https://technet.microsoft.com/en-us/library/bb491006.aspx
        /// </summary>
        /// <param name="letter"> letter naming virtual drive</param>
        /// <param name="path"> path that the virtual drive should map to</param>
        private bool MakeSubst(string letter, string path)
        {
            // ............................................................
            // FYI on Subst: Windows does not allow to subst an already subst directory
            // That is, do not try to map a virtual drive to another virtual drive
            // https://msdn.microsoft.com/en-us/library/ms828722.aspx
            // ............................................................

            return DefineDosDevice(ADD_DRIVE, letter, path);
        }

        /// <summary>
        /// Removes mapping from a "virtual drive" to a path
        /// </summary>
        /// <param name="letter">letter naming virtual drive</param>
        private void RemoveSubst(string letter)
        {
            DefineDosDevice(REMOVE_DRIVE, letter, null);
        }

        /// <summary>
        /// returns letter + ":"
        /// </summary>
        /// <returns>returns unused drive letter with column unless none is found, in which case it returns null</returns>
        private string MakeSubstPath(string path)
        {
             // Drive letter used for subst
            string substDriveLetter;

            // Drive letter used + colon
            string substDriveLetterColon;

            substDriveLetter = DriveUtils.GetFirstUnusedDriveLetter();

            if (substDriveLetter == null)
            {
                return null;
            }

            substDriveLetterColon = substDriveLetter + ":";
            XAssert.IsTrue(MakeSubst(substDriveLetterColon, path), "Failed to make subst");

            // Add this to make sure we remove later
            m_createdDrives.Add(substDriveLetterColon);
            return substDriveLetterColon;
        }

        /// <summary>
        /// Testing pips that write and read to a subst path (i.e., path with a virtual drive and not the original drive)
        /// can run and have cache hits. However, we currently are not testing if the cache operates on
        /// the original drive path (important for replaying and sharing cache) 
        /// because EngineCache (test cache) does not implement root translation, which is required
        /// for the cache to operate on the original drive path and not the virtual drive path
        /// </summary>
        [Fact]
        public void WriteToSubstDirTest()
        {
            string drive = null;
            try
            {
                // Make subst
                drive = MakeSubstPath(ObjectRoot);

                // If there are no drive letters available to use as virtual drive
                // then we have to abort this test
                if (drive == null)
                {
                    return;
                }

                string substPath = drive + Path.DirectorySeparatorChar;

                // SubstPath should now exist
                XAssert.IsTrue(Directory.Exists(substPath));

                FileArtifact substFile = CreateOutputFileArtifact(substPath);

                // ...........PIP A...........
                Process pipA = CreateAndSchedulePipBuilder(new Operation[]
                {
                    Operation.WriteFile(substFile)
                }).Process;

                // ...........PIP B...........
                Process pipB = CreateAndSchedulePipBuilder(new Operation[]
                {
                Operation.ReadFile(substFile),
                Operation.WriteFile(CreateOutputFileArtifact(substPath))
                }).Process;

                RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);

                RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);
            }
            finally
            {
                // Remove subst
                if (drive != null)
                {
                    RemoveSubst(drive);
                }
            }
        }

        /// <summary>
        /// Pips write and read to a junction that does *not* have a directory translation
        /// Illustrates that the target of a junction does not need to be set as input (in contrast to symlinks)
        /// </summary>
        [Fact]
        public void WriteToJunctionTest()
        {
            //// ............ Setting up the output dir for the pips .................
            AbsolutePath originPath = CreateUniqueDirectory(ObjectRoot);
            AbsolutePath junctionPath = CreateUniqueDirectory(ObjectRoot);
            FileArtifact junctionFile = CreateOutputFileArtifact(junctionPath.ToString(Context.PathTable));

            // .......... Creating the Junction ..........
            FileUtilities.CreateJunction(junctionPath.ToString(Context.PathTable), originPath.ToString(Context.PathTable));

            // ........... PIP A ...........
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                   Operation.WriteFile(junctionFile)
            }).Process;

            // ...........PIP B...........
            var builderB = CreatePipBuilder(new Operation[]
            {
                   Operation.ReadFile(junctionFile, doNotInfer: true),
                   Operation.WriteFile(CreateOutputFileArtifact(originPath.ToString(Context.PathTable)))
            });

            // Currently, BuildXL does not require adding the target of a junction as input (in contrast to symlinks)
            builderB.AddInputFile(junctionFile);

            var pipB = SchedulePipBuilder(builderB);

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.Process.PipId);

            // Should have hit with junction
            RunScheduler().AssertCacheHit(pipA.PipId, pipB.Process.PipId);
        }

        /// <summary>
        /// Pip reads a symlink via a path with a junction
        /// We use directory translation in order to only require specifying the
        /// junction target as input and fail when we do not provide a translation
        /// </summary>
        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void ReadSymlinkOnJunctionTest(bool translate)
        {
            // ............ Setting up the output dir for the pips .................
            AbsolutePath targetPath = CreateUniqueDirectory(ObjectRoot);
            AbsolutePath junctionPath = CreateUniqueDirectory(ObjectRoot);

            // .......... Creating the Junction ..........
            FileUtilities.CreateJunction(junctionPath.ToString(Context.PathTable), targetPath.ToString(Context.PathTable));

            ReparsePointWithSymlinkTest(targetPath.ToString(Context.PathTable), junctionPath.ToString(Context.PathTable), translate);
        }

        /// <summary>
        /// Pip reads a symlink via a subst path
        /// We use directory translation in order to only require specifying the
        /// subst target as input and fail when we do not provide a translation
        /// </summary>
        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void ReadSymlinkOnSubstTest(bool translate)
        {
            string drive = null;
            try
            {
                // ............ Setting up the output dir for the pips .................
                AbsolutePath targetPath = CreateUniqueDirectory(ObjectRoot);
                // .......... Creating the Subst ..........
                drive = MakeSubstPath(targetPath.ToString(Context.PathTable));
                string substPath = drive + Path.DirectorySeparatorChar;

                ReparsePointWithSymlinkTest(targetPath.ToString(Context.PathTable), substPath, translate);
            }
            finally
            {
                // Remove subst
                if (drive != null)
                {
                    RemoveSubst(drive);
                }
            }
        }

        [Fact]
        public void ChangeJunctionShouldRebuild()
        {
            const string ReadFileName = "file";

            AbsolutePath targetDirectoryA = CreateUniqueDirectory(SourceRoot, "DirA");
            AbsolutePath targetDirectoryB = CreateUniqueDirectory(SourceRoot, "DirB");

            string expandedTargetDirectoryA = targetDirectoryA.ToString(Context.PathTable);
            string expandedTargetDirectoryB = targetDirectoryB.ToString(Context.PathTable);

            AbsolutePath junction1 = CreateUniqueDirectory(SourceRoot, "Junc1");

            string expandedJunction1 = junction1.ToString(Context.PathTable);

            FileArtifact targetAFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryA);
            FileArtifact targetBFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryB);

            WriteSourceFile(targetAFile);
            WriteSourceFile(targetBFile);

            FileArtifact junction1File = CreateFileArtifactWithName(ReadFileName, expandedJunction1);

            // Create junction
            //     Junc1 -> DirA
            FileUtilities.CreateJunction(expandedJunction1, expandedTargetDirectoryA);

            FileArtifact outputP = CreateOutputFileArtifact();
            FileArtifact outputQ = CreateOutputFileArtifact();

            var pipP = SchedulePipBuilder(CreatePipBuilder(new Operation[] { Operation.CopyFile(junction1File, outputP) })).Process;

            // We don't need directory translator here because pipP reads through junction1File.
            // But we want to show that incremental scheduling is not sensitive to directory translator.
            DirectoryTranslator = new DirectoryTranslator();
            DirectoryTranslator.AddTranslation(expandedTargetDirectoryA, expandedJunction1);

            RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId);

            // Change junction
            //     Junc1 -> DirB
            FileUtilities.CreateJunction(expandedJunction1, expandedTargetDirectoryB);

            DirectoryTranslator = new DirectoryTranslator();
            DirectoryTranslator.AddTranslation(expandedTargetDirectoryB, expandedJunction1);

            RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId);

            // Modify DirB\File
            WriteSourceFile(targetBFile);
            RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId);

            // Modify DirA\File
            WriteSourceFile(targetAFile);

            // Note that, even though DirA\File has nothing to do with the build as the junction has changed to DirB,
            // incremental scheduling, when enabled, will still mark pipP dirty. In the first build the file change tracker
            // tracked DirA\File, and introduced a mapping from FileId(DirA\File) to Path(Junc1\File). Thus, when DirA\File
            // changes, Path(Junc1\File) is affected, and since pipP specifies Path(Junc1\File) as its input, pipP is affected as well.
            RunScheduler().AssertSuccess().AssertScheduled(pipP.PipId).AssertCacheHit(pipP.PipId);

            // Modify again DirA\File.
            // After the above run, the mapping FileId(DirA\File) to Path(Junc1\File) has been removed, and thus,
            // any change to DirA\File should not affect pipP.
            WriteSourceFile(targetAFile);

            var result = RunScheduler().AssertSuccess();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                result.AssertNotScheduled(pipP.PipId);
            }
            else
            {
                result.AssertCacheHit(pipP.PipId);
            }
        }

        [Fact]
        public void ChangeJunctionWithTranslateDirectoryShouldRebuild()
        {
            const string ReadFileName = "file";

            AbsolutePath targetDirectoryA = CreateUniqueDirectory(SourceRoot, "DirA");
            AbsolutePath targetDirectoryB = CreateUniqueDirectory(SourceRoot, "DirB");
            AbsolutePath targetDirectoryC = CreateUniqueDirectory(SourceRoot, "DirC");

            string expandedTargetDirectoryA = targetDirectoryA.ToString(Context.PathTable);
            string expandedTargetDirectoryB = targetDirectoryB.ToString(Context.PathTable);
            string expandedTargetDirectoryC = targetDirectoryC.ToString(Context.PathTable);

            AbsolutePath junction1 = CreateUniqueDirectory(SourceRoot, "Junc1");
            AbsolutePath junction2 = CreateUniqueDirectory(SourceRoot, "Junc2");

            string expandedJunction1 = junction1.ToString(Context.PathTable);
            string expandedJunction2 = junction2.ToString(Context.PathTable);

            FileArtifact targetAFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryA);
            FileArtifact targetBFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryB);
            FileArtifact targetCFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryC);

            WriteSourceFile(targetAFile);
            WriteSourceFile(targetBFile);
            WriteSourceFile(targetCFile);

            FileArtifact junction1File = CreateFileArtifactWithName(ReadFileName, expandedJunction1);
            FileArtifact junction2File = CreateFileArtifactWithName(ReadFileName, expandedJunction2);

            // Create junction
            //     Junc1 -> DirA
            //     Junc2 -> DirC
            FileUtilities.CreateJunction(expandedJunction1, expandedTargetDirectoryA);
            FileUtilities.CreateJunction(expandedJunction2, expandedTargetDirectoryC);

            FileArtifact outputP = CreateOutputFileArtifact();
            FileArtifact outputQ = CreateOutputFileArtifact();

            var builderP = CreatePipBuilder(new Operation[] { Operation.CopyFile(junction1File, outputP) });
            var pipP = SchedulePipBuilder(builderP).Process;

            var builderQ = CreatePipBuilder(new Operation[] { Operation.CopyFile(targetCFile, outputQ, doNotInfer: true) });
            builderQ.AddInputFile(junction2File);
            builderQ.AddOutputFile(outputQ.Path, FileExistence.Required);
            var pipQ = SchedulePipBuilder(builderQ).Process;

            DirectoryTranslator = new DirectoryTranslator();
            DirectoryTranslator.AddTranslation(expandedTargetDirectoryA, expandedJunction1);
            DirectoryTranslator.AddTranslation(expandedTargetDirectoryC, expandedJunction2);

            RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId, pipQ.PipId);

            // Change junction
            //     Junc2 -> DirB
            FileUtilities.CreateJunction(expandedJunction1, expandedTargetDirectoryB);

            var result = RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId);

            if (Configuration.Schedule.IncrementalScheduling)
            {
                // pipQ should not be affected at all.
                result.AssertNotScheduled(pipQ.PipId);
            }
            else
            {
                // pipQ is scheduled but results in cache hit.
                result.AssertScheduled(pipQ.PipId).AssertCacheHit(pipQ.PipId);
            }
        }

        /// <summary>
        /// This test shows the limitation of incremental scheduling in the presence of junction.
        /// That is, if the intermediate junction is modified, and not all incarnations of paths
        /// are tracked, then, similar to symlinks, we can have an underbuild.
        /// </summary>
        [Fact]
        public void ChangeIntermediateJunctionDoesNotRebuild()
        {
            const string ReadFileName = "file";

            AbsolutePath targetDirectoryA = CreateUniqueDirectory(SourceRoot, "DirA");
            AbsolutePath targetDirectoryB = CreateUniqueDirectory(SourceRoot, "DirB");

            string expandedTargetDirectoryA = targetDirectoryA.ToString(Context.PathTable);
            string expandedTargetDirectoryB = targetDirectoryB.ToString(Context.PathTable);

            AbsolutePath junction1 = CreateUniqueDirectory(SourceRoot, "Junc1");
            AbsolutePath junction2 = CreateUniqueDirectory(SourceRoot, "Junc2");

            string expandedJunction1 = junction1.ToString(Context.PathTable);
            string expandedJunction2 = junction2.ToString(Context.PathTable);

            FileArtifact targetAFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryA);
            FileArtifact targetBFile = CreateFileArtifactWithName(ReadFileName, expandedTargetDirectoryB);

            WriteSourceFile(targetAFile);
            WriteSourceFile(targetBFile);

            FileArtifact junction1File = CreateFileArtifactWithName(ReadFileName, expandedJunction1);
            FileArtifact junction2File = CreateFileArtifactWithName(ReadFileName, expandedJunction2);

            // Create junction
            //     Junc1 -> Junc2 -> DirA
            FileUtilities.CreateJunction(expandedJunction2, expandedTargetDirectoryA);
            FileUtilities.CreateJunction(expandedJunction1, expandedJunction2);

            FileArtifact outputP = CreateOutputFileArtifact();

            var builderP = CreatePipBuilder(new Operation[] { Operation.CopyFile(junction2File, outputP, doNotInfer: true) });
            builderP.AddInputFile(junction1File);
            builderP.AddOutputFile(outputP.Path, FileExistence.Required);
            var pipP = SchedulePipBuilder(builderP).Process;

            DirectoryTranslator = new DirectoryTranslator();
            DirectoryTranslator.AddTranslation(expandedTargetDirectoryA, expandedJunction2);
            DirectoryTranslator.AddTranslation(expandedJunction2, expandedJunction1);

            RunScheduler().AssertSuccess().AssertCacheMiss(pipP.PipId);

            // Change junction
            //     Junc2 -> DirB
            FileUtilities.CreateJunction(expandedJunction2, expandedTargetDirectoryB);

            var result = RunScheduler().AssertSuccess();

            if (Configuration.Schedule.IncrementalScheduling)
            {
                // Junc2 is not tracked because BuildXL only track final translation, which is Junc1.
                result.AssertNotScheduled(pipP.PipId);
            }
            else
            {
                result.AssertCacheMiss(pipP.PipId);
            }
        }

        private void ReparsePointWithSymlinkTest(string targetPath, string reparsePoint, bool useDirTranslation)
        {
            const string TARGET_NAME = "targetName";
            const string TARGET_SYM_NAME = "targetSymFile";

            // Target file artifacts 
            FileArtifact targetFile = CreateFileArtifactWithName(TARGET_NAME, targetPath);
            WriteSourceFile(targetFile);
            FileArtifact symlinkFileOnTarget = CreateFileArtifactWithName(TARGET_SYM_NAME, targetPath);

            // Create a symlink file on target
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(ArtifactToString(symlinkFileOnTarget), ArtifactToString(targetFile), isTargetFile: true));

            // junction file artifacts to read in pipA
            FileArtifact symlinkFileOnReparsePoint = CreateFileArtifactWithName(TARGET_SYM_NAME, reparsePoint);

            // ........... PIP A ...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                   Operation.ReadFile(symlinkFileOnReparsePoint, doNotInfer: true),
                   Operation.WriteFile(CreateOutputFileArtifact(targetPath))
            });

            builderA.AddInputFile(symlinkFileOnTarget);
            builderA.AddInputFile(targetFile);
            var pipA = SchedulePipBuilder(builderA);

            if (useDirTranslation)
            {
                string junctionPathStr = reparsePoint;
                string targetPathStr = targetPath;

                // ............CREATING DIRECTORY TRANSLATOR....................
                // Before scheduling, we need to: 
                // (1) init the directory translator
                // (2) add any translations 
                // (3) then seal
                // - bullets 1 and 2 are happening for us now in the SchedulerIntegrationTestBase
                DirectoryTranslator.AddTranslation(junctionPathStr, targetPathStr);

                RunScheduler().AssertSuccess();
            }
            else
            {
                // No directory translation will cause an error
                RunScheduler().AssertFailure();
                AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);
                AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess);
                AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
        }
    }
}
