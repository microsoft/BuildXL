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
    public class PackedExecutionTests : TemporaryStorageTestBase
    {
        public PackedExecutionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void PackedExecution_can_be_constructed()
        {
            PackedExecution packedExecution = new PackedExecution();

            XAssert.AreEqual(0, packedExecution.DirectoryTable.Count);
            XAssert.AreEqual(0, packedExecution.FileTable.Count);
            XAssert.AreEqual(0, packedExecution.PathTable.Count);
            XAssert.AreEqual(0, packedExecution.PipTable.Count);
            XAssert.AreEqual(0, packedExecution.StringTable.Count);
            XAssert.AreEqual(0, packedExecution.WorkerTable.Count);
        }

        [Fact]
        public void PackedExecution_can_store_pips()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";            
            PipId id = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);

            XAssert.AreEqual(0, packedExecution.DirectoryTable.Count);
            XAssert.AreEqual(0, packedExecution.FileTable.Count);
            XAssert.AreEqual(0, packedExecution.PathTable.Count);
            XAssert.AreEqual(1, packedExecution.PipTable.Count);
            XAssert.AreEqual(4, packedExecution.StringTable.Count);
            XAssert.AreEqual(0, packedExecution.WorkerTable.Count);

            PipEntry entry = packedExecution.PipTable[id];
            XAssert.AreEqual(hash, entry.SemiStableHash);
            XAssert.AreEqual(name, packedExecution.PipTable.PipNameTable.GetText(entry.Name));
        }

        [Fact]
        public void PackedExecution_can_store_files()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            string path = "d:\\os\\bin\\shellcommon\\shell\\merged\\winmetadata\\appresolverux.winmd";
            FileId id = packedExecutionBuilder.FileTableBuilder.GetOrAdd(path, 1024 * 1024, default, default, default);

            XAssert.AreEqual(0, packedExecution.PipTable.Count);
            XAssert.AreEqual(1, packedExecution.FileTable.Count);
            XAssert.AreEqual(8, packedExecution.StringTable.Count);
            XAssert.AreEqual(0, packedExecution.WorkerTable.Count);

            XAssert.AreEqual(path, packedExecution.FileTable.PathTable.GetText(packedExecution.FileTable[id].Path));
        }

        [Fact]
        public void PackedExecution_can_store_workers()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            string workerName = "BIGWORKER";
            StringId workerNameId = packedExecutionBuilder.StringTableBuilder.GetOrAdd(workerName);
            WorkerId workerId = packedExecution.WorkerTable.Add(workerNameId);
            
            XAssert.AreEqual(0, packedExecution.PipTable.Count);
            XAssert.AreEqual(0, packedExecution.FileTable.Count);
            XAssert.AreEqual(1, packedExecution.StringTable.Count);
            XAssert.AreEqual(1, packedExecution.WorkerTable.Count);

            XAssert.AreEqual(workerName, new string(packedExecution.StringTable[packedExecution.WorkerTable[workerId]]));
        }

        [Fact]
        public void PackedExecution_can_save_and_load()
        {
            PackedExecution packedExecution = new PackedExecution();
            PackedExecution.Builder packedExecutionBuilder = new PackedExecution.Builder(packedExecution);

            long hash = 1;
            string name = "ShellCommon.Shell.ShellCommon.Shell.Merged.Winmetadata";
            PipId pipId = packedExecutionBuilder.PipTableBuilder.Add(hash, name, PipType.Process);
            string path = "d:\\os\\bin\\shellcommon\\shell\\merged\\winmetadata\\appresolverux.winmd";
            packedExecutionBuilder.FileTableBuilder.GetOrAdd(path, 1024 * 1024, default, default, default);
            string workerName = "BIGWORKER";
            packedExecutionBuilder.WorkerTableBuilder.GetOrAdd(workerName);

            XAssert.AreEqual(1, packedExecution.PipTable.Count);
            XAssert.AreEqual(1, packedExecution.FileTable.Count);
            XAssert.AreEqual(13, packedExecution.StringTable.Count);

            packedExecution.SaveToDirectory(TemporaryDirectory);

            PackedExecution packedExecution2 = new PackedExecution();
            packedExecution2.LoadFromDirectory(TemporaryDirectory);

            XAssert.AreEqual(1, packedExecution2.PipTable.Count);
            XAssert.AreEqual(1, packedExecution2.FileTable.Count);
            XAssert.AreEqual(13, packedExecution2.StringTable.Count);

            PipId pipId2 = packedExecution2.PipTable.Ids.First();
            XAssert.AreEqual(name, packedExecution2.PipTable.PipNameTable.GetText(packedExecution2.PipTable[pipId].Name));

            FileId fileId2 = packedExecution2.FileTable.Ids.First();
            XAssert.AreEqual(path, packedExecution2.FileTable.PathTable.GetText(packedExecution2.FileTable[fileId2].Path));

            WorkerId workerId2 = packedExecution2.WorkerTable.Ids.First();
            XAssert.AreEqual(workerName, new string(packedExecution2.StringTable[packedExecution2.WorkerTable[workerId2]]));
        }
    }
}
