// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests functionality that is shared across opaque and shared opaque directories
    /// </summary>
    [Feature(Features.OpaqueDirectory)]
    [Feature(Features.SharedOpaqueDirectory)]
    public class DirectoryGraphConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public DirectoryGraphConstructionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineDataForOutputDirectory]
        public virtual void SimpleDirectoryProduceConsume(AddDirectory addDirectory)
        {
            // Create a relationship where pip2 depends on a shared opaque directory of pip1
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");

                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, path);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);

                outputs1.TryGetOutputDirectory(path, out var pip1OutputDirectory);

                var pip2 = CreatePipBuilderWithTag(env, "test");

                // Pip2 is consuming the directory produced by pip1.
                pip2.AddInputDirectory(pip1OutputDirectory.Root);

                // process has to produce something, adding a dummy output.
                AbsolutePath dummyOut = env.Paths.CreateAbsolutePath(@"\\dummyPath\output.dll");
                pip2.AddOutputFile(dummyOut);
                env.PipConstructionHelper.AddProcess(pip2);
                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineDataForOutputDirectory]
        public void TestOpaqueDirectorySemistableHashUniqueness(AddDirectory addDirectory)
        {
            // Create a relationship where pip2 uses the output of pip1
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path1 = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir1");
                AbsolutePath path2 = env.Paths.CreateAbsolutePath(@"\\dummyPath\Dir2");
                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, path1);
                addDirectory(pip1, path2);
                var outputs1 = env.PipConstructionHelper.AddProcess(pip1);

                outputs1.TryGetOutputDirectory(path1, out var pip1OutputDir1);
                outputs1.TryGetOutputDirectory(path2, out var pip1OutputDir2);

                var pipGraph = AssertSuccessGraphBuilding(env);

                var pip1SemistableHash = pipGraph.PipTable.GetPipSemiStableHash(pipGraph.GetProducer(pip1OutputDir1.Root));
                var od1SemistableHash = pipGraph.PipTable.GetPipSemiStableHash(pipGraph.GetSealedDirectoryNode(pip1OutputDir1.Root).ToPipId());
                var od2SemistableHash = pipGraph.PipTable.GetPipSemiStableHash(pipGraph.GetSealedDirectoryNode(pip1OutputDir2.Root).ToPipId());

                Assert.NotEqual(pip1SemistableHash, od1SemistableHash);
                Assert.NotEqual(pip1SemistableHash, od2SemistableHash);
                Assert.NotEqual(od1SemistableHash, od2SemistableHash);
            }
        }

        [Theory]
        [InlineDataForOutputDirectory(@"\\dummyPath\Dir1", @"\\dummyPath\Dir1\out1.dll")]
        public void TestExplicitOutputsAreAllowedInOpaqueDirectory(AddDirectory addDirectory, string opaqueDirPath, string explicitOutputPath)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(opaqueDirPath);
                AbsolutePath artifactInPath = env.Paths.CreateAbsolutePath(explicitOutputPath);

                XAssert.IsTrue(artifactInPath.IsWithin(env.PathTable, path));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, path);
                pip1.AddOutputFile(artifactInPath);
                env.PipConstructionHelper.AddProcess(pip1);

                AssertSuccessGraphBuilding(env);
            }
        }

        [Theory]
        [InlineDataForOutputDirectory(@"\\dummyPath\Dir1", @"\\dummyPath\Dir1")]
        public void TestNoOutputsAsFileAndDirectoryAreAllowed(AddDirectory addDirectory, string dirPath, string explicitOutputPath)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Paths.CreateAbsolutePath(dirPath);
                AbsolutePath artifactInPath = env.Paths.CreateAbsolutePath(explicitOutputPath);

                XAssert.IsTrue(artifactInPath.IsWithin(env.PathTable, path));

                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, path);
                pip1.AddOutputFile(artifactInPath); // this is not allowed.

                var result = env.PipConstructionHelper.TryAddProcess(pip1);
                Assert.False(result, "Should fail");
            }
        }


        [Theory]
        [InlineDataForOutputDirectory(@"\\dummyPath\OpaqueDir", @"\\dummyPath\OpaqueDir")]
        [InlineDataForOutputDirectory(@"\\dummyPath\nonWritableRoot", @"\\dummyPath\nonWritableRoot\sharedOpaqueDir")]
        public void TestOutputDirectoriesCannotBeCreatedUnderANonWritableMount(AddDirectory addDirectory, string nonWritableMountRoot, string directory)
        {
            var pathTable = new PathTable();

            var nonWritableMount = new Mount
            {
                Name = PathAtom.Create(pathTable.StringTable, "NonWritableRoot"),
                Path = AbsolutePath.Create(pathTable, nonWritableMountRoot),
                IsReadable = true,
                IsWritable = false,
                TrackSourceFileChanges = true,
                Location = default(LocationData)
            };

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler(mounts: new List<IMount> { nonWritableMount }, pathTable: pathTable))
            {
                var directoryPath = AbsolutePath.Create(pathTable, directory);

                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, directoryPath);

                var result = env.PipConstructionHelper.TryAddProcess(pip1);
                Assert.False(result, "Should fail");
            }
        }

        [Theory]
        [InlineDataForOutputDirectory(true)]
        [InlineDataForOutputDirectory(false)]
        public void TestNoSourceSealDirectoryShouldLaterBeSpecifiedAboveOutputDirectory(AddDirectory addDirectory, bool allDirectories)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath path = env.Configuration.Layout.ObjectDirectory.Combine(
                    env.PathTable,
                    RelativePath.Create(env.PathTable.StringTable, @"a\b\c\Dir1"));
                var pip1 = CreatePipBuilderWithTag(env, "test");
                addDirectory(pip1, path);
                env.PipConstructionHelper.AddProcess(pip1);

                AbsolutePath ssdPath = env.Configuration.Layout.ObjectDirectory.Combine(env.PathTable, "a");

                env.PipConstructionHelper.SealDirectorySource(
                    ssdPath,
                    allDirectories ? SealDirectoryKind.SourceAllDirectories : SealDirectoryKind.SourceTopDirectoryOnly
                );

                AssertFailedGraphBuilding(env);
            }
        }
    }

    #region helpers

    /// <summary>
    /// Adds a directory to the pipBuilder. This can represent either a regular opaque directory or a shared one.
    /// </summary>
    public delegate void AddDirectory(ProcessBuilder processBuilder, AbsolutePath absolutePath);

    /// <summary>
    /// Works similarly as <see cref="InlineDataAttribute"/>, but runs each test twice: one time for the case of regular opaque directories and another time for shared opaque directories.
    /// The first argument passed to the test is a generic addDirectory method that adds a regular or shared opaque to the pip builder, depending on the case
    /// </summary>
    public sealed class InlineDataForOutputDirectoryAttribute : Xunit.Sdk.DataAttribute
    {
        private readonly object[] m_dataForRegularOpaque;
        private readonly object[] m_dataForSharedOpaque;

        private static readonly AddDirectory s_addOpaqueDirectory = (pipBuilder, directory) => pipBuilder.AddOutputDirectory(directory);

        private static readonly AddDirectory s_addSharedOpaqueDirectory =
            (pipBuilder, directory) => pipBuilder.AddOutputDirectory(directory, SealDirectoryKind.SharedOpaque);

        public InlineDataForOutputDirectoryAttribute(params object[] data)
        {
            m_dataForRegularOpaque = AddDirectoryCallback(s_addOpaqueDirectory, data);
            m_dataForSharedOpaque = AddDirectoryCallback(s_addSharedOpaqueDirectory, data);
        }

        public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod)
        {
            yield return m_dataForRegularOpaque;
            yield return m_dataForSharedOpaque;
        }

        private static object[] AddDirectoryCallback(AddDirectory addDirectoryCallback, object[] originalData)
        {
            var newData = new object[originalData.Length + 1];
            Array.Copy(originalData, 0, newData, 1, originalData.Length);
            newData[0] = addDirectoryCallback;

            return newData;
        }
    }

    #endregion
}
