// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    public class OpaqueDirectoryGraphConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public OpaqueDirectoryGraphConstructionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(@"\\dummyPath\OpaqueDir1", @"\\dummyPath\OpaqueDir1\OpaqueDir2")]
        [InlineData(@"\\dummyPath\OpaqueDir1\OpaqueDir2", @"\\dummyPath\OpaqueDir1")] // nesting another way round.
        public void TestNoOpaqueDirectoryAreAllowedInAnotherOpaqueDirectory(string od1, string od2)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(od1);
                AbsolutePath anotherOd = env.Paths.CreateAbsolutePath(od2);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);
                pip1.AddOutputDirectory(anotherOd, SealDirectoryKind.Opaque);

                var success = env.PipConstructionHelper.TryAddProcess(pip1);
                Assert.False(success, "Finish should fail, since opaque directories nesting is not allowed.");
            }
        }

        [Fact]
        public void TestOpaqueDirectoriesCannotOverlap()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\OpaqueDir1");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);

                // Should be legit
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);

                var success = env.PipConstructionHelper.TryAddProcess(pip2);
                Assert.False(success, "Finish should fail, since multiple pips creating the same OD are not allowed.");
            }
        }

        /// <summary>
        /// This test should not be made to work because we allow users to specify explicitly output file inside an output directory of the same pip.
        /// </summary>
        /// <remarks>
        /// When users specify explicitly output file inside an output directory of the same pip, another pip may consume that output file
        /// directly. That same other pip can also refer both the output file and the output directory as its dependencies.
        /// </remarks>
        [Fact(Skip = "This test should not be made to work; see comment for the reason")]
        public void TestNoExplicitInputsAreAllowedInOpaqueDirectory()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\OpaqueDir1");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);
                outputs1.TryGetOutputDirectory(odPath, out var pip1OutputOd);
                var pip2 = CreatePipBuilderWithTag(env, "test");

                // Pip2 is consuming OD produced by pip1.
                pip2.AddInputDirectory(pip1OutputOd.Root);

                AbsolutePath artifactInOdPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\OpaqueDir1\in1.dll");
                XAssert.IsTrue(artifactInOdPath.IsWithin(env.PathTable, odPath));

                // and trying to add input, defined in OD. this is not allowed.
                pip2.AddInputFile(artifactInOdPath);

                // process has to produce something, adding a dummy output.
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");
                pip2.AddOutputFile(dummyOut);

                var success = env.PipConstructionHelper.TryAddProcess(pip2);
                Assert.False(success, "Finish should fail, since no explicit inputs are allowed in opaque directory path.");
            }
        }

        [Fact]
        public void TestNoSourceFileShouldLaterBeSpecifiedInsideOpaqueDirectory()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath odPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\OpaqueDir1");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(odPath, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip1);

                AbsolutePath artifactInOdPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\OpaqueDir1\in1.dll");
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddInputFile(artifactInOdPath);
                pip2.AddOutputFile(dummyOut);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertFailedGraphBuilding(env);
            }
        }

        [Fact]
        public void TestSourceFileCanNotBeSpecifiedInsideOpaqueDirectoryEvenIfItComesEarlier()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath artifactInPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1\in1.dll");
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddInputFile(artifactInPath);
                pip2.AddOutputFile(dummyOut);
                env.PipConstructionHelper.AddProcess(pip2);

                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip1);

                AssertFailedGraphBuilding(env);
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithSealedSourceDirectory1()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(env.ObjectRoot, "ssd");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip1);

                var ssd = env.PipConstructionHelper.SealDirectorySource(path);
                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputFile(env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll"));
                pip2.AddInputDirectory(ssd);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertFailedGraphBuilding(env);
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithSealedSourceDirectory2()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(env.ObjectRoot, "ssd");

                var ssd = env.PipConstructionHelper.SealDirectorySource(path);
                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputFile(env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll"));
                pip2.AddInputDirectory(ssd);
                env.PipConstructionHelper.AddProcess(pip2);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                XAssert.IsFalse(env.PipConstructionHelper.TryAddProcess(pip1));
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithOutputFile1()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");
                
                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputFile(path);
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertFailedGraphBuilding(env);
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithOutputFile2()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip2);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputFile(path);
                XAssert.IsFalse(env.PipConstructionHelper.TryAddProcess(pip1));
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithSourceFile1()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddInputFile(path);
                pip1.AddOutputFile(env.Paths.CreateAbsolutePath(@"\\dummyPath\out"));
                env.PipConstructionHelper.AddProcess(pip1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip2);

                AssertFailedGraphBuilding(env);
            }
        }

        [Fact]
        public void TestOpaqueDirectoryCannotCoincideWithSourceFile2()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(path, SealDirectoryKind.Opaque);
                env.PipConstructionHelper.AddProcess(pip2);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddInputFile(path);
                pip1.AddOutputFile(env.Paths.CreateAbsolutePath(@"\\dummyPath\out"));

                env.PipConstructionHelper.AddProcess(pip1);
                AssertFailedGraphBuilding(env);
            }
        }
    }
}
