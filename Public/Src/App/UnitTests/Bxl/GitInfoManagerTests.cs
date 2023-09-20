// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL
{
    /// <summary>
    /// Tests to verify the functionality for obtaining git remote repo name.
    /// </summary>
    public class GitInfoManagerTests : TemporaryStorageTestBase
    {
        public GitInfoManagerTests(ITestOutputHelper output)
             : base(output)
        {
            RegisterEventSource(global::bxl.ETWLogger.Log);
        }

        /// <summary>
        /// Helper method to obtain the git remote url.
        /// </summary>
        private string GetGitRemoteUrl(string gitFileContentForTesting)
        {
            using var reader = new StringReader(gitFileContentForTesting);
            return GitInfoManager.ParseGitConfigStreamReader(reader);
        }

        /// <summary>
        /// Tests for two conditions:
        /// The remote named as "upstream" will take the highest precendence. When using GitHub triangular forking conventions, the "origin" will be the user's fork while the remote named "upstream" will be the original repo that was forked.
        /// Otherwise use the url of the remote named "origin" (if one exists).
        /// If both are not present then the application should not crash, but should return null and log a verbose event.
        /// </summary>
        [Theory]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithOnlyOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOnlyOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithUpstreamAndOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithUpstream)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithNoUrlField, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithNoUpstreamAndNoOrigin, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigRemoteRepoWithMultipleFork, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOnlyOrigin)]
        public void TestGitConfigRemoteRepoName(string gitFileContent, string expectedGitRemoteUrl)
        {
            XAssert.AreEqual(GetGitRemoteUrl(gitFileContent), expectedGitRemoteUrl);
        }

        /// <summary>
        /// Special cases in parsing git config file
        /// </summary>
        [Theory]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithSpacesForOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOnlyOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithSpacesForUpstream, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithUpstream)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigFileWithWrongSpacingInOrigin, null)]
        public void TestGitConfigParsing(string gitFileContent, string expectedGitRemoteUrl)
        {
            XAssert.AreEqual(GetGitRemoteUrl(gitFileContent), expectedGitRemoteUrl);
        }

        internal static class CaptureGitInfoTestsProperties
        {
            public const string ExpectedGitRepoUrlWithOnlyOrigin = "https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin";

            public const string ExpectedGitRepoUrlWithUpstream = "https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream";

            public const string GitConfigFileWithOnlyOrigin = @"[core]repositoryformatversion = 0
                                                             filemode = false
                                                             bare = false
                                                             logallrefupdates = true
                                                             symlinks = false
                                                             ignorecase = true
                                                             [remote ""origin""]
                                                             Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                             fetch = +refs/heads/*:refs/remotes/origin/*
                                                             [branch ""main""]
                                                             remote = origin
                                                             merge = refs/heads/main
                                                             [branch ""dev/userName/branch1""]
                                                             remote = origin
                                                             merge = refs/heads/dev/userName/branch1
                                                             [branch ""dev/userName/branch2""]
                                                             remote = origin
                                                             merge = refs/heads/dev/userName/branch2";

            public const string GitConfigFileWithUpstreamAndOrigin = @"[core]repositoryformatversion = 0
                                                                    filemode = false
                                                                    bare = false
                                                                    logallrefupdates = true
                                                                    symlinks = false
                                                                    ignorecase = true
                                                                    [remote ""origin""]
                                                                    Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                                    fetch = +refs/heads/*:refs/remotes/origin/*
                                                                    [remote ""upstream""]
                                                                    Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
                                                                    fetch = +refs/heads/*:refs/remotes/upstream/* 
                                                                    [branch ""main""]
                                                                    remote = origin
                                                                    merge = refs/heads/main
                                                                    [branch ""dev/userName/branch1""]
                                                                    remote = origin
                                                                    merge = refs/heads/dev/userName/branch1
                                                                    [branch ""dev/userName/branch2""]
                                                                    remote = origin
                                                                    merge = refs/heads/dev/userName/branch2";


            public const string GitConfigFileWithNoUpstreamAndNoOrigin = @"[core]repositoryformatversion = 0
                                                                        filemode = false
                                                                        bare = false
                                                                        logallrefupdates = true
                                                                        symlinks = false
                                                                        ignorecase = true
                                                                        [branch ""main""]
                                                                        remote = origin
                                                                        merge = refs/heads/main
                                                                        [branch ""dev/userName/branch1""]
                                                                        remote = origin
                                                                        merge = refs/heads/dev/userName/branch1
                                                                        [branch ""dev/userName/branch2""]
                                                                        remote = origin
                                                                        merge = refs/heads/dev/userName/branch2";


            public const string GitConfigFileWithNoUrlField = @"[core]repositoryformatversion = 0
                                                             filemode = false
                                                             bare = false
                                                             logallrefupdates = true
                                                             symlinks = false
                                                             ignorecase = true
                                                             [remote ""origin""]
                                                             fetch = +refs/heads/*:refs/remotes/origin/*
                                                             [branch ""main""]
                                                             remote = origin
                                                             merge = refs/heads/main
                                                             [branch ""dev/userName/branch1""]
                                                             remote = origin
                                                             merge = refs/heads/dev/userName/branch1
                                                             [branch ""dev/userName/branch2""]
                                                             remote = origin
                                                             merge = refs/heads/dev/userName/branch2";


            public const string GitConfigRemoteRepoWithMultipleFork = @"[core]repositoryformatversion = 0
                                                                     filemode = false
                                                                     bare = false
                                                                     logallrefupdates = true
                                                                     symlinks = false
                                                                     ignorecase = true
                                                                     [remote ""origin""]
                                                                     Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                                     fetch = +refs/heads/*:refs/remotes/origin/*
                                                                     [remote ""test""]
                                                                     Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
                                                                     fetch = +refs/heads/*:refs/remotes/upstream/* 
                                                                     [branch ""main""]
                                                                     remote = origin
                                                                     merge = refs/heads/main
                                                                     [branch ""dev/userName/branch1""]
                                                                     remote = origin
                                                                     merge = refs/heads/dev/userName/branch1
                                                                     [branch ""dev/userName/branch2""]
                                                                     remote = origin
                                                                     merge = refs/heads/dev/userName/branch2";

            public const string GitConfigFileWithSpacesForOrigin = @"[core]repositoryformatversion = 0
                                                                  filemode = false
                                                                  bare = false
                                                                  logallrefupdates = true
                                                                  symlinks = false
                                                                  ignorecase = true
                                                                  [   remote                   ""origin""]
                                                                  Url               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                                  fetch = +refs/heads/*:refs/remotes/origin/*
                                                                  [branch ""main""]
                                                                  remote = origin
                                                                  merge = refs/heads/main
                                                                  [branch ""dev/userName/branch1""]
                                                                  remote = origin
                                                                  merge = refs/heads/dev/userName/branch1
                                                                  [branch ""dev/userName/branch2""]
                                                                  remote = origin
                                                                  merge = refs/heads/dev/userName/branch2";

            public const string GitConfigFileWithSpacesForUpstream = @"[core]repositoryformatversion = 0
                                                                    filemode = false
                                                                    bare = false
                                                                    logallrefupdates = true
                                                                    symlinks = false
                                                                    ignorecase = true
                                                                    [   remote                   ""origin""]
                                                                    Url               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                                    fetch = +refs/heads/*:refs/remotes/origin/*
                                                                    [branch ""main""]
                                                                    remote = origin
                                                                    merge = refs/heads/main
                                                                    [remote           ""upstream""                    ]
                                                                    Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
                                                                    fetch = +refs/heads/*:refs/remotes/upstream/*";

            public const string GitConfigFileWithWrongSpacingInOrigin = @"[core]repositoryformatversion = 0
                                                                       filemode = false
                                                                       bare = false
                                                                       logallrefupdates = true
                                                                       symlinks = false
                                                                       ignorecase = true
                                                                       [   remote                   ""origin""]
                                                                       u  r  l               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
                                                                       fetch = +refs/heads/*:refs/remotes/origin/*
                                                                       [branch ""main""]
                                                                       remote = origin
                                                                       merge = refs/heads/main
                                                                       [branch ""dev/userName/branch1""]
                                                                       remote = origin
                                                                       merge = refs/heads/dev/userName/branch1
                                                                       [branch ""dev/userName/branch2""]
                                                                       remote = origin
                                                                       merge = refs/heads/dev/userName/branch2";

        }
    }
}