// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedExecution;
using BuildXL.Utilities.PackedTable;
using System.Linq;
using System.Runtime.CompilerServices;
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

        [Fact]
        public void DerivedTable_can_save_and_load_large()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            // This variable is used to ensure the remaining test variables are set with the correct relationship.
            const int RecordsPerSlice = 10;

            // Set the IdealSliceSizeBytes to follow the above RecordsPerSlice value.
            // Offset by 1 to avoid the slice size being a perfect multiplier of the type size (to catch bugs).
            FileSpanUtilities.IdealSliceSizeBytes = Unsafe.SizeOf<FileEntry>() * RecordsPerSlice + 1;

            // Write enough records to fully populate 2 slices and partially populate the final slice.
            int recordCount = RecordsPerSlice * 2 + 1;
            FileId[] fileIDs = new FileId[recordCount];

            for (int i = 0; i < recordCount; i++)
            {
                FileEntry entry = new FileEntry(
                    name: new NameId(i + 1),
                    sizeInBytes: i,
                    contentFlags: ContentFlags.None,
                    hash: new FileHash(),
                    rewriteCount: 0);
                fileIDs[i] = packedExecutionBuilder.FileTableBuilder.GetOrAdd(entry);
            }

            packedExecution.FileTable.SaveToFile(TemporaryDirectory, "FileTable.bin");

            SingleValueTable<FileId, FileEntry> derivedTable2 = new SingleValueTable<FileId, FileEntry>(packedExecution.FileTable);
            derivedTable2.LoadFromFile(TemporaryDirectory, "FileTable.bin");

            XAssert.AreEqual(recordCount, derivedTable2.Count);
            XAssert.AreEqual(recordCount, derivedTable2.Ids.Count());

            for (int i = 0; i < recordCount; i++)
            {
                FileEntry entry = derivedTable2[fileIDs[i]];
                XAssert.AreEqual(i, entry.SizeInBytes);
            }
        }
    }
}
