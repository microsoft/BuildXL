// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedExecution;
using BuildXL.Utilities.PackedTable;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class PackedExecutionDerivedTableTests : TemporaryStorageTestBase
    {
        public PackedExecutionDerivedTableTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void DerivedTable_can_store_one_element()
        {
            PackedExecution packedExecution = new PackedExecution();
            SingleValueTable<PipId, int> derivedTable = new SingleValueTable<PipId, int>(packedExecution.PipTable);

            XAssert.AreEqual(0, derivedTable.Count);
            XAssert.AreEqual(0, derivedTable.Ids.Count());

            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);

            XAssert.AreEqual(0, derivedTable.Count);
            XAssert.AreEqual(0, derivedTable.Ids.Count());

            derivedTable.Add(1000);

            XAssert.AreEqual(1, derivedTable.Count);
            XAssert.AreEqual(1, derivedTable.Ids.Count());
            XAssert.AreEqual(1000, derivedTable[pipId]);
        }

        [Fact]
        public void DerivedTable_can_save_and_load()
        {
            PackedExecution packedExecution = new PackedExecution();
            SingleValueTable<PipId, int> derivedTable = new SingleValueTable<PipId, int>(packedExecution.PipTable);
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);

            derivedTable.Add(1000);

            derivedTable.SaveToFile(TemporaryDirectory, "PipInt.bin");

            SingleValueTable<PipId, int> derivedTable2 = new SingleValueTable<PipId, int>(packedExecution.PipTable);
            derivedTable2.LoadFromFile(TemporaryDirectory, "PipInt.bin");

            XAssert.AreEqual(1, derivedTable2.Count);
            XAssert.AreEqual(1, derivedTable2.Ids.Count());
            XAssert.AreEqual(1000, derivedTable2[pipId]);
        }
    }
}
