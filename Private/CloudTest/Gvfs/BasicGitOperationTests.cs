// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Xunit;
using Xunit.Abstractions;
using GVFS.FunctionalTests.Tools;

namespace BuildXL.CloudTest.Gvfs
{
    // TODO: make this class public once there is at least one enabled test in it
    class BasicGvfsperationTests : TestBase
    {

        public BasicGvfsperationTests(ITestOutputHelper testOutput)
            : base(testOutput)
        {

        }

        [Fact(Skip="Gvfs Bug: The change file notification is flacky, not 100% reliable...")]
        public void ChangeFileWhereFolderIsNotMaterialized()
        {
            using (var helper = Clone())
            {
                // Setup
                var file = helper.GetPath(@"src\files\changingFile.txt");
                helper.TrackPath(file);

                // Operation (no materializtion)
                helper.GitCheckout("changingFile1");

                // Check
                helper.SnapCheckPoint();
                helper.AssertDeleteFile(file); // Gvfs causes a journal delete and a journal change
                helper.AssertChangeFile(file); // $$Bug: Gvfs should trigger this USN entry..
                helper.AssertFileContents(file, "VersionA");
            }
        }

        [Fact(Skip="Gvfs Bug: The change file notification is flacky, not 100% reliable...")]
        public void ChangeFileWhereFileNotButFolderIsMaterialized()
        {
            using (var helper = Clone())
            {
                // Setup
                var file = helper.GetPath(@"src\files\changingFile.txt");
                helper.TrackPath(file);

                // Operation (Materialize folder only)
                helper.AssertFileOnDisk(file);
                helper.GitCheckout("changingFile1");

                // Check
                helper.SnapCheckPoint();
                helper.AssertDeleteFile(file); // $$BUG: Gvfs causes a journal delete and a journal change
                helper.AssertChangeFile(file); // $$BUG: This does not always fire :(
                helper.AssertFileContents(file, "VersionA");
            }
        }

        [Fact(Skip="Gvfs Bug: The change file notification is flacky, not 100% reliable...")]
        public void ChangeFileWhereFileIsMaterialized()
        {
            using (var helper = Clone())
            {
                // Setup
                var file = helper.GetPath(@"src\files\changingFile.txt");

                // Operation (Materialize File)
                helper.AssertFileContents(file, "VersionB");
                helper.TrackPath(file);
                helper.GitCheckout("changingFile1");

                // Check
                helper.SnapCheckPoint();
                System.Diagnostics.Debugger.Launch();
                helper.AssertDeleteFile(file); // $$BUG: Gvfs causes a journal delete and a journal change
                helper.AssertChangeFile(file); // $$BUG: This does not always fire :(
                helper.AssertFileContents(file, "VersionA");
            }
        }

        [Fact(Skip = "Gvfs bug - Fires too many directory deleted notifications")]
        public void NewFileWhenParentFolderIsNotMaterialzedBeforeOperation()
        {
            using (var helper = Clone())
            {
                // Setup
                var file = helper.GetPath(@"src\files\subfolder\newfile2.txt");
                helper.TrackPath(file);

                // Operation
                helper.GitCheckout("newFileInSubfolder");

                // Check
                helper.SnapCheckPoint();
                helper.AssertCreateFile(file); // This step is failing

                // $$BUG: Gvfs fires lots of Folder Removed notifications and a metadatachange on the root
            }
        }

        [Fact(Skip = "Gvfs bug - Fires too many directory deleted notifications")]
        public void NewFileWhenParentFolderIsMaterialzedBeforeOperation()
        {
            using (var helper = Clone())
            {
                // Setup
                var file = helper.GetPath(@"src\files\subfolder\newfile.txt");
                var file2 = helper.GetPath(@"src\files\subfolder\newfile2.txt");
                helper.TrackPath(file);
                helper.TrackPath(file2);

                // Operation (materializes parent folder but not files)
                helper.AssertFileOnDisk(file);
                helper.AssertFileOnDisk(file2, expectExists: false);
                helper.GitCheckout("newFileInSubfolder");

                // Check
                helper.AssertFileOnDisk(file);
                helper.AssertFileOnDisk(file2);

                helper.SnapCheckPoint();
                helper.AssertCreateFile(file2);

                // $$BUG: Gvfs fires lots of Folder Removed notifications and a metadatachange on the root
            }
        }
    }
}