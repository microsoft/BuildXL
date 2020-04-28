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
    public class GvfsJournalHelper : JournalHelper
    {
        private readonly string m_repo = "https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.Test.Gvfs";

        private  GVFSFunctionalTestEnlistment m_enlistment;
        
        public string RepoRoot => m_enlistment.RepoRoot;

        private ITestOutputHelper m_testOutput;

        private GvfsJournalHelper(ITestOutputHelper testOutput, string repo = null)
        {
            m_testOutput = testOutput;
            if (repo != null)
            {
                m_repo = repo;
            }

            // Set values for the Gvfs test helpers
            Settings.Default.Initialize();
            Settings.Default.Commitish = @"master";
            Settings.Default.EnlistmentRoot = WorkingFolder;
            Settings.Default.PathToGVFS = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles"), "GVFS", "gvfs.exe");
            Settings.Default.RepoToClone = m_repo;
            GVFSTestConfig.RepoToClone = m_repo;
            GVFSTestConfig.NoSharedCache = true;
            GVFSTestConfig.TestGVFSOnPath = true;
            GVFSTestConfig.DotGVFSRoot = ".gvfs";

            Assert.True(File.Exists(Settings.Default.PathToGVFS), Settings.Default.PathToGVFS);
            Assert.True(File.Exists(Settings.Default.PathToGit), Settings.Default.PathToGit);
        }

        public override string GetPath(string path)
        {
            return Path.Combine(RepoRoot, path);
        }

        private void Clone(string commitish = null)
        {
            Assert.True(CurrentProcess.IsElevated, "Tests must run elevated!!");

            m_enlistment = GVFSFunctionalTestEnlistment.CloneAndMount(Settings.Default.PathToGVFS);
        }

        public static GvfsJournalHelper Clone(ITestOutputHelper m_testOutput, string repo = null)
        {
            var helper = new GvfsJournalHelper(m_testOutput, repo);
            helper.Clone();
            return helper;
        }

        public void Git(string command)
        {
            m_testOutput.WriteLine($"Executing Git command -- {command}");
            var result = GitProcess.InvokeProcess(RepoRoot, command);
            if (result.ExitCode != 0)
            {
                m_testOutput.WriteLine($"Failed executing Git command -- {command}");
                m_testOutput.WriteLine(result.Output);
                m_testOutput.WriteLine(result.Errors);
                Assert.Equal(0, result.ExitCode);
            }
        }

        public void GitCheckout(string testBranchName)
        {
            Git($"checkout -b tests/{testBranchName} origin/tests/{testBranchName}");
        }

        public void Gvfs(string command)
        {
            m_testOutput.WriteLine($"Executing Gvfs command -- {command}");
        }

        public void AssertFileOnDisk(string path, bool expectExists = true)
        {
            var fullPath = Path.Combine(RepoRoot, path);
            var fileExists = File.Exists(fullPath); 
            if (expectExists != fileExists)
            {
                var allFiles = Directory.EnumerateFiles(RepoRoot);
                var expectMsg = expectExists ? "to exist, but it wasn't found." : "not to exist, but it is found on disk.";
                Assert.True(false, $"Expected file '{fullPath}' {expectMsg}. Found {allFiles.Count()} files in enlistment: {string.Join(", ", allFiles)} ");
            }
        }

        public void AssertFileContents(string path, string contents)
        {
            var fullPath = Path.Combine(RepoRoot, path);
            var fileExists = File.Exists(fullPath); 
            if (!fileExists)
            {
                var allFiles = Directory.EnumerateFiles(RepoRoot);
                var expectMsg = "to exist, but it wasn't found.";
                Assert.True(false, $"Expected file '{fullPath}' {expectMsg}. Found {allFiles.Count()} files in enlistment: {string.Join(", ", allFiles)} ");
            }
            
            var fileContents = File.ReadAllText(fullPath);
            Assert.Equal(contents, fileContents);
        }
    }
}
