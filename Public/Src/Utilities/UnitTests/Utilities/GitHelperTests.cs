// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)] // the git portable executable is only available for windows
    public class GitHelperTests : TemporaryStorageTestBase
    {
        private class FakeRepository : IDisposable 
        {
            private readonly GitHelper m_gitHelper;
            
            public FakeRepository(GitHelper gitHelper, string path)
            {
                m_gitHelper = gitHelper;
                Directory.CreateDirectory(path);

                // Create repository
                _ = AssertSuccess(m_gitHelper.ExecuteGitCommandAsync("init")).GetAwaiter().GetResult();
                _ = AssertSuccess(m_gitHelper.ExecuteGitCommandAsync("config user.name buildxl", expectStdOut: false)).GetAwaiter().GetResult();
                _ = AssertSuccess(m_gitHelper.ExecuteGitCommandAsync("config user.email buildxl@example.com", expectStdOut: false)).GetAwaiter().GetResult();
                _ = AssertSuccess(m_gitHelper.ExecuteGitCommandAsync("init")).GetAwaiter().GetResult();

                for (var i = 0; i < 5; i++)
                {
                    _ = AssertSuccess(m_gitHelper.ExecuteGitCommandAsync("commit --allow-empty -m x")).GetAwaiter().GetResult();
                }
            }

            public void Dispose()
            {
                // git can launch a daemon that will survive the test if we don't stop it
                _ = m_gitHelper.ExecuteGitCommandAsync("fsmonitor--daemon stop").GetAwaiter().GetResult();
            }

        }

        private readonly GitHelper m_gitHelper;
        private readonly string m_repository;

        public GitHelperTests(ITestOutputHelper output) : base(output) 
        {
            var gitExecutablePath = Path.Combine(TestDeploymentDir, "git\\mingw64\\bin\\git.exe");
            m_repository = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            m_gitHelper = new GitHelper(gitExecutablePath, m_repository);
        }

        [Fact]
        public async Task GitHelperBasicTest()
        {
            // let's have a sanity check that we are setting up the tests properly, that is,
            // that the 'git' in our deployment is actually executable.
            // 'git --version' will always succeed, so let's use that.
            var version = await m_gitHelper.ExecuteGitCommandAsync("--version", true);
            XAssert.IsTrue(version.Succeeded);
        }

        [Fact]
        public async Task GitHelperOperationTests()
        {
            using var fakeRepository = new FakeRepository(m_gitHelper, m_repository);

            _ = await AssertSuccess(m_gitHelper.GetLatestCommitHashesAsync(3));
            _ = await AssertSuccess(m_gitHelper.GetMergeBaseAsync("HEAD", "HEAD"));
        }

        [Fact]
        public async Task MergeBaseCheck()
        {
            using var fakeRepository = new FakeRepository(m_gitHelper, m_repository);

            var latestCommits = await AssertSuccess(m_gitHelper.GetLatestCommitHashesAsync(3));
            var latest = latestCommits[0];
            var oldCommit = latestCommits[2];
                
            // The merge-base of two commits in a linear chain is the oldest one
            var mergeBase = await AssertSuccess(m_gitHelper.GetMergeBaseAsync(latest, oldCommit));
            XAssert.AreEqual(oldCommit, mergeBase);            
        }

        [Fact]
        public async Task ConsistentCommitHistory()
        {
            using var fakeRepository = new FakeRepository(m_gitHelper, m_repository);

            // Suppose the commit history is ... C3 -- C2 -- C1 -- C0
            // with C0 being the latest commit. This call should give [C0, C1, C2]
            var latestCommits = await AssertSuccess(m_gitHelper.GetLatestCommitHashesAsync(3));

            // The commit coming right before the one at the tip (C1)
            var previousCommit = latestCommits[1];
         
            // We get the latests commits starting from C1 (should be [C1, C2, C3])
            var prior = await AssertSuccess(m_gitHelper.GetLatestCommitHashesAsync(3, previousCommit));

            XAssert.AreEqual(latestCommits[2], prior[1]);
        }

        private static async Task<T> AssertSuccess<T>(Task<Possible<T>> task)
        {
            var result = await task;
            if (!result.Succeeded)
            {
                XAssert.IsTrue(false, result.Failure.Describe());
            }

            return result.Result;
        }
    }
}
