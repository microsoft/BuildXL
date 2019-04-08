// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Unit tests for the graph construction rules around sealing directories.
    /// These tests do not execute pips.
    /// </summary>
    public class SealedDirectoryTableTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SealedDirectoryTableTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSealDirectoryTable()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                SealedDirectoryTable table = new SealedDirectoryTable(env.PathTable);

                AbsolutePath directoryPath = env.Paths.CreateAbsolutePath(env.ObjectRoot, "seal");
                AbsolutePath otherDirectoryPath = env.Paths.CreateAbsolutePath(env.ObjectRoot, "otherSeal");
                AbsolutePath unsealedDirectoryPath = env.Paths.CreateAbsolutePath(env.ObjectRoot, "unseal");
                FileArtifact a = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "a"));
                FileArtifact b = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "subdir", "b"));
                FileArtifact c = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "subdir", "c"));

                // This file is not sealed until the full seal is created
                FileArtifact d = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "d"));

                FileArtifact other_a = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(otherDirectoryPath, "a"));
                FileArtifact other_b = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(otherDirectoryPath, "b"));

                FileArtifact z_unsealed = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(unsealedDirectoryPath, "z"));

                var partialSeal1 = CreatePartialSeal(env, directoryPath, a, b);
                var partialSeal2 = CreatePartialSeal(env, directoryPath, a, b, c);
                ReserveAndAddSeal(table, partialSeal1);
                ReserveAndAddSeal(table, partialSeal2);

                // Create other seals to ensure that seals for different paths are independent
                var otherPartialSeal = CreatePartialSeal(env, otherDirectoryPath, other_a);
                var otherFullSeal = CreateFullSeal(env, otherDirectoryPath, other_a, other_b);
                ReserveAndAddSeal(table, otherPartialSeal);
                ReserveAndAddSeal(table, otherFullSeal);

                // Assert no full seals for partial sealed files
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(a).IsValid);
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(b).IsValid);
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(c).IsValid);

                // Assert no partial/full seals for unsealed files under directory
                XAssert.IsFalse(table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, d).IsValid);
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(d).IsValid);

                // Assert no partial/full seals for unsealed files
                XAssert.IsFalse(table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, z_unsealed).IsValid);
                XAssert.IsFalse(table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, z_unsealed).IsValid);
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(z_unsealed).IsValid);

                // Assert that minimal partial seal is found for sealed files
                XAssert.AreEqual(partialSeal1.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, a));
                XAssert.AreEqual(partialSeal1.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, b));
                XAssert.AreEqual(partialSeal2.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, c));

                // Assert that partial seal is not found for unsealed files
                XAssert.IsFalse(table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, z_unsealed).IsValid);

                // Create a full seal and verify update to table
                // We re-assert many of the conditions above to ensure that adding a full seal
                // only changes result of the intended calls
                var fullSeal = CreateFullSeal(env, directoryPath, a, b, c, d);
                ReserveAndAddSeal(table, fullSeal);

                // Assert that minimal partial seal DirectoryArtifact is found for sealed files (TryFindSealDirectoryContainingFileArtifact)
                XAssert.AreEqual(partialSeal1.Directory, table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, a));
                XAssert.AreEqual(partialSeal1.Directory, table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, b));
                XAssert.AreEqual(partialSeal2.Directory, table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, c));
                XAssert.AreEqual(fullSeal.Directory, table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, d));

                // Assert that minimal partial seal PipID is found for sealed files (TryFindSealDirectoryContainingFileArtifact)
                XAssert.AreEqual(partialSeal1.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, a));
                XAssert.AreEqual(partialSeal1.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, b));
                XAssert.AreEqual(partialSeal2.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, c));
                XAssert.AreEqual(fullSeal.PipId, table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, d));

                // Assert full seal for partial sealed files
                XAssert.AreEqual(fullSeal.Directory, table.TryFindFullySealedDirectoryArtifactForFile(a));
                XAssert.AreEqual(fullSeal.Directory, table.TryFindFullySealedDirectoryArtifactForFile(b));
                XAssert.AreEqual(fullSeal.Directory, table.TryFindFullySealedDirectoryArtifactForFile(c));
                XAssert.AreEqual(fullSeal.Directory, table.TryFindFullySealedDirectoryArtifactForFile(c));

                // Assert no partial/full seals for unsealed files
                XAssert.IsFalse(table.TryFindSealDirectoryContainingFileArtifact(env.PipTable, z_unsealed).IsValid);
                XAssert.IsFalse(table.TryFindSealDirectoryPipContainingFileArtifact(env.PipTable, z_unsealed).IsValid);
                XAssert.IsFalse(table.TryFindFullySealedDirectoryArtifactForFile(z_unsealed).IsValid);
            }
        }

        [Fact]
        public void TestAddInitializedSealDirectoryWhilePatching()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                SealedDirectoryTable table = new SealedDirectoryTable(env.PathTable);
                AbsolutePath directoryPath = env.Paths.CreateAbsolutePath(env.ObjectRoot, "seal");
                FileArtifact a = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "a"));
                var partialSeal = CreatePartialSeal(env, directoryPath, a);
                var directoryArtifact = DirectoryArtifact.CreateDirectoryArtifactForTesting(directoryPath, 1);
                partialSeal.SetDirectoryArtifact(directoryArtifact);
                table.StartPatching(); // if not in IsPatching state, 'ReserveAndAddSeal' would fail
                ReserveAndAddSeal(table, partialSeal);
                table.FinishPatching();
            }
        }

        [Fact]
        public void TestCallingSetDirectoryArtifactMultipleTimesWithSameDirectoryArtifactShouldBeAllowed()
        {
            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler())
            {
                SealedDirectoryTable table = new SealedDirectoryTable(env.PathTable);
                AbsolutePath directoryPath = env.Paths.CreateAbsolutePath(env.ObjectRoot, "seal");
                FileArtifact a = FileArtifact.CreateSourceFile(env.Paths.CreateAbsolutePath(directoryPath, "a"));
                var partialSeal = CreatePartialSeal(env, directoryPath, a);
                var directoryArtifact = DirectoryArtifact.CreateDirectoryArtifactForTesting(directoryPath, 1);
                partialSeal.SetDirectoryArtifact(directoryArtifact);
                partialSeal.SetDirectoryArtifact(directoryArtifact); // setting the same directory artifact multiple times should be fine
            }
        }

        private static SealDirectory CreatePartialSeal(TestEnv env, AbsolutePath path, params FileArtifact[] contents)
        {
            return CreateSeal(env, path, partial: true, contents: contents);
        }

        private static SealDirectory CreateFullSeal(TestEnv env, AbsolutePath path, params FileArtifact[] contents)
        {
            return CreateSeal(env, path, partial: false, contents: contents);
        }

        private static SealDirectory CreateSeal(TestEnv env, AbsolutePath path, bool partial, FileArtifact[] contents)
        {
            var seal = new SealDirectory(
                path,
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(contents, OrdinalFileArtifactComparer.Instance),
                kind: partial ? SealDirectoryKind.Partial : SealDirectoryKind.Full,
                provenance: env.CreatePipProvenance(StringId.Invalid),
                tags: ReadOnlyArray<StringId>.Empty,
                patterns: ReadOnlyArray<StringId>.Empty);

            env.PipTable.Add(((PipGraph.Builder)(env.PipGraph)).MutableDataflowGraph.CreateNode().Value, seal);

            return seal;
        }

        private void ReserveAndAddSeal(SealedDirectoryTable table, SealDirectory seal)
        {
            var directoryArtifact = table.ReserveDirectoryArtifact(seal);
            seal.SetDirectoryArtifact(directoryArtifact);
            table.AddSeal(seal);

            XAssert.IsTrue(seal.IsInitialized);

            XAssert.IsTrue(table.TryGetSealForDirectoryArtifact(directoryArtifact, out PipId pipId));
            XAssert.AreEqual(seal.PipId, pipId);
            XAssert.IsTrue(directoryArtifact.IsValid);
        }
    }
}
