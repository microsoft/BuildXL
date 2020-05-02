// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage.ChangeTracking;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class BasicGitOperationTests : TestBase
    {
        public BasicGitOperationTests(ITestOutputHelper testOutput)
            : base(testOutput)
        {
        }

        public static IEnumerable<object[]> RepoConfigData()
        {
            // args: (RepoConfig repoCfg)
            yield return new object[] { new RepoConfig(RepoKind.Gvfs, RepoInit.Clone) };
            yield return new object[] { new RepoConfig(RepoKind.Gvfs, RepoInit.UseExisting) };
            yield return new object[] { new RepoConfig(RepoKind.Git, RepoInit.UseExisting) };
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void ChangeFile(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an existing file
            var file = helper.GetPath(@"src\files\changingFile.txt");
            XAssert.FileExists(file);
            var oldContent = File.ReadAllText(file);
            helper.TrackPath(file);

            // switch to a new branch where that file is modified
            using var reseter = helper.GitCheckout("changingFile1");

            // snap USN entries before doing any checks (because they can modify the journal too)
            helper.SnapCheckPoint();

            // assert that the file exists and has different content in the new branch
            XAssert.FileExists(file);
            XAssert.AreNotEqual(oldContent, File.ReadAllText(file));

            // assert that the journal recorded a change
            // (it doesn't matter to us whether that change is 'Delete' or 'Change')
            helper.AssertDeleteOrChangeFile(file);
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void ChangeFileWithMaterialization(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an existing file
            var file = helper.GetPath(@"src\files\changingFile.txt");
            var result = helper.TrackPath(file);
            XAssert.AreEqual(PathExistence.ExistsAsFile, result.Existence);
            var oldContent = File.ReadAllText(file);

            // switch to a new branch where that file is modified
            using var reseter = helper.GitCheckout("changingFile1");

            // materialize the file (by probing it) before snapping USN entries
            helper.AssertFileOnDisk(file);
            helper.SnapCheckPoint();

            // assert that the file exists and has different content in the new branch
            XAssert.FileExists(file);
            XAssert.AreNotEqual(oldContent, File.ReadAllText(file));

            // assert that the journal recorded a change
            // (it doesn't matter to us whether that change is 'Delete' or 'Change')
            helper.AssertDeleteOrChangeFile(file);
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void ChangeFileNoMaterialization(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an existing file
            var file = helper.GetPath(@"src\files\changingFile.txt");
            var result = helper.TrackPath(file);
            XAssert.AreEqual(PathExistence.ExistsAsFile, result.Existence);

            // switch to a new branch where that file is modified
            using var reseter = helper.GitCheckout("changingFile1");

            // immediately snap changes
            helper.SnapCheckPoint();

            // assert that the journal recorded a change
            // (it doesn't matter to us whether that change is 'Delete' or 'Change') 
            helper.AssertDeleteOrChangeFile(file);
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void NewFileWhenParentFolderIsNotMaterialzedBeforeOperation(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track a file that doesn't exist
            var file = helper.GetPath(@"src\files\subfolder\newfile2.txt");
            var result = helper.TrackPath(file);
            XAssert.AreEqual(PathExistence.Nonexistent, result.Existence);

            // switch to a new branch where that file does exist
            using var reseter = helper.GitCheckout("newFileInSubfolder");

            // immediately snap changes
            helper.SnapCheckPoint();

            if (repoCfg.RepoKind == RepoKind.Git)
            {
                // assert that 'CreateFile' USN entry was recorded
                helper.AssertCreateFile(file);
            }
            else
            {
                // because of GVFS lazy materialization no 'CreateFile' USN change is recorded;
                // instead, assert that the GVFS projection file (indicating that file projections changed) has changed
                helper.AssertDeleteOrChangeFile(helper.GetGvfsProjectionFilePath());
            }
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void NewFileWhenParentFolderIsMaterialzedBeforeOperation(RepoConfig repoCfg)
        {
            var helper = Clone(repoCfg);

            // track a file that doesn't exist
            var file = helper.GetPath(@"src\files\subfolder\newfile.txt");
            var file2 = helper.GetPath(@"src\files\subfolder\newfile2.txt");
            helper.TrackPath(file);
            helper.TrackPath(file2);

            // Operation (materializes parent folder but not files)
            helper.AssertFileOnDisk(file);
            helper.AssertFileOnDisk(file2, expectExists: false);
            using var reseter = helper.GitCheckout("newFileInSubfolder");

            helper.SnapCheckPoint();
            if (repoCfg.RepoKind == RepoKind.Git)
            {
                // this change is recorded ONLY when GVFS lazy materialization is NOT used
                helper.AssertCreateFile(file2);
            }
            else
            {
                // when GVFS lazy materialization is used, assert that GVFS projections changed
                helper.AssertDeleteOrChangeFile(helper.GetGvfsProjectionFilePath());
            }
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void NewFileWhenParentFolderIsMaterialzedAfterOperation(RepoConfig repoCfg)
        {
            using (var helper = Clone(repoCfg))
            {
                // track one existing and one absent file
                var file = helper.GetPath(@"src\files\subfolder\newfile.txt");
                var file2 = helper.GetPath(@"src\files\subfolder\newfile2.txt");
                var existingFileResult = helper.TrackPath(file);
                var absentFileResult = helper.TrackPath(file2);
                XAssert.AreEqual(PathExistence.ExistsAsFile, existingFileResult.Existence);
                XAssert.AreEqual(PathExistence.Nonexistent, absentFileResult.Existence);

                // switch to a new branch where absent file exists
                using var reseter = helper.GitCheckout("newFileInSubfolder");

                helper.AssertFileOnDisk(file);
                helper.AssertFileOnDisk(file2);

                helper.SnapCheckPoint();
                helper.AssertCreateFile(file2);
            }
        }

        [Fact]
        public void GvfsProjectionDoesNotChangeUponLocalCommit()
        {
            using var helper = Clone(new RepoConfig(RepoKind.Gvfs, RepoInit.UseExisting));

            helper.SnapCheckPoint();

            // create a new file
            var file = helper.GetPath(@"newLocalFile.txt");
            XAssert.FileDoesNotExist(file);
            TestOutput.WriteLine("Creating new file: " + file);
            File.WriteAllText(file, "hi");
            
            // commit to master; assert that GVFS projection hasn't changed
            helper.Git("add .");
            helper.Git("commit -m hi");
            helper.SnapCheckPoint();
            helper.AssertNoChange(helper.GetGvfsProjectionFilePath());

            // create a new local branch, modify the new file, and commit it; assert that GVFS projection hasn't changed
            var newBranchName = "tmp/new-local-branch";
            helper.Git($"checkout -b {newBranchName}");
            using var reseter = new RepoReseter(helper, newBranchName); // This guy will delete the newly created branch
            TestOutput.WriteLine("Modifying file: " + file);
            File.AppendAllText(file, "there");
            helper.Git("commit -am there");
            helper.SnapCheckPoint();
            helper.AssertNoChange(helper.GetGvfsProjectionFilePath());

            // delete the file, commit that as a new change; assert GVFS projection still hasn't changed
            TestOutput.WriteLine("Deleting file: " + file);
            helper.Git($"rm -f {file}");
            helper.Git("commit -am deleted");
            helper.SnapCheckPoint();
            XAssert.FileDoesNotExist(file);
            helper.AssertNoChange(helper.GetGvfsProjectionFilePath());
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void TestStashingAndUnstashing(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            var file = helper.GetPath(@"src\files\changingFile.txt");
            XAssert.FileExists(file);
            TestOutput.WriteLine("Modifying file: " + file);
            File.AppendAllText(file, "hi");
            var modifiedContent = File.ReadAllText(file);

            var result = helper.TrackPath(file);
            XAssert.AreEqual(PathExistence.ExistsAsFile, result.Existence);

            // stash changes, assert that 'Change' or 'Delete' USN entry was recorded
            helper.Git("stash");
            helper.SnapCheckPoint();
            XAssert.AreNotEqual(modifiedContent, File.ReadAllText(file));
            helper.AssertDeleteOrChangeFile(file);
            // unfortunately, GVFS projection seems to change even though it probably shoudn't
            // helper.AssertNoChange(helper.GetGvfsProjectionFilePath());

            // must re-track the same path because now it could be a different physical file
            helper.TrackPath(file);

            // unstash changes, assert that 'Change' or 'Delete' USN entry was recorded and that GVFS projection hasn't changed
            helper.Git("stash pop");
            helper.SnapCheckPoint();
            XAssert.AreEqual(modifiedContent, File.ReadAllText(file));
            helper.AssertDeleteOrChangeFile(file);
            // unfortunately, GVFS projection seems to change even though it probably shouldn't
            // helper.AssertNoChange(helper.GetGvfsProjectionFilePath());
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void CreateFile(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an absent file
            var filePath = helper.GetPath("a.txt");
            var result = helper.TrackPath(filePath);
            XAssert.AreEqual(PathExistence.Nonexistent, result.Existence);

            // create that file
            File.WriteAllText(filePath, "hi");

            // snap entries and assert that a 'CreateFile' entry is found
            helper.SnapCheckPoint();
            helper.AssertCreateFile(filePath);
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void EditFile(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an existing file
            var filePath = helper.GetPath(@"src\files\changingFile.txt");
            var result = helper.TrackPath(filePath);
            XAssert.AreEqual(PathExistence.ExistsAsFile, result.Existence);

            // modify that file
            File.AppendAllText(filePath, "new content");

            // snap changes and assert that a 'ChangeFile' USN entry was recorded
            helper.SnapCheckPoint();
            helper.AssertChangeFile(filePath);
        }

        [Theory]
        [MemberData(nameof(RepoConfigData))]
        public void DeleteFile(RepoConfig repoCfg)
        {
            using var helper = Clone(repoCfg);

            // track an existing file
            var filePath = helper.GetPath(@"src\files\changingFile.txt");
            var result = helper.TrackPath(filePath);
            XAssert.AreEqual(PathExistence.ExistsAsFile, result.Existence);

            // delete that file
            File.Delete(filePath);

            // snap changes and assert that a 'DeleteFile' USN entry was recorded
            helper.SnapCheckPoint();
            helper.AssertDeleteFile(filePath);
            XAssert.FileDoesNotExist(filePath);
        }
    }
}