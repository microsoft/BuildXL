// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class TestBase
    {
        // CODESYNC: Setup.cmd (Setup.cmd clones that repo in these 2 locations)
        private const string HardcodedGvfsRepoLocation = @"C:\Temp\BuildXL.Test.Gvfs";
        private const string HardcodedGitRepoLocation = @"C:\Temp\BuildXL.Test";

        protected ITestOutputHelper TestOutput { get; }

        public TestBase(ITestOutputHelper testOutput)
        {
            TestOutput = testOutput;
        }

        private static string GitRepoLocation(bool useGvfs)
        {
            // TODO: if needed, override hardcoded locations with env vars
            return useGvfs 
                ? HardcodedGvfsRepoLocation + "\\src" // git repo is always in src folder relative to gvfs repo root
                : HardcodedGitRepoLocation;
        }

        public enum RepoInit { Clone, UseExisting }
        public enum RepoKind { Git, Gvfs }
        public readonly struct RepoConfig
        {
            public RepoKind RepoKind { get; }
            public RepoInit RepoInit { get; }
            public RepoConfig(RepoKind kind, RepoInit init)
            {
                RepoKind = kind;
                RepoInit = init;
            }

            public override string ToString() => $"{{{RepoKind};{RepoInit}}}";
        }

        public GitJournalHelper Clone(RepoConfig cfg)
        {
            bool useGvfs = cfg.RepoKind == RepoKind.Gvfs;
            var helper = cfg.RepoInit == RepoInit.Clone
                ? GitJournalHelper.Clone(TestOutput, useGvfs)
                : GitJournalHelper.InitializeWithExistingRepo(TestOutput, GitRepoLocation(useGvfs));

            if (useGvfs)
            {
                // tracking GVFS_projection file so that we can assert that this file indeed changed
                // whenever the actual file changes are not yet reflected in the filesystem
                helper.TrackGvfsProjectionFile();
            }

            return helper;
        }
    }
}