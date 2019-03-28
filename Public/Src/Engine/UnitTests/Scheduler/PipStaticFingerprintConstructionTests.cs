// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Scheduler.SchedulerTestHelper;

namespace Test.BuildXL.Scheduler
{
    public class PipStaticFingerprintConstructionTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public PipStaticFingerprintConstructionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestPartialSealDirectoryMembersAffectFingerprints()
        {
            ContentFingerprint fingerprint1 = CreateFingerprintForPartialSealWithMember("f.txt");
            ContentFingerprint fingerprint2 = CreateFingerprintForPartialSealWithMember("f.txt");

            XAssert.AreEqual(fingerprint1, fingerprint2);

            fingerprint1 = CreateFingerprintForPartialSealWithMember("f.txt");
            fingerprint2 = CreateFingerprintForPartialSealWithMember("g.txt");

            XAssert.AreNotEqual(fingerprint1, fingerprint2);
        }

        [Fact]
        public void TestCompositeSharedOpaqueMembersAffectFingerprints()
        {
            ContentFingerprint fingerprint1 = CreateFingerprintForCompositeSharedOpaque(@"\\root", @"\\root\so1", @"\\root\so2");
            ContentFingerprint fingerprint2 = CreateFingerprintForCompositeSharedOpaque(@"\\root", @"\\root\so1", @"\\root\so2");

            XAssert.AreEqual(fingerprint1, fingerprint2);

            fingerprint1 = CreateFingerprintForCompositeSharedOpaque(@"\\root", @"\\root\so1", @"\\root\so2");
            fingerprint2 = CreateFingerprintForCompositeSharedOpaque(@"\\root", @"\\root\so1", @"\\root\so3");

            XAssert.AreNotEqual(fingerprint1, fingerprint2);
        }

        [Fact]
        public void TestSharedOpaqueProducersAffectFingerprints()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                var root = env.Paths.CreateAbsolutePath(@"\\root");

                // We create two shared opaques with same root and empty content
                var sharedOpaque1 = CreateSharedOpaque(env, root);
                var sharedOpaque2 = CreateSharedOpaque(env, root);
                var graph = AssertSuccessGraphBuilding(env);

                // But their fingerprints should be different
                var fingerprint1 = CreateFingerprintForSharedOpaque(sharedOpaque1, graph);
                var fingerprint2 = CreateFingerprintForSharedOpaque(sharedOpaque2, graph);

                XAssert.AreNotEqual(fingerprint1, fingerprint2);
            }
        }

        private ContentFingerprint CreateFingerprintForPartialSealWithMember(string fileName)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                AbsolutePath root = env.Paths.CreateAbsolutePath(@"\\dummyPath\Root");
                FileArtifact member = FileArtifact.CreateSourceFile(root.Combine(env.PathTable, fileName));

                var staticDirectory = env.PipConstructionHelper.SealDirectoryPartial(
                    root,
                    new[] {member});

                var pipBuilder = CreatePipBuilderWithTag(env, nameof(TestPartialSealDirectoryMembersAffectFingerprints));
                var outputPath = env.Paths.CreateAbsolutePath(@"\\dummyPath\out");
                pipBuilder.AddOutputFile(outputPath);
                pipBuilder.AddInputDirectory(staticDirectory);

                env.PipConstructionHelper.AddProcess(pipBuilder);
                var graph = AssertSuccessGraphBuilding(env);
                var producerId = graph.TryGetProducer(FileArtifact.CreateOutputFile(outputPath));

                XAssert.IsTrue(producerId.IsValid);
                XAssert.IsTrue(graph.TryGetPipFingerprint(producerId, out ContentFingerprint fingerprint));

                return fingerprint;
            }
        }

        private ContentFingerprint CreateFingerprintForCompositeSharedOpaque(string composedSharedOpaqueRoot, params string[] sharedOpaqueMembers)
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                var sharedOpaqueDirectoryArtifactMembers = new DirectoryArtifact[sharedOpaqueMembers.Length];
                for (int i = 0; i < sharedOpaqueMembers.Length; i++)
                {
                    sharedOpaqueDirectoryArtifactMembers[i] = CreateSharedOpaque(env, env.Paths.CreateAbsolutePath(sharedOpaqueMembers[i]));
                }

                var success = env.PipConstructionHelper.TryComposeSharedOpaqueDirectory(
                    env.Paths.CreateAbsolutePath(composedSharedOpaqueRoot), 
                    sharedOpaqueDirectoryArtifactMembers, 
                    description: null, 
                    tags: new string[0], 
                    out var sharedOpaqueDirectory);
                XAssert.IsTrue(success);

                var graph = AssertSuccessGraphBuilding(env);
                var fingerprint = CreateFingerprintForSharedOpaque(sharedOpaqueDirectory, graph);

                return fingerprint;
            }
        }

        private static ContentFingerprint CreateFingerprintForSharedOpaque(DirectoryArtifact sharedOpaqueDirectory, PipGraph graph)
        {
            var sealDirectoryPipId = graph.GetSealedDirectoryNode(sharedOpaqueDirectory).ToPipId();
            XAssert.IsTrue(graph.TryGetPipFingerprint(sealDirectoryPipId, out ContentFingerprint fingerprint));
            return fingerprint;
        }

        private DirectoryArtifact CreateSharedOpaque(TestEnv env, AbsolutePath root)
        {
            var pipBuilder = CreatePipBuilderWithTag(env, nameof(TestPartialSealDirectoryMembersAffectFingerprints));
            pipBuilder.AddOutputDirectory(root, SealDirectoryKind.SharedOpaque);
            var outputs = env.PipConstructionHelper.AddProcess(pipBuilder);

            outputs.TryGetOutputDirectory(root, out var sharedOpaqueDirectoryArtifact);
            return sharedOpaqueDirectoryArtifact.Root;
        }
    }
}
