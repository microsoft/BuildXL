// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedExecution;
using BuildXL.Utilities.PackedTable;
using System;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedExecutionRelationTableTests : TemporaryStorageTestBase
    {
        public PackedExecutionRelationTableTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void RelationTable_can_store_one_relation()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);
            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);

            packedExecution.ConstructRelationTables();

            RelationTable<PipId, PipId> relationTable = packedExecution.PipDependencies;

            relationTable.Add(new[] { pipId }.AsSpan());

            XAssert.AreEqual(1, relationTable[pipId].Length);

            ReadOnlySpan<PipId> relations = relationTable[pipId];
            XAssert.AreEqual(pipId, relations[0]);

            RelationTable<PipId, PipId> inverseRelationTable = relationTable.Invert();

            XAssert.AreEqual(1, inverseRelationTable[pipId].Length);
            XAssert.AreEqual(pipId, inverseRelationTable[pipId][0]);
        }

        [Fact]
        public void RelationTable_can_store_multiple_relations()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);
            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);
            PipId pipId2 = packedExecutionBuilder.PipTableBuilder.Add(hash + 1, $"{name}2", PipType.Process);
            PipId pipId3 = packedExecutionBuilder.PipTableBuilder.Add(hash + 2, $"{name}3", PipType.Process);

            XAssert.AreNotEqual(pipId, pipId2);
            XAssert.AreNotEqual(pipId, pipId3);
            XAssert.AreNotEqual(pipId2, pipId3);

            packedExecution.ConstructRelationTables();

            RelationTable<PipId, PipId> relationTable = packedExecution.PipDependencies;

            relationTable.Add(new[] { pipId2, pipId3 }.AsSpan());

            XAssert.AreEqual(2, relationTable[pipId].Length);

            ReadOnlySpan<PipId> relations = relationTable[pipId];

            XAssert.AreEqual(pipId2, relations[0]);
            XAssert.AreEqual(pipId3, relations[1]);

            relationTable.Add(new[] { pipId }.AsSpan());

            XAssert.AreEqual(1, relationTable[pipId2].Length);

            relationTable.Add(new[] { pipId, pipId2, pipId3 }.AsSpan());

            XAssert.AreEqual(3, relationTable[pipId3].Length);
            XAssert.AreArraysEqual(new[] { pipId2, pipId3 }, relationTable[pipId].ToArray(), true);
            XAssert.AreArraysEqual(new[] { pipId, pipId2, pipId3 }, relationTable[pipId3].ToArray(), true);

            RelationTable<PipId, PipId> inverseRelationTable = relationTable.Invert();

            XAssert.AreArraysEqual(new[] { pipId2, pipId3 }, inverseRelationTable[pipId].ToArray(), true);
            XAssert.AreArraysEqual(new[] { pipId, pipId3 }, inverseRelationTable[pipId2].ToArray(), true);
            XAssert.AreArraysEqual(new[] { pipId, pipId3 }, inverseRelationTable[pipId3].ToArray(), true);
        }

        [Fact]
        public void RelationTable_can_be_built()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);
            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);
            PipId pipId2 = packedExecutionBuilder.PipTableBuilder.Add(hash + 1, $"{name}2", PipType.Process);
            PipId pipId3 = packedExecutionBuilder.PipTableBuilder.Add(hash + 2, $"{name}3", PipType.Process);

            packedExecution.ConstructRelationTables();

            RelationTable<PipId, PipId> relationTable = packedExecution.PipDependencies;
            RelationTable<PipId, PipId>.Builder builder = new RelationTable<PipId, PipId>.Builder(relationTable);

            // add relations in any order
            builder.Add(pipId3, pipId2);
            builder.Add(pipId3, pipId);
            builder.Add(pipId, pipId3);
            builder.Add(pipId, pipId2);

            // done adding relations; flush to table
            builder.Complete();

            XAssert.AreArraysEqual(new[] { pipId2, pipId3 }, relationTable[pipId].ToArray(), true);
            XAssert.AreArraysEqual(new PipId[0], relationTable[pipId2].ToArray(), true);
            XAssert.AreArraysEqual(new[] { pipId, pipId2 }, relationTable[pipId3].ToArray(), true);

            XAssert.AreArraysEqual(new[] { pipId2, pipId3 }, relationTable.Enumerate(pipId).ToArray(), true);
            XAssert.AreArraysEqual(new PipId[0], relationTable.Enumerate(pipId2).ToArray(), true);
            XAssert.AreArraysEqual(new[] { pipId, pipId2 }, relationTable.Enumerate(pipId3).ToArray(), true);
        }
    }
}
