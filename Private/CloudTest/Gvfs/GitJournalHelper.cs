// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using GVFS.FunctionalTests;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    /// <summary>
    /// The Dispose() method resets the repo to master and deletes a previously created branch
    /// </summary>
    public sealed class RepoReseter : IDisposable
    {
        public GitJournalHelper Helper { get;  }
        public string BranchName { get; }

        public RepoReseter(GitJournalHelper helper, string branch)
        {
            Helper = helper;
            BranchName = branch;
        }

        public void Dispose()
        {
            Helper.ResetToMaster();
            Helper.Git("branch -D " + BranchName);
        }
    }

    public class GitJournalHelper : JournalHelper
    {
        private readonly string m_repo;
        private readonly string m_repoRoot;

        private  GVFSFunctionalTestEnlistment m_enlistment;
        
        public string RepoRoot => m_enlistment?.RepoRoot ?? m_repoRoot;

        private GitJournalHelper(ITestOutputHelper testOutput, string repo = null, string repoRoot = null)
            : base(testOutput)
        {
            m_repo = repo ?? "https://almili@dev.azure.com/almili/Public/_git/Public.BuildXL.Test.Gvfs";
            m_repoRoot = repoRoot;

            // Set values for the Gvfs test helpers
            Settings.Default.Initialize();
            Settings.Default.Commitish = @"master";
            Settings.Default.EnlistmentRoot = WorkingFolder;
            Settings.Default.PathToGVFS = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files",
                "GVFS", "gvfs.exe");
            Settings.Default.RepoToClone = m_repo;
            GVFSTestConfig.RepoToClone = m_repo;
            GVFSTestConfig.NoSharedCache = true;
            GVFSTestConfig.TestGVFSOnPath = true;
            GVFSTestConfig.DotGVFSRoot = ".gvfs";

            Assert.True(File.Exists(Settings.Default.PathToGVFS), "gvfs not found: " + Settings.Default.PathToGVFS);
            Assert.True(File.Exists(Settings.Default.PathToGit), "git not found: " + Settings.Default.PathToGit);
        }

        public override void Dispose()
        {
            ResetToMaster();
            base.Dispose();
        }

        public override string GetPath(string path)
        {
            return Path.Combine(RepoRoot, path);
        }

        /// <summary>
        /// This "GVFS_projection" tells us whether GVFS projection of the file system changes
        /// (GVFS is responsible of updating this file). Whenever this projection changes, that
        /// means that there could be some pending unmaterialized files.
        /// </summary>
        public string GetGvfsProjectionFilePath()
        {
            return Path.Combine(Path.GetDirectoryName(RepoRoot), ".gvfs", "GVFS_projection");
        }

        private void Clone(bool useGvfs, string commitish = null)
        {
            const string PathVarName = "Path";
            if (useGvfs)
            {
                // make sure PATH points to PathToGVFS beause otherwise GVFS.hooks.exe won't be found when executing gvfs.exe
                Assert.True(CurrentProcess.IsElevated, "Tests must run elevated!!");
                string currentPathValue = Environment.GetEnvironmentVariable(PathVarName) ?? "";
                var newPathValue = Path.GetDirectoryName(Settings.Default.PathToGVFS) + ";" + currentPathValue;
                Environment.SetEnvironmentVariable(PathVarName, newPathValue);

                m_enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(Settings.Default.PathToGVFS, skipPrefetch: true);
            }
            else
            {
                var repoName = Path.GetFileName(RepoRoot);
                var repoParentDirectory = Path.GetDirectoryName(RepoRoot);
                Git($"clone {m_repo} {repoName}", workingDirectory: repoParentDirectory);
            }
        }

        public override void SnapCheckPoint(int? nrOfExpectedOperations = null)
        {
            base.SnapCheckPoint(nrOfExpectedOperations);
            TrackGvfsProjectionFile();
        }

        public void TrackGvfsProjectionFile()
        {
            TrackPath(GetGvfsProjectionFilePath());
        }

        public void ResetToMaster()
        {
            Git("reset --hard");
            Git("clean -xfd");
            Git("checkout master");
            Git("reset --hard origin/master");
        }

        public static GitJournalHelper Clone(ITestOutputHelper testOutput, bool useGvfs, string repoUrl = null)
        {
            var repoRoot = useGvfs
                ? null // GVFSFunctionalTestEnlistment.CloneAndMount will create m_enlistment which will hold the root
                : Path.Combine(Path.GetTempPath(), $"BuildXL.Test.{Guid.NewGuid()}");
            var helper = new GitJournalHelper(testOutput, repoUrl, repoRoot);
            helper.Clone(useGvfs);
            return helper;
        }

        public static GitJournalHelper InitializeWithExistingRepo(ITestOutputHelper testOutput, string repoRoot)
        {
            var helper = new GitJournalHelper(testOutput, null, repoRoot);
            helper.ResetToMaster();
            return helper;
        }

        public void Git(string command, string workingDirectory = null)
        {
            TestOutput.WriteLine($"Executing Git command -- {command}");
            var result = GitProcess.InvokeProcess(workingDirectory ?? RepoRoot, command);
            if (result.ExitCode != 0)
            {
                TestOutput.WriteLine($"Failed executing Git command -- {command}");
                TestOutput.WriteLine(result.Output);
                TestOutput.WriteLine(result.Errors);
                Assert.Equal(0, result.ExitCode);
            }
        }

        public RepoReseter GitCheckout(string testBranchName)
        {
            var branchName = $"tests/{testBranchName}";
            Git($"checkout -b {branchName} origin/tests/{testBranchName}");
            return new RepoReseter(this, branchName);
        }

        public void AssertFileOnDisk(string fullPath, bool expectExists = true)
        {
            var fileExists = File.Exists(fullPath); 
            if (expectExists != fileExists)
            {
                var allFiles = Directory.EnumerateFiles(RepoRoot);
                var expectMsg = expectExists ? "to exist, but it wasn't found." : "not to exist, but it is found on disk.";
                Assert.True(false, $"Expected file '{fullPath}' {expectMsg}. Found {allFiles.Count()} files in enlistment: {string.Join(", ", allFiles)} ");
            }
        }
    }
}
