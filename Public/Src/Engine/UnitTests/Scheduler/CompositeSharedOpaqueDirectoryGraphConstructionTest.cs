// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    [Feature(Features.OpaqueDirectory)]
    public class CompositeSharedOpaqueDirectoryGraphConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public CompositeSharedOpaqueDirectoryGraphConstructionTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
        }

        [Fact]
        public void AllContainedDirectoriesMustBeUnderACommonRoot()
        {
            var sodDir1 = @"\\dummyPath\SharedOpaqueDir1";
            var sodDir2 = @"\\outOfRoot\SharedOpaqueDir2";

            var root = @"\\dummyPath";

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath1 = env.Paths.CreateAbsolutePath(sodDir1);
                AbsolutePath sodPath2 = env.Paths.CreateAbsolutePath(sodDir2);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath1, SealDirectoryKind.SharedOpaque);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);
                outputs1.TryGetOutputDirectory(sodPath1, out var sharedOpaqueDirectory1);

                var pip2 = CreatePipBuilderWithTag(env, "test");
                pip2.AddOutputDirectory(sodPath2, SealDirectoryKind.SharedOpaque);
                var outputs2 = env.PipConstructionHelper.AddProcess(pip2);
                outputs2.TryGetOutputDirectory(sodPath2, out var sharedOpaqueDirectory2);

                var result = env.PipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    env.Paths.CreateAbsolutePath(root),
                    new[] { sharedOpaqueDirectory1.Root, sharedOpaqueDirectory2.Root },
                    contentFilter: null,
                    description: null,
                    tags: new string[] { },
                    out var composedSharedOpaque);

                XAssert.IsFalse(result);

                AssertErrorEventLogged(LogEventId.ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot);
                IgnoreWarnings();
            }
        }

        [Fact]
        public void AllContainedDirectoriesMustBeSharedOpaques()
        {
            var sodDir1 = @"\\dummyPath\SharedOpaqueDir1";
            var sourceSealDir2 = @"\\dummyPath\SourceSeal";

            var root = @"\\dummyPath";

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath1 = env.Paths.CreateAbsolutePath(sodDir1);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath1, SealDirectoryKind.SharedOpaque);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);
                outputs1.TryGetOutputDirectory(sodPath1, out var sharedOpaqueDirectory1);
                var sourceSealDirectory = env.PipConstructionHelper.SealDirectoryPartial(
                    env.Paths.CreateAbsolutePath(sourceSealDir2),
                    new FileArtifact[0]);

                var result = env.PipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    env.Paths.CreateAbsolutePath(root),
                    new[] { sharedOpaqueDirectory1.Root, sourceSealDirectory },
                    contentFilter: null,
                    description: null,
                    tags: new string[] { },
                    out var composedSharedOpaque);

                XAssert.IsFalse(result);
                AssertErrorEventLogged(LogEventId.ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque);
                IgnoreWarnings();
            }
        }

        [Fact]
        public void CompositeSharedOpaquesCanBeComposed()
        {
            var sodDir1 = @"\\dummyPath\SharedOpaqueDir1";
            var root = @"\\dummyPath";

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath sodPath1 = env.Paths.CreateAbsolutePath(sodDir1);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                pip1.AddOutputDirectory(sodPath1, SealDirectoryKind.SharedOpaque);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);
                outputs1.TryGetOutputDirectory(sodPath1, out var sharedOpaqueDirectory1);

                // This composite shared opaque contains a regular shared opaque
                var result = env.PipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    env.Paths.CreateAbsolutePath(root),
                    new[] { sharedOpaqueDirectory1.Root },
                    contentFilter: null,
                    description: null,
                    tags: new string[] { },
                    out var composedSharedOpaque);

                XAssert.IsTrue(result);

                // This composite shared opaque contains another composite shared opaque
                result = env.PipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    env.Paths.CreateAbsolutePath(root),
                    new[] { composedSharedOpaque },
                    contentFilter: null,
                    description: null,
                    tags: new string[] { },
                    out var nestedComposedSharedOpaque);

                XAssert.IsTrue(result);

                AssertSuccessGraphBuilding(env);
                IgnoreWarnings();
            }
        }
    }
}
