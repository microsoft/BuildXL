// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    public class TrustedAccessesConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public TrustedAccessesConstructionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TrustedAccessesAreBlockedIfPipContainsOutputDirectory()
        {
            var sharedOpaqueDirPath = @"\\dummyPath\SharedOpaqueDir1";

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(sharedOpaqueDirPath);

                var pip = CreatePipBuilderWithTag(env, "test");
                pip.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                pip.Options |= Process.Options.TrustStaticallyDeclaredAccesses;

                var success = env.PipConstructionHelper.TryAddProcess(pip);
                Assert.False(success, "Finish should fail, a process with output directories is not allowed to trust declared accesses");
            }
        }

        [Fact]
        public void TrustedAccessesAreBlockedIfPipDependsOnSourceSeal()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                var sourceSealedDirectory = new SealDirectory(
                    env.SourceRoot,
                    CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance),
                    outputDirectoryContents: CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                    kind: SealDirectoryKind.SourceAllDirectories,
                    provenance: env.CreatePipProvenance(StringId.Invalid),
                    tags: ReadOnlyArray<StringId>.Empty,
                    patterns: ReadOnlyArray<StringId>.Empty);

                DirectoryArtifact artifact = env.PipGraph.AddSealDirectory(sourceSealedDirectory, PipId.Invalid);
                Assert.True(artifact.IsValid);

                var pip = CreatePipBuilderWithTag(env, "test");

                pip.AddInputDirectory(artifact);
                pip.Options |= Process.Options.TrustStaticallyDeclaredAccesses;

                var success = env.PipConstructionHelper.TryAddProcess(pip);
                Assert.False(success, "Finish should fail, a process depending on a source sealed directory is not allowed to trust declared accesses");
            }
        }
    }
}
