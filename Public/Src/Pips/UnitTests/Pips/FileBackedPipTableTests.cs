// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Pips
{
    public sealed class FileBackedPipTableTests : XunitBuildXLTest
    {
        public FileBackedPipTableTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void FileBackedTableDoesNotDeriveFromEagerTable()
        {
            Assert.False(typeof(PipTable).IsAssignableFrom(typeof(FileBackedPipTable)));
            Assert.True(typeof(IPipTable).IsAssignableFrom(typeof(FileBackedPipTable)));
        }

        [Fact]
        public async Task EmptyFileBackedTableCompletes()
        {
            var pathTable = new PathTable();
            using (var table = new FileBackedPipTable(
                pathTable,
                new SymbolTable(pathTable.StringTable),
                initialBufferSize: 1024,
                maxDegreeOfParallelism: 1,
                debug: false))
            {
                table.StopBackgroundSerialization();
                await table.WhenDone();
                Assert.Empty(table.StableKeys);
            }
        }

        [Fact]
        public async Task StableKeysIndexOfReturnsMinusOneForMissingPips()
        {
            var pathTable = new PathTable();
            using (var table = new FileBackedPipTable(
                pathTable,
                new SymbolTable(pathTable.StringTable),
                initialBufferSize: 1024,
                maxDegreeOfParallelism: 1,
                debug: false))
            {
                table.Add(
                    1,
                    new HashSourceFile(
                        FileArtifact.CreateSourceFile(
                            AbsolutePath.Create(pathTable, Path.Combine(Path.GetTempPath(), "source.txt")))));

                table.StopBackgroundSerialization();
                await table.WhenDone();

                Assert.Equal(0, table.StableKeys.IndexOf(new PipId(1)));
                Assert.Equal(-1, table.StableKeys.IndexOf(PipId.Invalid));
                Assert.Equal(-1, table.StableKeys.IndexOf(new PipId(2)));
            }
        }

        [Fact]
        public void SerializationRejectsSparsePipIdsBeforeWriting()
        {
            var pathTable = new PathTable();
            using (var table = new FileBackedPipTable(
                pathTable,
                new SymbolTable(pathTable.StringTable),
                initialBufferSize: 1024,
                maxDegreeOfParallelism: 1,
                debug: false))
            {
                table.Add(
                    2,
                    new HashSourceFile(
                        FileArtifact.CreateSourceFile(
                            AbsolutePath.Create(pathTable, Path.Combine(Path.GetTempPath(), "source.txt")))));

                using (var stream = new MemoryStream())
                using (var writer = new BuildXLWriter(debug: false, stream, leaveOpen: true, logStats: false))
                {
                    Assert.Throws<InvalidOperationException>(() => table.Serialize(writer, maxDegreeOfParallelism: -1));
                    Assert.Equal(0, stream.Length);
                }
            }
        }

        [Fact]
        public async Task FactoryPreservesEagerFormat()
        {
            var pathTable = new PathTable();
            var symbolTable = new SymbolTable(pathTable.StringTable);
            using (var serialized = new MemoryStream())
            {
                using (var table = new PipTable(pathTable, symbolTable, initialBufferSize: 1024, maxDegreeOfParallelism: 1, debug: false))
                {
                    table.Add(
                        1,
                        new HashSourceFile(
                            FileArtifact.CreateSourceFile(
                                AbsolutePath.Create(pathTable, Path.Combine(Path.GetTempPath(), "source.txt")))));

                    using (var writer = new BuildXLWriter(debug: false, serialized, leaveOpen: true, logStats: false))
                    {
                        table.Serialize(writer, maxDegreeOfParallelism: -1);
                    }
                }

                serialized.Position = 0;
                using (var reader = new BuildXLReader(debug: false, serialized, leaveOpen: true))
                using (var table = await PipTableFactory.DeserializeAsync(
                    reader,
                    Task.FromResult(pathTable),
                    Task.FromResult(symbolTable),
                    initialBufferSize: 1024,
                    maxDegreeOfParallelism: 1,
                    debug: false))
                {
                    Assert.IsType<PipTable>(table);
                    Assert.Equal(PipType.HashSourceFile, table.GetPipType(new PipId(1)));
                }
            }
        }

        [Fact]
        public async Task SerializesHydratesAndReloads()
        {
            var pathTable = new PathTable();
            var symbolTable = new SymbolTable(pathTable.StringTable);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string serializedPath = Path.Combine(directory, "PipTable");
            Directory.CreateDirectory(directory);

            try
            {
                using (var table = new FileBackedPipTable(
                    pathTable,
                    symbolTable,
                    initialBufferSize: 1024,
                    maxDegreeOfParallelism: 1,
                    debug: false,
                    storageDirectory: directory))
                {
                    var pip = new HashSourceFile(
                        FileArtifact.CreateSourceFile(
                            AbsolutePath.Create(pathTable, Path.Combine(directory, "source.txt"))));
                    var pipId = table.Add(1, pip);

                    table.StopBackgroundSerialization();
                    await table.WhenDone();

                    Assert.Equal(PipType.HashSourceFile, table.GetPipType(pipId));
                    Assert.Equal(pipId, table.HydratePip(pipId, PipQueryContext.Test).PipId);
                    Assert.Equal(0, table.Size);

                    using (var stream = new FileStream(serializedPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new BuildXLWriter(debug: false, stream, leaveOpen: false, logStats: false))
                    {
                        table.Serialize(writer, maxDegreeOfParallelism: -1);
                    }
                }

                using (var stream = new FileStream(serializedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BuildXLReader(debug: false, stream, leaveOpen: false, bufferSize: 0))
                using (var table = await PipTableFactory.DeserializeAsync(
                    reader,
                    Task.FromResult(pathTable),
                    Task.FromResult(symbolTable),
                    initialBufferSize: 1024,
                    maxDegreeOfParallelism: 1,
                    debug: false))
                {
                    Assert.IsType<FileBackedPipTable>(table);
                    Assert.Equal(PipType.HashSourceFile, table.GetPipType(new PipId(1)));
                    Assert.Equal(new PipId(1), table.HydratePip(new PipId(1), PipQueryContext.Test).PipId);
                    Assert.Equal(0, table.Size);
                }
            }
            finally
            {
                if (File.Exists(serializedPath))
                {
                    File.Delete(serializedPath);
                }

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory);
                }
            }
        }
    }
}
