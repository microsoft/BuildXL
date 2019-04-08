// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// If a test is essentially the same for shared opaques and regular opaques, put it in <see cref="DirectoryGraphConstructionTests"/> and use <see cref="InlineDataForOutputDirectoryAttribute"/>
    /// </summary>
    [Feature(Features.OpaqueDirectory)]
    public class SharedOpaqueDirectoryGraphConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SharedOpaqueDirectoryGraphConstructionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void ExplicitOutputsByDifferentPipsAreAllowedInSharedOpaqueDirectory()
        {
            var sharedOpaqueDirPath = @"\\dummyPath\SharedOpaqueDir1";
            var explicitOutputPath = @"\\dummyPath\SharedOpaqueDir1\out1.dll";

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(sharedOpaqueDirPath);

                AbsolutePath artifactInsodPath = env.Paths.CreateAbsolutePath(explicitOutputPath);

                XAssert.IsTrue(artifactInsodPath.IsWithin(env.PathTable, sodPath));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputFile(artifactInsodPath);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1\OpaqueDir2")]
        [InlineData(@"\\dummyPath\OpaqueDir1\SharedOpaqueDir2", @"\\dummyPath\OpaqueDir1")] 
        public void OpaqueAndSharedOpaqueShouldNotOverlap(string pod, string od)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(pod);
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(od);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath);
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);

                if (sodPath.IsWithin(env.PathTable, odPath))
                {
                    // If the shared opaque is in the cone of an opaque, we will discover that when adding the pip
                    var success = env.PipConstructionHelper.TryAddProcess(pip1);
                    Assert.False(success, "Finish should fail, since overlapping opaque and shared opaque directories is not allowed.");
                }
                else
                {
                    // Otherwise, when building the graph
                    env.PipConstructionHelper.AddProcess(pip1);
                    AssertFailedGraphBuilding(env);
                }
            }
        }

        [Theory]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1\OpaqueDir2", false)]
        [InlineData(@"\\dummyPath\OpaqueDir1\SharedOpaqueDir2", @"\\dummyPath\OpaqueDir1", true)]
        public void OpaqueAndSharedOpaqueShouldNotOverlapOnDifferentPips(string pod, string od, bool failOnFinish)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(pod);
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(od);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath);
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);

                if (failOnFinish)
                {
                    var success = env.PipConstructionHelper.TryAddProcess(pip2);
                    Assert.False(success, "Finish should fail, since overlapping opaque and shared opaque directories is not allowed.");
                }
                else
                {
                    env.PipConstructionHelper.AddProcess(pip2);
                    AssertFailedGraphBuilding(env);
                }
            }
        }

        [Fact]
        public void SharedOpaqueDirectoriesCanOverlapInDifferentPips()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath1 = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1");
                AbsolutePath sodPath2 = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1\SharedOpaqueDir2");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath1, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(sodPath2, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Fact]
        public void SharedOpaqueDirectoriesCannotOverlapInSamePip()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath1 = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1");
                AbsolutePath sodPath2 = sodPath1.Combine(env.PathTable, "SharedOpaqueDir2");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath1, SealDirectoryKind.SharedOpaque);
                pip1.AddOutputDirectory(sodPath2, SealDirectoryKind.SharedOpaque);

                var success = env.PipConstructionHelper.TryAddProcess(pip1);
                Assert.False(success, "Finish should fail, since overlapping shared opaques in the same pip is not allowed.");
            }
        }

        [Fact]
        public void PipsCanShareSameSharedOpaqueDirectory()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);
                outputs1.TryGetOutputDirectory(sodPath, out var pip1OutputPod);

                // Pip2 is consuming the shared opaque directory produced by pip1.
                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddInputDirectory(pip1OutputPod.Root);
                // process has to produce something, adding a dummy output.
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");
                pip2.AddOutputFile(dummyOut);
                env.PipConstructionHelper.AddProcess(pip2);

                // Pip3 uses the same shared opaque path (but with a different seal id)
                var pip3 = CreatePipBuilderWithTag(env, "test");
                pip3.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                var outputs3 = env.PipConstructionHelper.AddProcess(pip3);
                outputs3.TryGetOutputDirectory(sodPath, out var pip3OutputPod);

                // Pip4 is consuming the shared opaque directory produced by pip3.
                var pip4 = CreatePipBuilderWithTag(env, "test");
                pip4.AddInputDirectory(pip3OutputPod.Root);
                // process has to produce something, adding a dummy output.
                AbsolutePath dummyOut2 = env.Paths.CreateAbsolutePath(@"\\dummyPath\output2.dll");
                pip4.AddOutputFile(dummyOut2);
                env.PipConstructionHelper.AddProcess(pip4);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Fact]
        public void SourceFileCanBeSpecifiedInsideSharedOpaqueDirectory()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                AbsolutePath artifactInsodPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\SharedOpaqueDir1\in1.dll");
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddInputFile(FileArtifact.CreateSourceFile(artifactInsodPath));
                pip2.AddOutputFile(dummyOut);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData(@"a\b\c\SharedOpaqueDir1", "a", true)]
        [InlineData(@"a\b\c\SharedOpaqueDir1", "a", false)]
        public void NoSourceSealDirectoryShouldBeSpecifiedAboveSharedOpaqueDirectory(string SharedOpaqueDir, string sourceSealDir, bool allDirectories)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Configuration.Layout.ObjectDirectory.Combine(
                    env.PathTable,
                    RelativePath.Create(env.PathTable.StringTable, SharedOpaqueDir));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                AbsolutePath ssdPath = env.Configuration.Layout.ObjectDirectory.Combine(
                    env.PathTable,
                    RelativePath.Create(env.PathTable.StringTable, sourceSealDir));

                env.PipConstructionHelper.SealDirectorySource(
                        ssdPath,
                        kind: allDirectories ? SealDirectoryKind.SourceAllDirectories : SealDirectoryKind.SourceTopDirectoryOnly);

                AssertFailedGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData("SharedOpaqueDir1", @"SharedOpaqueDir1\a", false)]
        [InlineData("SharedOpaqueDir1", @"SharedOpaqueDir1\a", true)]
        public void SourceSealDirectoryCanBeSpecifiedBelowSharedOpaqueDirectory(string SharedOpaqueDir, string sourceSealDir, bool allDirectories)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath = env.Configuration.Layout.ObjectDirectory.Combine(
                    env.PathTable,
                    RelativePath.Create(env.PathTable.StringTable, SharedOpaqueDir));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                AbsolutePath ssdPath = env.Configuration.Layout.ObjectDirectory.Combine(
                    env.PathTable,
                    RelativePath.Create(env.PathTable.StringTable, sourceSealDir));

                env.PipConstructionHelper.SealDirectorySource(
                    ssdPath,
                    kind: allDirectories ? SealDirectoryKind.SourceAllDirectories : SealDirectoryKind.SourceTopDirectoryOnly);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Fact]
        public void UntrackedPathsAndScopesAreAllowedUnderSharedOpaqueDirectories()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(env.PathTable, @"\\dummyPath\SharedOpaqueDir1", SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddUntrackedDirectoryScope(env.PathTable, @"\\dummyPath\SharedOpaqueDir1\Nested");
                pip2.AddOutputFile(env.PathTable, @"\\dummyPath\output2.dll"); // dummy output
                env.PipConstructionHelper.AddProcess(pip2);

                var pip3 = CreatePipBuilderWithTag(env, "test");
                pip2.AddUntrackedDirectoryScope(env.PathTable, @"\\dummyPath\SharedOpaqueDir1\AnotherNested");
                pip3.AddOutputFile(env.PathTable, @"\\dummyPath\output3.dll"); // dummy output
                env.PipConstructionHelper.AddProcess(pip3);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1\partialSealedDir")]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1")]
        public void PartialSealedDirectoriesAreAllowedUnderSharedOpaqueDirectories(string sharedOpaqueRoot, string partialSealDirectoryRoot)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                // Pip2 writes a dummy output under partialSealDirectoryRoot
                var partialSealDirectoryPath = env.Paths.CreateAbsolutePath(partialSealDirectoryRoot);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputFile(partialSealDirectoryPath.Combine(env.PathTable, "dummy.txt"));
                var outputs2 = env.PipConstructionHelper.AddProcess(pip2);

                var partialSeal = env.PipConstructionHelper.SealDirectoryPartial(
                    partialSealDirectoryPath,
                    new [] { outputs2.GetOutputFiles().First() });

                // Pip1 declares a shared opaque and a dependency on the partial seal (which is nested to the shared opaque)
                var sodPath = env.Paths.CreateAbsolutePath(sharedOpaqueRoot);
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                pip1.AddInputDirectory(partialSeal);
                env.PipConstructionHelper.AddProcess(pip1);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1\fullySealedDir")]
        [InlineData(@"\\dummyPath\SharedOpaqueDir1", @"\\dummyPath\SharedOpaqueDir1")]
        public void FullySealedDirectoriesAreNotAllowedUnderSharedOpaqueDirectories(string sharedOpaqueRoot, string fullySealDirectoryRoot)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                var sodPath = env.Paths.CreateAbsolutePath(sharedOpaqueRoot);
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath, SealDirectoryKind.SharedOpaque);
                env.PipConstructionHelper.AddProcess(pip1);

                // Create empty fully seal dir
                env.PipConstructionHelper.SealDirectoryFull(
                    AbsolutePath.Create(env.PathTable, fullySealDirectoryRoot),
                    new FileArtifact[0]);

                AssertFailedGraphBuilding(env);
            }
        }
    }
}
