// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using AssemblyHelper = BuildXL.Utilities.AssemblyHelper;

namespace Test.BuildXL.Storage.Admin
{
    /// <summary>
    /// Tests for detecting changes via a <see cref="FileChangeTrackingSet" />.
    /// </summary>
    public class FileChangeDetectionTests : TemporaryStorageTestBase
    {
        #region Singly-linked file and directory invalidations

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDataChangeToExistentFileAndCheckpoint()
        {
            const string FileName = @"FileToModify";
            
            WriteFile(FileName, "Original");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(FileName);

            WriteFile(FileName, "Modified");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(FileName));

            // Track the file again (the path will not trigger again otherwise). We shouldn't see the modification a second time.
            support.Track(FileName);

            // No change notifications were expected; last change was before the last checkpoint
            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDataChangeToExistentFileOnce()
        {
            const string FileName = @"FileToModify";
            
            WriteFile(FileName, "Original");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(FileName);

            WriteFile(FileName, "Modified");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(FileName));

            // Note that we have not tracked the path again, so changes are no longer relevant.
            WriteFile(FileName, "Modified again");

            // No change notifications expected; file was not tracked again preceding the last change
            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectAbsenceOfChangeToExistentFileWithUnrelatedCreation()
        {
            const string FileName = @"FileToModify";
            const string OtherFileName = @"FileToIgnore";
            
            WriteFile(FileName, "Original");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(FileName);

            WriteFile(OtherFileName, "These changes are not relevant.");

            // No change notifications were expected; an unrelated file was changed.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDeletion()
        {
            const string FileNameA = @"FileToDelete";
            const string FileNameB = @"FileToLeave";
            
            WriteFile(FileNameA, "Original");
            WriteFile(FileNameB, "Original also");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(FileNameA);
            support.Track(FileNameB);

            Delete(FileNameA);

            // Expect FileNameA changed (deleted); FileNameB is a sibling but wasn't modified.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(FileNameA));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectFileRename()
        {
            const string FileNameOriginal = @"Before";
            const string FileNameFinal = @"After";
            
            WriteFile(FileNameOriginal, "Original");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(FileNameOriginal);

            Rename(FileNameOriginal, FileNameFinal);

            // We didn't track a path at the rename destination, so expect an effective deletion for the source.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(FileNameOriginal));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDirectoryRename()
        {
            const string DirectoryNameOriginal = @"D";
            const string DirectoryNameFinal = @"D2";
            const string FileNameA = @"D\A";
            const string FileNameB = @"D\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryNameOriginal);
            WriteFile(FileNameA, "A");
            support.Track(FileNameA);
            WriteFile(FileNameB, "B");
            support.Track(FileNameB);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            RenameDirectory(DirectoryNameOriginal, DirectoryNameFinal);

            // Expect changes for D, D\A, and D\B.
            // TODO: Consumers may want to opt-in to the notification for D.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                Removed(FileNameA),
                Removed(FileNameB),
                Removed(DirectoryNameOriginal));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDirectoryRenamesWithNameReuse()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string DirectoryName3 = @"D3";

            const string FileNameA = @"D1\A";
            const string FileNameB = @"D1\B";
            const string FileNameC = @"D1\C";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            WriteFile(FileNameA, "A");
            support.Track(FileNameA);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            RenameDirectory(DirectoryName1, DirectoryName2);

            CreateDirectory(DirectoryName1);
            WriteFile(FileNameB, "B");
            support.Track(FileNameB);

            RenameDirectory(DirectoryName2, DirectoryName3);
            RenameDirectory(DirectoryName1, DirectoryName2);

            CreateDirectory(DirectoryName1);
            WriteFile(FileNameC, "C");
            support.Track(FileNameC);

            // Everything changed. We could maybe notice that C was tracked after all the renames, but we choose to not maintain
            // a mapping from path -> file-records (i.e., we don't know the min / max USN for D1\C).
            // TODO: Consumers may want to opt-in to the notification for D1.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                Removed(FileNameA),
                Removed(FileNameB),
                Removed(FileNameC),
                Removed(DirectoryName1));

            // Should be able to track C again, expecting the others are now totally irrelevant.
            support.Track(FileNameC);

            WriteFile(@"D3\A", "Data change");
            WriteFile(FileNameC, "Data change 2");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(FileNameC));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectDirectoryRenameWithDeepNestingFile()
        {
            const string DirectoryNameD1 = @"D1";
            const string DirectoryNameD1D2 = @"D1\D2";
            const string DirectoryNameD1New = @"D1-new";
            const string FileNameA = @"D1\D2\A";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryNameD1);
            CreateDirectory(DirectoryNameD1D2);
            WriteFile(FileNameA, "A");
            support.Track(FileNameA);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            RenameDirectory(DirectoryNameD1, DirectoryNameD1New);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                Removed(DirectoryNameD1),
                Removed(DirectoryNameD1D2),
                Removed(FileNameA));
        }

        #endregion

        #region Hardlink invalidations

        [FactIfSupported(requiresJournalScan: true)]
        public void IgnoreCreatedHardlinksInOtherDirectories()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            const string LinkC = @"D2\C";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            LinkIfSupportedOrAssertInconclusive(LinkB, LinkC);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DeleteHardlinksInDistinctDirectories()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            // Creating LinkB should be ignored, since we haven't tracked D2\ at all.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            support.Track(LinkB);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Delete(LinkB);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(LinkB));

