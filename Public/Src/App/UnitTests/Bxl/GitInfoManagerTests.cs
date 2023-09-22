// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL
{
    /// <summary>
    /// Tests to verify the functionality for obtaining git remote repo URL.
    /// </summary>
    public class GitInfoManagerTests : TemporaryStorageTestBase
    {
        public GitInfoManagerTests(ITestOutputHelper output)
             : base(output)
        {
        }

        [Fact]
        public void TestGetGitInfoFromFile()
        {
            // Create A/B/C/.git/config
            string gitRoot = WriteGitConfigFile(Path.Combine("A", "B", "C"), CaptureGitInfoTestsProperties.GitConfigWithOnlyOrigin);

            // Search from A/B/C
            var gitInfoManager = CreateGitInfoManager(gitRoot);
            string url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin, url);

            // Search from A/B/C/D/E
            gitInfoManager = CreateGitInfoManager(Path.Combine(gitRoot, "D", "E"));
            url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin, url);

            // Search from A/B (missing .git folder)
            gitInfoManager = CreateGitInfoManager(Path.GetDirectoryName(gitRoot));
            url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(null, url);
        }

        [Fact]
        public void TestGetGitInfoFromMissingFile()
        {
            // Create A/B/C/.git/config
            string gitRoot = WriteGitConfigFile(Path.Combine("A", "B", "C"), null);

            // Search from A/B/C
            var gitInfoManager = CreateGitInfoManager(gitRoot);
            string url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(null, url);

            // Search from A/B/C/D/E
            gitInfoManager = CreateGitInfoManager(Path.Combine(gitRoot, "D", "E"));
            url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(null, url);

            // Search from A/B (missing .git folder)
            gitInfoManager = CreateGitInfoManager(Path.GetDirectoryName(gitRoot));
            url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(null, url);
        }

        [Fact]
        public void TestGetGitInfoWithMalformedContent()
        {
            // Create A/B/C/.git/config
            string gitRoot = WriteGitConfigFile(Path.Combine("A", "B", "C"), CaptureGitInfoTestsProperties.GitConfigWithNoUrlField);

            // Search from A/B/C
            var gitInfoManager = CreateGitInfoManager(gitRoot);
            string url = gitInfoManager.GetRemoteRepoUrl();
            XAssert.AreEqual(null, url);
        }

        private string WriteGitConfigFile(string relativeDir, string gitConfigContent)
        {
            var rootDirectory = GetFullPathFromRelative(relativeDir);
            var gitConfigFilePath = Path.Combine(rootDirectory, ".git", "config");
            Directory.CreateDirectory(Path.GetDirectoryName(gitConfigFilePath));

            if (gitConfigContent != null)
            {
                File.WriteAllText(gitConfigFilePath, gitConfigContent);
            }

            return rootDirectory;
        }

        private string GetFullPathFromRelative(string relativePath)
        {
            return Path.Combine(TemporaryDirectory, relativePath);
        }

        private GitInfoManager CreateGitInfoManager(string startDirectory) => GitInfoManager.CreateForTesting(new LoggingContext("Test"), startDirectory, TemporaryDirectory);

        [Theory]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithSpacesForOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithSpacesForUpstream, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithUpstream)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithWrongSpacingInOrigin, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithOnlyOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithUpstreamAndOrigin, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithUpstream)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithNoUrlField, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithNoUpstreamAndNoOrigin, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithMultipleFork, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithNoUpstreamUrlButOriginUrl1, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithNoUpstreamUrlButOriginUrl2, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithMisplacedUrl, null)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithMisplacedUpstreamUrlButValidOriginUrl1, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        [InlineData(CaptureGitInfoTestsProperties.GitConfigWithMisplacedUpstreamUrlButValidOriginUrl2, CaptureGitInfoTestsProperties.ExpectedGitRepoUrlWithOrigin)]
        public void TestGitConfigParsing(string gitFileContent, string expectedGitRemoteUrl)
        {
            XAssert.AreEqual(expectedGitRemoteUrl, GitInfoManager.ParseGitConfigContent(gitFileContent));
        }

        /// <summary>
        /// Test data for <see cref="TestGitConfigParsing(string, string)"/>.
        /// </summary>
        /// <remarks>
        /// These data are deliberatly written with tabs instead of spaces to make sure that they reflect typical .git/config files.
        /// </remarks>
        internal static class CaptureGitInfoTestsProperties
        {
            public const string ExpectedGitRepoUrlWithOrigin = "https://dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin";
            public const string ExpectedGitRepoUrlWithUpstream = "https://dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream";
            public const string CommonGitContentPrefix =
@"[core]
	repositoryformatversion = 0
	filemode = false
	bare = false
	logallrefupdates = true
	symlinks = false
	ignorecase = true";

            public const string CommonGitContentSuffix =
@"
[branch """"main""""]
	remote = origin
	merge = refs/heads/main
[branch """"dev/userName/branch1""""]
	remote = origin
	merge = refs/heads/dev/userName/branch1
[branch """"dev/userName/branch2""""]
	remote = origin
	merge = refs/heads/dev/userName/branch2";

            public const string GitConfigWithOnlyOrigin =
CommonGitContentPrefix +
@"
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*" +
CommonGitContentSuffix;

            public const string GitConfigWithUpstreamAndOrigin =
CommonGitContentPrefix +
@"
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*
[remote ""upstream""]
	Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
	fetch = +refs/heads/*:refs/remotes/upstream/*" +
CommonGitContentSuffix;

            public const string GitConfigWithNoUpstreamAndNoOrigin = CommonGitContentPrefix + CommonGitContentSuffix;

            public const string GitConfigWithNoUrlField =
CommonGitContentPrefix +
@"
[remote ""origin""]
	fetch = +refs/heads/*:refs/remotes/origin/*" +
CommonGitContentSuffix;

            public const string GitConfigWithMultipleFork =
CommonGitContentPrefix +
@"
[remote ""origin""]
	Url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*
[remote ""test""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
	fetch = +refs/heads/*:refs/remotes/upstream/*" +
CommonGitContentSuffix;

            public const string GitConfigWithSpacesForOrigin =
CommonGitContentPrefix +
@"
[   remote                   ""origin""]
	Url               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*" +
CommonGitContentSuffix;

            public const string GitConfigWithWrongSpacingInOrigin =
CommonGitContentPrefix +
@"[   remote                   ""origin""]
	u  r  l               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*" +
CommonGitContentSuffix;

            public const string GitConfigWithSpacesForUpstream =
CommonGitContentPrefix +
@"
[   remote                   ""origin""]
	url               =      https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*
[branch ""main""]
	remote = origin
	merge = refs/heads/main
[remote           ""upstream""                    ]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
	fetch = +refs/heads/*:refs/remotes/upstream/*";

            public const string GitConfigWithNoUpstreamUrlButOriginUrl1 =
CommonGitContentPrefix +
@"
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*
[remote ""upstream""]
	fetch = +refs/heads/*:refs/remotes/upstream/*";

            public const string GitConfigWithNoUpstreamUrlButOriginUrl2 =
CommonGitContentPrefix +
@"
[remote ""upstream""]
	fetch = +refs/heads/*:refs/remotes/upstream/*
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*" +
CommonGitContentSuffix;

            public const string GitConfigWithMisplacedUrl =
CommonGitContentPrefix +
@"
[remote ""upstream""]
	fetch = +refs/heads/*:refs/remotes/origin/*
[branch ""main""]
	remote = origin
	merge = refs/heads/main
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream";

            public const string GitConfigWithMisplacedUpstreamUrlButValidOriginUrl1 =
CommonGitContentPrefix +
@"
[remote ""upstream""]
	fetch = +refs/heads/*:refs/remotes/origin/*
[branch ""main""]
	remote = origin
	merge = refs/heads/main
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*";

            public const string GitConfigWithMisplacedUpstreamUrlButValidOriginUrl2 =
CommonGitContentPrefix +
@"
[remote ""origin""]
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
	fetch = +refs/heads/*:refs/remotes/origin/*
[remote ""upstream""]
	fetch = +refs/heads/*:refs/remotes/origin/*
[branch ""main""]
	remote = origin
	merge = refs/heads/main
	url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream";
        }
    }
}