            Delete(LinkA);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(LinkA));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ChangeDataOfHardlinksInDistinctDirectories()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);

            WriteFile(LinkA, "A");

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            support.Track(LinkA);
            support.Track(LinkB);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkB, "Updated");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(LinkA), DataOrMetadataChanged(LinkB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ChangeDataOfHardlinksInTheSameDirectoryButTrackTheFirstHardLinkBeforeCreatingTheSecondOne()
        {
            const string DirectoryName = @"D1";

            const string LinkA = @"D1\A";
            const string LinkB = @"D1\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);
            support.Track(LinkB);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkB, "Updated");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(LinkA), DataOrMetadataChanged(LinkB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ChangeDataViaUntrackedHardlink()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);

            WriteFile(LinkA, "A");

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            support.Track(LinkA);

            // Note that we don't track LinkB at all, but still should notice a data change for LinkA

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkB, "Updated");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(LinkA));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DeleteHardlinksInSameDirectory()
        {
            const string DirectoryName = @"D1";

            const string LinkA = @"D1\A";
            const string LinkB = @"D1\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            support.Track(LinkB);

            // Creating LinkB unfortunately is ambiguous since we just see 'HardlinkChanged'.
            // Note that we don't invalidate LinkB since the HardlinkChanged USN is equal to the USN in the tracking record.
            DetectedChanges changedPaths = support.ProcessChanges();

            // If content of LinkA doesn't change, then we don't need to invalidate LinkA.
            changedPaths.AssertNoChangesDetected();

            // No need to explicitly re-track A because it's re-tracked automatically if nothing has changed.
            // support.Track(LinkA);

            // Deleting LinkB is ambiguous for the same reason.
            Delete(LinkB);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(LinkB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void CreateTwoHardlinksShouldRetrackTheFirstLink()
        {
            const string DirectoryName = @"D1";

            const string LinkA = @"D1\A";
            const string LinkB = @"D1\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            support.Track(LinkB);

            // Creating LinkB unfortunately is ambiguous since we just see 'HardlinkChanged'.
            // Note that we don't invalidate LinkB since the HardlinkChanged USN is equal to the USN in the tracking record.
            // However, LinkA gets invalidated although nothing has changed on LinkA.-
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // No need to explicitly re-track A because it's re-tracked automatically if nothing has changed.
            // support.Track(LinkA);

            // Update data.
            WriteFile(LinkB, "Updated");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(LinkA), DataOrMetadataChanged(LinkB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void RenameHardlink()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            const string LinkBFinal = @"D1\B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);

            WriteFile(LinkA, "A");

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            support.Track(LinkA);
            support.Track(LinkB);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Rename(LinkB, LinkBFinal);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(LinkB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DeleteAllHardlinks()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string DirectoryName3 = @"D3";
            const string DirectoryName4 = @"D4";

            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";
            const string LinkC = @"D3\C";
            const string LinkD = @"D4\D";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);
            CreateDirectory(DirectoryName3);
            CreateDirectory(DirectoryName4);

            WriteFile(LinkA, "A");
            support.Track(LinkA);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);
            support.Track(LinkB);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkC);
            support.Track(LinkC);

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkD);
            support.Track(LinkD);
            
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Delete(LinkA);
            Delete(LinkB);
            Delete(LinkC);

            // This causes D4\D to have FileDelete instead of HardLink change,
            // and repros Bug #1189498.
            DeleteDirectory(DirectoryName4);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(DirectoryName4), Removed(LinkA), Removed(LinkB), Removed(LinkC), Removed(LinkD));
        }

        #endregion

        #region Symbolic link invalidation

        [FactIfSupported(requiresJournalScan: true, requiresSymlinkPermission: true)]
        public void IgnoreNewlyIntroducedSymbolicLinkTargetFromOrigin()
        {
            const string LinkFrom = "From";
            const string LinkTarget = "Target";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            SymbolicLinkOrAssertInconclusive(LinkFrom, LinkTarget);
            support.Track(LinkFrom);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, LinkTarget);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkTarget, "Target");
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(LinkTarget));
        }

        [FactIfSupported(requiresJournalScan: true, requiresSymlinkPermission: true)]
        public void IgnoreNewlyIntroducedSymbolicLinkTargetFromOriginWithSymLinkProbe()
        {
            const string LinkFrom = "From";
            const string LinkTarget = "Target";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            SymbolicLinkOrAssertInconclusive(LinkFrom, LinkTarget);
            support.ProbeAndTrackPath(PathExistence.ExistsAsFile, LinkFrom);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, LinkTarget);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkTarget, "Target");
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(LinkTarget));
        }

        [FactIfSupported(requiresJournalScan: true, requiresSymlinkPermission: true)]
        public void IgnoreDataChangeSymbolicLinkTargetFromOrigin()
        {
            const string LinkFrom = "From";
            const string LinkTarget = "Target";

            WriteFile(LinkTarget, "Target1");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            SymbolicLinkOrAssertInconclusive(LinkFrom, LinkTarget);
            support.Track(LinkFrom);
            support.Track(LinkTarget);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(LinkTarget, "Target2");
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(LinkTarget));
        }

        #endregion

        #region Junction invalidation

        [FactIfSupported(requiresJournalScan: true)]
        public void TrackFileViaJunction()
        {
            const string TargetFile = @"Target\file";
            const string JunctionFile = @"Junction\file";

            CreateDirectory("Target");
            WriteFile(TargetFile, "Content1");

            // Create junction: Junction -> Target
            CreateDirectory("Junction");
            CreateJunction("Junction", "Target");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(JunctionFile);

            // Modify target file directly.
            WriteFile(TargetFile, "Content2");

            DetectedChanges changedPaths = support.ProcessChanges();

            // Junction file change should be detected.
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(JunctionFile));

            support.Track(JunctionFile);

            // Modify target file through junction.
            WriteFile(JunctionFile, "Content3");

            // Junction file change should be detected.
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(JunctionFile));
        }

        [TheoryIfSupported(requiresJournalScan: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void ModifyJunctionRemoveAllTrackedFilesUnderTheJunction(bool deleteAndRecreateJunction)
        {
            const string TargetFile11 = @"Target1\file1";
            const string TargetFile12 = @"Target1\file2";
            const string JunctionFile1 = @"Junction\file1";
            const string JunctionFile2 = @"Junction\file2";

            CreateDirectory("Target1");
            WriteFile(TargetFile11, "Content11");
            WriteFile(TargetFile12, "Content12");

            // Create junction: Junction -> Target1
            CreateDirectory("Junction");
            CreateJunction("Junction", "Target1");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.Track(JunctionFile1);
            support.Track(JunctionFile2);

            const string TargetFile21 = @"Target2\file1";
            const string TargetFile22 = @"Target2\file2";

            CreateDirectory("Target2");
            WriteFile(TargetFile21, "Content21");
            WriteFile(TargetFile22, "Content22");

            // Change junction: Junction -> Target2

            if (deleteAndRecreateJunction)
            {
                // This test scenario with mklink where, in order to change junction target,
                // one needs to delete and re-create junction.
                DeleteDirectory("Junction");
                CreateDirectory("Junction");
            }

            CreateJunction("Junction", "Target2");

            DetectedChanges changedPaths = support.ProcessChanges();

            changedPaths.AssertChangedExactly(
                deleteAndRecreateJunction ? Removed("Junction") : DataOrMetadataChanged("Junction"), 
                Removed(JunctionFile1), 
                Removed(JunctionFile2));
        }

        /// <summary>
        /// This test shows a limitation of file change tracker on tracking files via junctions.
        /// The limitation is the mapping from file ids to paths are not affected even though junction target changes.
        /// This limitation can create an overbuild.
        /// </summary>
        [FactIfSupported(requiresJournalScan: true)]
        public void FileTrackViaJunctionIsStickyAndCanCauseOverbuild()
        {
            const string TargetFile1 = @"Target1\file";
            const string TargetFile2 = @"Target2\file";
            const string JunctionFile = @"Junction\file";

            CreateDirectory("Target1");
            WriteFile(TargetFile1, "Content1");

            CreateDirectory("Target2");
            WriteFile(TargetFile2, "Content2");

            // Create junction: Junction -> Target1
            CreateDirectory("Junction");
            CreateJunction("Junction", "Target1");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            
            // Tracking junction file creates a mapping in the tracker: FileId(Target1\file) -> Path(Junction\file).
            support.Track(JunctionFile);

            // Change junction: Junction -> Target2
            CreateJunction("Junction", "Target2");

            DetectedChanges changedPaths = support.ProcessChanges();

            // Although Path(Junction\file) is marked removed, but the above mapping is still sticky in the tracker.
            changedPaths.AssertChangedExactly(DataOrMetadataChanged("Junction"), Removed(JunctionFile));

            // Tracking junction file adds a mapping in the tracker: FileId(Target2\file) -> Path(Junction\file).
            support.Track(JunctionFile);

            // Modify Target1\file.
            WriteFile(TargetFile1, "Content1-new");

            changedPaths = support.ProcessChanges();

            // As a result of 'FileId(Target1\file) -> Path(Junction\file)' being sticky, Junction\file is marked changed.
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(JunctionFile));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void TrackerDoesNotTrackAllPossibleJunctionIncarnationPaths()
        {
            const string TargetFile1 = @"Target1\file";
            const string TargetFile2 = @"Target2\file";
            const string JunctionFile1 = @"Junction1\file";

            CreateDirectory("Target1");
            WriteFile(TargetFile1, "Content1");

            CreateDirectory("Target2");
            WriteFile(TargetFile2, "Content2");

            // Create junction: Junction1 -> Junction2 -> Target1
            CreateDirectory("Junction1");
            CreateDirectory("Junction2");
            CreateJunction("Junction2", "Target1");
            CreateJunction("Junction1", "Junction2");

            // Now Target1\file has two incarnations, Junction1\file and Junction2\file.
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            // Just track Junction1\file.
            support.Track(JunctionFile1);

            // Change junction: Junction2 -> Target2
            CreateJunction("Junction2", "Target2");

            // No changes detected because Junction2\file is not tracked.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void TrackFileViaJunctionAcrossVolumes()
        {
            string uniqueFolderName = Path.Combine(nameof(TrackFileViaJunctionAcrossVolumes), Guid.NewGuid().ToString());
            string tempFolder = Path.Combine(SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            string junctionSubdir = @"Junction\Subdir";
            string junctionFile = Path.Combine(junctionSubdir, "file");

            CreateDirectory("Junction");
            
            if (!TryGetVolumeSerial(GetFullPath("Junction"), out ulong junctionSerial)
                || !TryGetVolumeSerial(tempFolder, out ulong tempFolderSerial)
                || junctionSerial == tempFolderSerial)
            {
                // If we are not able to determine that the volume serials of junctions and temp folder
                // are different, then skip the test.
                // Note that this is best effort in the sense that we assume LocalApplicationData is not
                // in the same volume as BuildXL object folder, as it is often the case. A proper way is
                // to use VHD, but it is overkill for a unit test.
                return;
            }

            string uniqueFolder = Path.Combine(tempFolder, uniqueFolderName);
            string targetFolder1 = Path.Combine(uniqueFolder, "Target1");
            string targetFolder2 = Path.Combine(uniqueFolder, "Target2");
            string subTargetFolder1 = Path.Combine(targetFolder1, "Subdir");
            string subTargetFolder2 = Path.Combine(targetFolder2, "Subdir");

            // Create TempFolder\GUID\Target1\Subdir and TempFolder\GUID\Target2\Subdir.
            Directory.CreateDirectory(subTargetFolder1);
            Directory.CreateDirectory(subTargetFolder2);

            // Write file TempFolder\GUID\Target1\Subdir\file and TempFolder\GUID\Target2\Subdir\file.
            File.WriteAllText(Path.Combine(subTargetFolder1, "file"), Guid.NewGuid().ToString());
            File.WriteAllText(Path.Combine(subTargetFolder2, "file"), Guid.NewGuid().ToString());

            // Create junction: Junction -> TempFolder\GUID\Target1
            FileUtilities.CreateJunction(GetFullPath("Junction"), targetFolder1);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            // Track file TempFolder\GUID\Target1\Subdir\file via junction Junction\Subdir\file.
            support.Track(junctionFile);

            // Change junction: Junction -> TempFolder\GUID\Target2.
            FileUtilities.CreateJunction(GetFullPath("Junction"), targetFolder2);

            DetectedChanges changedPaths = support.ProcessChanges();

            changedPaths.AssertChangedExactly(
                DataOrMetadataChanged("Junction"), 
                Removed(junctionSubdir),
                Removed(junctionFile));

            FileUtilities.DeleteDirectoryContents(uniqueFolder, deleteRootDirectory: true);
        }

        #endregion

        #region Probing (anti-dependency) invalidation

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeExistentFile()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            WriteFile(File, "A");

            support.ProbeAndTrackPath(PathExistence.ExistsAsFile, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from ExistsAsFile -> Nonexistent
            Delete(File);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeExistentDirectory()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D2";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(Directory1);

            support.ProbeAndTrackPath(PathExistence.ExistsAsDirectory, Directory1);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from ExistsAsDirectory -> Nonexistent
            RenameDirectory(Directory1, Directory2);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(Directory1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeNonexistentPath()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeNonexistentPathDueToNonexistentVolume()
        {
            string nonExistentVolume = GetNonExistentVolume();

            if (string.IsNullOrEmpty(nonExistentVolume))
            {
                XAssert.Fail(@"This test requires a non-existent volume, but all volumes, from a:\ to z:\ exist in this machine");
            }

            // ReSharper disable once AssignNullToNotNullAttribute
            string fileInNonExistentVolume = Path.Combine(nonExistentVolume, @"Path\To\File");
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, fileInNonExistentVolume);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeForNonexistentPathIsNotInvalidatedDueToSiblingAddition()
        {
            const string FileA = @"A";
            const string FileB = @"B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, FileA);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Should be ignored (should re-track absence of FileA when double-checking)
            WriteFile(FileB, "Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Re-tracking should ensure that eventual existence of FileA still triggers.
            WriteFile(FileA, "Re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(FileA));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeForNonexistentPathIsNotInvalidatedIfRemovedAgain()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");

            // Transition from ExistsAsFile -> Nonexistent
            Delete(File);

            // Existence of File should be double-checked (detected as possibly newly present, but then suppressed and re-tracked)
            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Re-tracking should ensure that eventual existence still triggers.
            WriteFile(File, "Re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));
        }

        [Fact(Skip ="TODO: Need to fix it")]
        public void ProbeForNonexistentPathIsNotRetrackedIfExistent()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");

            // Existence of File should be double-checked, but that shouldn't re-track the existent file.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));

            WriteFile(File, "Re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeNonexistentPathLaterTracked()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");
            support.Track(File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));

            // No need for re-tracking, since we haven't had an invalidation
            WriteFile(File, "Re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeNonexistentPathLaterTrackedInOneScan()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");
            support.Track(File);
            WriteFile(File, "Re-Hello");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File), DataOrMetadataChanged(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbeForNonexistentPathLaterTrackedIsInvalidatedEvenIfRemovedAgain()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Hello");
            support.Track(File);

            // Transition from ExistsAsFile -> Nonexistent
            Delete(File);

            // Deletion should invalidate the tracking (post-Hello); absence was re-tracked due to double-checking.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(File));

            // Transition from Nonexistent -> ExistsAsFile
            WriteFile(File, "Re-Hello");

            // Absence should be re-tracked, meaning we can invalidate it still.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(File));

            // Absence should be re-tracked, meaning we can invalidate it still.
            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeViaDirectoryRename()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D2";
            const string File = @"D1\F";
            const string Probe = @"D2\F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(Directory1);
            WriteFile(File, "Contents");

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe);

            // Renamed D1\F -> D2\F so now the probe has been invalidated (D2 and D2\F exist now)
            RenameDirectory(Directory1, Directory2);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory2), NewlyPresentAsFile(Probe));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void CreationInAncestorDirectoryDoesNotInvalidateNonexistentProbe()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D1\D2";
            const string ExtraFile = @"D1\F";
            const string Probe = @"D1\D2\F";
            
            CreateDirectory(Directory1);
            CreateDirectory(Directory2);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe);

            // Writing into D1\ should not invalidate D1\D2\F since D1\D2 exists (should not recurse through existent children when generating the invalidations).
            WriteFile(ExtraFile, "Extra");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeMultiLevel()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D1\D2";
            const string Probe = @"D1\D2\F";
            
            CreateDirectory(Directory1);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe);

            // We should be able to create D2 and F, getting invalidations for both.
            CreateDirectory(Directory2);
            WriteFile(Probe, "Now existent");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory2), NewlyPresentAsFile(Probe));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeMultiLevelSeparateScans()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D1\D2";
            const string Probe = @"D1\D2\F";
            
            CreateDirectory(Directory1);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe);

            // We should be able to create D2 and F, getting invalidations for both.
            CreateDirectory(Directory2);

            // Directory2 created, but Probe still absent. Absent-ness should be preserved despite the notification for Directory2.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory2));

            WriteFile(Probe, "Now existent");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsFile(Probe));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeMultiLevelViaDirectoryRename()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D1\D2";
            const string Directory2Temp = @"D2Temp";
            const string Directory2TempFile = @"D2Temp\F";
            const string Probe = @"D1\D2\F";
            
            CreateDirectory(Directory1);
            CreateDirectory(Directory2Temp);
            WriteFile(Directory2TempFile, "Moves via parent rename");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe);

            // A single rename event causes D1\D2 and D1\D2\F to appear. Should invalidate the tree of anti-dependencies.
            RenameDirectory(Directory2Temp, Directory2);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory2), NewlyPresentAsFile(Probe));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeMultiLevelViaSiblingDirectoryCreation()
        {
            const string Directory = @"D1";
            const string Probe1 = @"D1\D2\F";
            const string Probe2 = @"D1\D3";
            
            CreateDirectory(Directory);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe1);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, Probe2);

            CreateDirectory(Probe2);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Probe2));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void InvalidateNonexistentProbeMultiLevelViaDescendantCreations()
        {
            const string DirectoryA = @"A";
            const string DirectoryAB = @"A\B";
            const string DirectoryABC = @"A\B\C";

            CreateDirectory(DirectoryA);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, DirectoryABC);

            // Note that we do not track A\B explicitly, but on tracking A\B\C, we traverse the prefix path until we find an existing prefix.
            // The suffixes are all marked absent. Thus, we implicitly track A\B.
            CreateDirectory(DirectoryAB);

            // A\B should be re-tracked as existent by the following process changes as part of tracking A\B\C as non-existent.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(DirectoryAB));

            CreateDirectory(DirectoryABC);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(DirectoryABC));
        }

        /// <summary>
        /// Test for BUG1210030.
        /// </summary>
        [FactIfSupported(requiresJournalScan: true)]
        public void ChildDirectoryCreationDoesNotMakeParentMarkedAbsent()
        {
            const string DirectoryA = @"A";
            const string DirectoryAB = @"A\B";
            const string DirectoryABCD = @"A\B\C\D";

            CreateDirectory(DirectoryA);
            CreateDirectory(DirectoryAB);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.ProbeAndTrackPath(PathExistence.Nonexistent, DirectoryABCD);

            const string DirectoryABE = @"A\B\E";
            CreateDirectory(DirectoryABE);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            const string DirectoryAF = @"A\F";
            CreateDirectory(DirectoryAF);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        /// <summary>
        /// Test for BUG1191543.
        /// </summary>
        [FactIfSupported(requiresJournalScan: true)]
        public void CreatingFileWhoseParentHasAbsentSiblingDoesNotInvalidateTheAbsenceOfThatSibling()
        {
            const string Directory1 = @"Dir1";
            const string Directory1A = @"Dir1\A";
            const string Directory1B = @"Dir1\B";
            const string File1AF = @"Dir1\A\F";
            const string File1BF = @"Dir1\B\F";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.ProbeAndTrackPath(PathExistence.Nonexistent, File1AF);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, File1BF);

            WriteFile(File1AF, "Hello");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory1), NewlyPresentAsDirectory(Directory1A), NewlyPresentAsFile(File1AF));

            WriteFile(File1BF, "Hello");

            // In the original bug, the parent directory, Directory1B, becomes untracked such that changes to the directory go unnoticed
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(NewlyPresentAsDirectory(Directory1B), NewlyPresentAsFile(File1BF));
        }

        #endregion

        #region Membership (enumeration) invalidation

        [FactIfSupported(requiresJournalScan: true)]
        public void DeletionChangesMembership()
        {
            const string DirectoryName1 = @"D1";
            const string File = @"D1\A";

            CreateDirectory(DirectoryName1);
            WriteFile(File, "A");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1, File);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Delete(File);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(MembershipChanged(DirectoryName1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void CreationBeforeTrackingDoesNotChangeMembership()
        {
            const string DirectoryName1 = @"D1";
            const string File = @"D1\A";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            WriteFile(File, "A");

            support.TrackDirectoryMembership(DirectoryName1, File);

            DetectedChanges changedPaths = support.ProcessChanges();

            // The journal scan will see the creation of File, but we suppress this when double-checking (fingerprint matches)
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void CreationChangesMembership()
        {
            const string DirectoryName1 = @"D1";
            const string File = @"D1\A";

            CreateDirectory(DirectoryName1);

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(File, "A");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(MembershipChanged(DirectoryName1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void RenameAwayChangesMembership()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string FileOriginal = @"D1\A";
            const string FileFinal = @"D2\A";

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);
            WriteFile(FileOriginal, "A");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1, FileOriginal);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Rename(FileOriginal, FileFinal);
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(MembershipChanged(DirectoryName1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void RenameAwayChangesMembershipInSourceAndTargetDirectories()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string FileOriginal = @"D1\A";
            const string FileFinal = @"D2\A";

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);
            WriteFile(FileOriginal, "A");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1, FileOriginal);
            support.TrackDirectoryMembership(DirectoryName2);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            Rename(FileOriginal, FileFinal);
            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                MembershipChanged(DirectoryName1),
                MembershipChanged(DirectoryName2));

            support.TrackDirectoryMembership(DirectoryName1);
            support.TrackDirectoryMembership(DirectoryName2, FileFinal);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void HardlinkCreationDoesNotChangeMembershipInUnrelatedDirectory()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);
            WriteFile(LinkA, "A");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1, LinkA);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void HardlinkCreationChangesMembershipInTargetDirectory()
        {
            const string DirectoryName1 = @"D1";
            const string DirectoryName2 = @"D2";
            const string LinkA = @"D1\A";
            const string LinkB = @"D2\B";

            CreateDirectory(DirectoryName1);
            CreateDirectory(DirectoryName2);
            WriteFile(LinkA, "A");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            support.TrackDirectoryMembership(DirectoryName1, LinkA);
            support.TrackDirectoryMembership(DirectoryName2);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            LinkIfSupportedOrAssertInconclusive(LinkA, LinkB);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(MembershipChanged(DirectoryName2));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void CreationThenDeletionCancelAndDoNotChangeMembership()
        {
            const string DirectoryName1 = @"D1";
            const string File = @"D1\A";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);

            support.TrackDirectoryMembership(DirectoryName1);

            WriteFile(File, "A");
            Delete(File);

            DetectedChanges changedPaths = support.ProcessChanges();

            // The journal scan will see the creation and deletion of File, but we suppress this when double-checking (fingerprint matches)
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DeletionThenCreationCancelAndDoNotChangeMembership()
        {
            const string DirectoryName1 = @"D1";
            const string File = @"D1\A";

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(DirectoryName1);
            WriteFile(File, "A");

            // Tracked as containing A
            support.TrackDirectoryMembership(DirectoryName1, File);

            // Then, some changes happen but eventually the membership set is the same (just A; content doesn't matter).
            Delete(File);
            WriteFile(File, "!!");

            DetectedChanges changedPaths = support.ProcessChanges();

            // The journal scan will see the creation and deletion of File, but we suppress this when double-checking (fingerprint matches)
            changedPaths.AssertNoChangesDetected();
        }

        #endregion

        #region Superseding updates (last tracker wins)

        [FactIfSupported(requiresJournalScan: true)]
        public void ChangesIgnoredDueToSupersedeRetracking()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            WriteFile(File, "Hello");
            support.Track(File);

            WriteFile(File, "Re-Hello");
            support.Track(File, TrackingUpdateMode.Supersede);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(File, "Re-re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void RecreationIgnoredDueToSupersedeRetracking()
        {
            const string File = @"F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            WriteFile(File, "Hello");
            support.Track(File);

            Delete(File);
            WriteFile(File, "Re-Hello");
            support.Track(File, TrackingUpdateMode.Supersede);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            WriteFile(File, "Re-re-Hello");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(File));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void SupersedeIsLinkSpecificUnderDataChange()
        {
            const string FileA = @"A";
            const string FileB = @"B";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            WriteFile(FileA, "Hello");
            LinkIfSupportedOrAssertInconclusive(FileA, FileB);

            support.Track(FileA);
            support.Track(FileB);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Invalidate content (all links changed); superseding one link should suppress changes for only that link.
            WriteFile(FileB, "Re-Hello");
            support.Track(FileA, TrackingUpdateMode.Supersede);

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(DataOrMetadataChanged(FileB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void SupersedeRetrackingNotEffectiveUnderDirectoryRename()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D2";
            const string File = @"D1\F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(Directory1);
            WriteFile(File, "Hello");
            support.Track(File);

            RenameDirectory(Directory1, Directory2);

            // Directory rename has happened, so we should be doomed to invalidate D1\F

            // Now, create a new D1\F and 'supersede'.
            CreateDirectory(Directory1);
            WriteFile(File, "Re-Hello");
            support.Track(File, TrackingUpdateMode.Supersede);

            // Despite trying to supersede, D1\F is still doomed due to the rename.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                Removed(File),
                Removed(Directory1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void SupersedeRetrackingNotEffectiveUnderDirectoryRenameContainingAbsenceProbe()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D2";
            const string File = @"D1\F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(Directory1);
            support.ProbeAndTrackPath(PathExistence.Nonexistent, File);

            RenameDirectory(Directory1, Directory2);

            // Now 'supersede' the directory path with a new file.
            WriteFile(Directory1, "Re-Hello");
            support.Track(Directory1, TrackingUpdateMode.Supersede);

            // Despite trying to supersede, D1\ is not supersede-able because of the anti-dependencies underneath it.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(Directory1));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void SupersedeRetrackingNotEffectiveOnEnumeratedDirectory()
        {
            const string Directory1 = @"D1";
            const string Directory2 = @"D2";
            const string File = @"D1\F";
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            CreateDirectory(Directory1);
            WriteFile(File, "Stuff");
            support.TrackDirectoryMembership(Directory1, File);

            RenameDirectory(Directory1, Directory2);

            // Now 'supersede' the directory path with a new file.
            WriteFile(Directory1, "Re-Hello");
            support.Track(Directory1, TrackingUpdateMode.Supersede);

            // Despite trying to supersede, membership should still be invalidated.
            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(Removed(Directory1), MembershipChanged(Directory1));
        }

        #endregion

        #region Mixed scenario

        [FactIfSupported(requiresJournalScan: true)]
        public void DeletionExistingMemberFollowedByAdditionNewMemberThatWasProbeNonExistentShouldNotifyNewlyPresent()
        {
            const string Directory = "D1";
            const string NestedExistingDirectory = @"D1\D2";
            const string NestedExistingFile = @"D1\D2\A";
            const string NestedNonExistingDirectory = @"D1\D3";
            const string NestedNonExistingFile = @"D1\D3\B";

            // Setup.
            CreateDirectory(Directory);
            CreateDirectory(NestedExistingDirectory);
            WriteFile(NestedExistingFile, "A");
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            // Track membership of D1.
            support.TrackDirectoryMembership(Directory, NestedExistingDirectory);

            // Probe non-existing D1\D3\B.
            support.ProbeAndTrackPath(PathExistence.Nonexistent, NestedNonExistingFile);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Delete D1\D2, followed by creating D1\D3\B
            DeleteDirectory(NestedExistingDirectory);
            CreateDirectory(NestedNonExistingDirectory);
            WriteFile(NestedNonExistingFile, "B");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                MembershipChanged(Directory),
                NewlyPresentAsDirectory(NestedNonExistingDirectory),
                NewlyPresentAsFile(NestedNonExistingFile));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void RenameExistingMemberFollowedByAdditionNewMemberThatWasProbeNonExistentShouldNotifyNewlyPresent()
        {
            const string Directory = "D1";
            const string NestedExistingDirectory = @"D1\D2";
            const string NestedExistingFile = @"D1\D2\A";
            const string NestedExistingDirectoryNew = @"D1\D4";
            const string NestedNonExistingDirectory = @"D1\D3";
            const string NestedNonExistingFile = @"D1\D3\B";

            // Setup.
            CreateDirectory(Directory);
            CreateDirectory(NestedExistingDirectory);
            WriteFile(NestedExistingFile, "A");
            
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            // Track membership of D1.
            support.TrackDirectoryMembership(Directory, NestedExistingDirectory);

            // Probe non-existing D1\D3\B.
            support.ProbeAndTrackPath(PathExistence.Nonexistent, NestedNonExistingFile);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();

            // Rename D1\D2 to D1\D4, followed by creating D1\D3\B
            RenameDirectory(NestedExistingDirectory, NestedExistingDirectoryNew);
            CreateDirectory(NestedNonExistingDirectory);
            WriteFile(NestedNonExistingFile, "B");

            changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                MembershipChanged(Directory),
                NewlyPresentAsDirectory(NestedNonExistingDirectory),
                NewlyPresentAsFile(NestedNonExistingFile));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void ProbingAndTrackingPathsUnderFilePath()
        {
            WriteFile(@"D", "D is a file");
            ChangeDetectionSupport support = InitializeChangeDetectionSupport();
            support.ProbeAndTrackPath(PathExistence.Nonexistent, @"D\f");

            WriteFile(@"D", "D is still a file");
            support.ProbeAndTrackPath(PathExistence.Nonexistent, @"D\g");

            support.Track(@"D", TrackingUpdateMode.Supersede);

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertNoChangesDetected();
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void DetectAbsentPathCreationAfterParentIsDeleted()
        {
            CreateDirectory(@"D\E");

            ChangeDetectionSupport support = InitializeChangeDetectionSupport();

            // Track absent path D\E\f.
            support.ProbeAndTrackPath(PathExistence.Nonexistent, @"D\E\f");

            // Delete D\E.
            DeleteDirectory(@"D\E");

            // Write file to D\E\f.
            WriteFile(@"D\E\f", "test");

            DetectedChanges changedPaths = support.ProcessChanges();
            changedPaths.AssertChangedExactly(
                NewlyPresentAsFile(@"D\E\f"),
                Removed(@"D\E"));
        }

        #endregion

        private Tuple<PathChanges, string> DataOrMetadataChanged(string relative)
        {
            return Change(PathChanges.DataOrMetadataChanged, relative);
        }

        private Tuple<PathChanges, string> MembershipChanged(string relative)
        {
            return Change(PathChanges.MembershipChanged, relative);
        }

        private Tuple<PathChanges, string> Removed(string relative)
        {
            return Change(PathChanges.Removed, relative);
        }

        private Tuple<PathChanges, string> NewlyPresentAsFile(string relative)
        {
            return Change(PathChanges.NewlyPresentAsFile, relative);
        }

        private Tuple<PathChanges, string> NewlyPresentAsDirectory(string relative)
        {
            return Change(PathChanges.NewlyPresentAsDirectory, relative);
        }

        private Tuple<PathChanges, string> Change(PathChanges change, string relative)
        {
            return Tuple.Create(change, relative);
        }

        private void Delete(string relative)
        {
            string fullPath = GetFullPath(relative);
            FileUtilities.DeleteFile(fullPath);
        }

        private void Rename(string relativeFrom, string relativeTo)
        {
            File.Move(GetFullPath(relativeFrom), GetFullPath(relativeTo));
        }

        private void RenameDirectory(string relativeFrom, string relativeTo)
        {
            Directory.Move(GetFullPath(relativeFrom), GetFullPath(relativeTo));
        }

        private void CreateDirectory(string relative)
        {
            Directory.CreateDirectory(GetFullPath(relative));
        }

        private void DeleteDirectory(string relative)
        {
            FileUtilities.DeleteDirectoryContents(GetFullPath(relative), true);
        }

        private void LinkIfSupportedOrAssertInconclusive(string relativeFrom, string relativeTo)
        {
            CreateHardLinkStatus status = FileUtilities.TryCreateHardLink(link: GetFullPath(relativeTo), linkTarget: GetFullPath(relativeFrom));

            if (status == CreateHardLinkStatus.Success)
            {
                return;
            }

            if (status == CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem)
            {
                // This was once Assert.Inconclusive, but xunit doesn't support that Assert. It could be dynamically skipped
                // (see FactIfSupportedAttribute) but since we have hardlinks turned on by default in our self-host build,
                // a dynamic check probably isn't necessary.
                //
                // Add the check if people start hitting this assert.
                XAssert.Fail("Filesystem does not support hardlinks");
            }
            else
            {
                XAssert.Fail($"Creating a hardlink failed unexpectedly: {status:G}");
            }

            throw new InvalidOperationException("Unreachable");
        }

        private void SymbolicLinkOrAssertInconclusive(string relativeFrom, string relativeTo, bool isTargetFile = true)
        {
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(
                symLinkFileName: GetFullPath(relativeFrom),
                targetFileName: GetFullPath(relativeTo),
                isTargetFile: isTargetFile));
        }

        private void CreateJunction(string relativeJunction, string relativeTarget)
        {
            FileUtilities.CreateJunction(GetFullPath(relativeJunction), GetFullPath(relativeTarget));
        }

        private static string GetNonExistentVolume()
        {
            for (char c = 'a'; c <= 'z'; ++c)
            {
                string volume = c + @":\";
                if (!Directory.Exists(volume))
                {
                    return volume;
                }
            }

            return null;
        }

        private static bool TryGetVolumeSerial(string path, out ulong volumeSerial)
        {
            volumeSerial = 0;

            OpenFileResult directoryOpenResult = FileUtilities.TryOpenDirectory(
                path,
                FileDesiredAccess.None,
                FileShare.ReadWrite | FileShare.Delete,
                FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out SafeFileHandle handle);

            if (!directoryOpenResult.Succeeded)
            {
                return false;
            }

            using (handle)
            {
                var maybeIdentity = VersionedFileIdentity.TryQuery(handle);

                if (!maybeIdentity.Succeeded)
                {
                    return false;
                }

                volumeSerial = maybeIdentity.Result.VolumeSerialNumber;
            }

            return true;
        }

        private class ChangeDetectionSupport
        {
            private readonly JournalState m_journalState;
            private readonly FileChangeTrackingSet m_changeTrackingSet;
            private readonly string m_temporaryDirectory;

            public ChangeDetectionSupport(
                string temporaryDirectory,
                JournalState journalState,
                FileChangeTrackingSet trackingSet)
            {
                m_journalState = journalState;
                m_changeTrackingSet = trackingSet;
                m_temporaryDirectory = temporaryDirectory;
            }

            internal string GetFullPath(string relativePath)
            {
                return Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(m_temporaryDirectory, relativePath);
            }

            public DetectedChanges ProcessChanges()
            {
                var changedPathObserver = new ChangedPathsObserver();

                using (m_changeTrackingSet.Subscribe(changedPathObserver))
                {
                    var processedAllChanges = m_changeTrackingSet.TryProcessChanges(m_journalState.Journal, null);
                    XAssert.IsTrue(processedAllChanges.Succeeded, "Change processing gave up unexpectedly");
                }

                return DetectedChanges.Create(this, changedPathObserver.ChangedPaths);
            }

            public void ProbeAndTrackPath(PathExistence expectedExistence, string relative)
            {
                string path = GetFullPath(relative);

                Possible<FileChangeTrackingSet.ProbeResult> possiblyProbed = m_changeTrackingSet.TryProbeAndTrackPath(path);

                if (!possiblyProbed.Succeeded)
                {
                    XAssert.Fail(
                        "Failed to make a tracking existence probe (expecting {0}: {1})",
                        expectedExistence,
                        possiblyProbed.Failure.DescribeIncludingInnerFailures());
                }

                PathExistence actualExistence = possiblyProbed.Result.Existence;
                Possible<Unit> possiblyTracked = possiblyProbed.Result.PossibleTrackingResult;

                if (!possiblyTracked.Succeeded)
                {
                    XAssert.Fail(
                        "Existence probe succeeded ({1} ; expecting {2}), but tracking failed: {0}",
                        actualExistence,
                        expectedExistence,
                        possiblyTracked.Failure.DescribeIncludingInnerFailures());
                }

                XAssert.AreEqual(expectedExistence, actualExistence, $"Incorrect existence result from a probe to {path}");
            }

            public void Track(string relative, TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve)
            {
                string path = GetFullPath(relative);

                var openResult = FileUtilities.TryOpenDirectory(
                    path,
                    FileDesiredAccess.GenericRead,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                    out SafeFileHandle handle);

                using (handle)
                {
                    XAssert.IsTrue(openResult.Succeeded, "Failed to open file");

                    Possible<FileChangeTrackingSubscription> possiblyTracked = m_changeTrackingSet.TryTrackChangesToFile(
                        handle,
                        path,
                        updateMode: updateMode);

                    if (!possiblyTracked.Succeeded)
                    {
                        XAssert.Fail($"Failed to register the test file for change tracking: {possiblyTracked.Failure.DescribeIncludingInnerFailures()}");
                    }
                }
            }

            public void TrackDirectoryMembership(string relative, params string[] expectedMembers)
            {
                string path = GetFullPath(relative);

                var calculator = DirectoryMembershipTrackingFingerprint.CreateCalculator();
                var diff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Possible<FileChangeTrackingSet.EnumerationResult> possibleEnumeration = m_changeTrackingSet.TryEnumerateDirectoryAndTrackMembership(
                    path,
                    (entry, attributes) =>
                    {
                        diff.Add(Path.Combine(path, entry));
                        calculator = calculator.Accumulate(entry, attributes);
                    });

                var fingerprint = calculator.GetFingerprint();

                if (!possibleEnumeration.Succeeded)
                {
                    XAssert.Fail(
                        $"Failed to open and enumerate a directory to track its membership: {possibleEnumeration.Failure.DescribeIncludingInnerFailures()}");
                }

                FileChangeTrackingSet.EnumerationResult enumerationResult = possibleEnumeration.Result;

                XAssert.AreEqual(PathExistence.ExistsAsDirectory, enumerationResult.Existence, "Expected an existent directory");
                XAssert.AreEqual(fingerprint, enumerationResult.Fingerprint, "Incorrect fingerprint");

                if (!enumerationResult.PossibleTrackingResult.Succeeded)
                {
                    XAssert.Fail(
                        "Failed to register a directory for change tracking: {0}",
                        enumerationResult.PossibleTrackingResult.Failure.DescribeIncludingInnerFailures());
                }

                diff.SymmetricExceptWith(expectedMembers.Select(GetFullPath));

                if (diff.Count != 0)
                {
                    XAssert.Fail("Incorrect directory membership. Unexpected or missing paths:\r\n\t{0}", string.Join("\r\n\t", diff));
                }
            }
        }

        private class ChangedPathsObserver : IObserver<ChangedPathInfo>
        {
            public IEnumerable<Tuple<PathChanges, string>> ChangedPaths => m_changedPaths;

            private readonly List<Tuple<PathChanges, string>> m_changedPaths = new List<Tuple<PathChanges, string>>();

            public void OnNext(ChangedPathInfo value)
            {
                m_changedPaths.Add(Tuple.Create(value.PathChanges, value.Path));
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }

        private class DetectedChanges
        {
            private readonly Dictionary<string, PathChanges> m_changedPaths = new Dictionary<string, PathChanges>(StringComparer.OrdinalIgnoreCase);
            private readonly ChangeDetectionSupport m_support;

            private DetectedChanges(ChangeDetectionSupport support)
            {
                m_support = support;
            }

            public static DetectedChanges Create(ChangeDetectionSupport support, IEnumerable<Tuple<PathChanges, string>> emitted)
            {
                var result = new DetectedChanges(support);

                foreach (var changed in emitted)
                {
                    PathChanges existingChangeReason;
                    if (result.m_changedPaths.TryGetValue(changed.Item2, out existingChangeReason))
                    {
                        if ((existingChangeReason & changed.Item1) != 0)
                        {
                            XAssert.Fail(
                                "Duplicate path change reported (a particular path should get at most one of each change reason per scan): {0} (existing reasons {1}; new reasons {2})",
                                changed.Item2,
                                existingChangeReason,
                                changed.Item1);
                        }

                        result.m_changedPaths[changed.Item2] = existingChangeReason | changed.Item1;
                    }
                    else
                    {
                        result.m_changedPaths.Add(changed.Item2, changed.Item1);
                    }
                }

                return result;
            }

            public void AssertNoChangesDetected()
            {
                if (m_changedPaths.Count > 0)
                {
                    XAssert.Fail("Did not expect any changed paths. Reported: {0}", FormatChangedPaths());
                }
            }

            public void AssertChangedExactly(params Tuple<PathChanges, string>[] relativePaths)
            {
                Dictionary<string, PathChanges> expected = new Dictionary<string, PathChanges>(StringComparer.OrdinalIgnoreCase);
                foreach (Tuple<PathChanges, string> expectedChangeRelative in relativePaths)
                {
                    string fullPath = m_support.GetFullPath(expectedChangeRelative.Item2);
                    PathChanges existing;
                    expected.TryGetValue(fullPath, out existing);

                    expected[fullPath] = existing | expectedChangeRelative.Item1;
                }

                var errors = new List<string>();

                foreach (KeyValuePair<string, PathChanges> expectedChange in expected)
                {
                    PathChanges actual;
                    if (m_changedPaths.TryGetValue(expectedChange.Key, out actual))
                    {
                        PathChanges extraChanges = actual & ~expectedChange.Value;
                        PathChanges missingChanges = expectedChange.Value & ~actual;

                        if (extraChanges != PathChanges.None)
                        {
                            errors.Add(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Unexpected (extra) changes {3}:\t{0} ({1}) expecting ({2})",
                                    expectedChange.Key,
                                    actual,
                                    expectedChange.Value,
                                    extraChanges));
                        }

                        if (missingChanges != PathChanges.None)
                        {
                            errors.Add(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Missing (expected) changes {3}:\t{0} ({1}) expecting ({2})",
                                    expectedChange.Key,
                                    actual,
                                    expectedChange.Value,
                                    missingChanges));
                        }
                    }
                    else
                    {
                        errors.Add(string.Format(CultureInfo.InvariantCulture, "Missing path:\t{0} (expecting {1})", expectedChange.Key, expectedChange.Value));
                    }
                }

                foreach (KeyValuePair<string, PathChanges> actualChange in m_changedPaths)
                {
                    if (!expected.ContainsKey(actualChange.Key))
                    {
                        errors.Add(string.Format(CultureInfo.InvariantCulture, "Unexpected path changed:\t{0} ({1})", actualChange.Key, actualChange.Value));
                    }
                }

                if (errors.Count > 0)
                {
                    XAssert.Fail("Incorrect set of changes detected.\r\n{0}", string.Join("\r\n", errors));
                }
            }

            private string FormatChangedPaths()
            {
                string[] orderedPaths = m_changedPaths.Select(kvp => string.Format("{0} ({1})", kvp.Key, kvp.Value)).ToArray();

                Array.Sort(orderedPaths, StringComparer.OrdinalIgnoreCase);
                return string.Format(CultureInfo.InvariantCulture, "Actual changed paths:\r\n\t{0}", string.Join("\r\n\t", orderedPaths));
            }
        }

        private ChangeDetectionSupport InitializeChangeDetectionSupport()
        {
            var loggingContext = new LoggingContext("Dummy", "Dummy");
            VolumeMap volumeMap = JournalUtils.TryCreateMapOfAllLocalVolumes(loggingContext);
            XAssert.IsNotNull(volumeMap);

            var maybeJournal = JournalUtils.TryGetJournalAccessorForTest(volumeMap);
            XAssert.IsTrue(maybeJournal.Succeeded, "Could not connect to journal");

            FileChangeTrackingSet trackingSet = FileChangeTrackingSet.CreateForAllCapableVolumes(loggingContext, volumeMap, maybeJournal.Result);

            return new ChangeDetectionSupport(TemporaryDirectory, JournalState.CreateEnabledJournal(volumeMap, maybeJournal.Result), trackingSet);
        }
    }
}
