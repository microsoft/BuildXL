// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class RocksDbLogsManagerTests : TestBase
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private new readonly IAbsFileSystem _fileSystem;
        private readonly DisposableDirectory _workingDirectory;

        public RocksDbLogsManagerTests(ITestOutputHelper? output = null)
            : base(TestGlobal.Logger, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = Guid.NewGuid().ToString();

            _fileSystem = new MemoryFileSystem(_clock);
            _workingDirectory = new DisposableDirectory(_fileSystem, Path.Combine(uniqueOutputFolder, nameof(RocksDbLogsManagerTests)));
        }

        [Fact]
        public Task CanBackupSingleFile()
        {
            return WithLogManager(TimeSpan.FromDays(7),
                async (context, manager) =>
                {
                    var instanceFolder = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 0, numLogFiles: 1);
                    var backupFolder = await BackupAsync(manager, context, instanceFolder);
                    Assert.True(_fileSystem.DirectoryExists(backupFolder));

                    var files = _fileSystem.EnumerateFiles(backupFolder, EnumerateOptions.None).ToList();
                    Assert.Equal(files.Count, 1);
                    Assert.Equal(files[0].FullPath.FileName, "LOG");
                });
        }

        [Fact]
        public Task CanBackupMultipleFiles()
        {
            return WithLogManager(TimeSpan.FromDays(7),
                async (context, manager) =>
                {
                    var instanceFolder = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 10, numLogFiles: 10);
                    var backupFolder = await BackupAsync(manager, context, instanceFolder);
                    Assert.True(_fileSystem.DirectoryExists(backupFolder));

                    var files = _fileSystem.EnumerateFiles(backupFolder, EnumerateOptions.None).ToList();
                    Assert.Equal(files.Count, 10);
                });
        }

        [Fact]
        public Task DoesNotBackupIfThereArentAnyLogs()
        {
            return WithLogManager(TimeSpan.FromDays(7),
                async (context, manager) =>
                {
                    var instanceFolder = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 10, numLogFiles: 0);
                    var backupFolder = await BackupAsync(manager, context, instanceFolder);
                    Assert.False(_fileSystem.DirectoryExists(backupFolder));
                });
        }

        [Fact]
        public Task CollectsGarbage()
        {
            return WithLogManager(TimeSpan.FromDays(7),
                async (context, manager) =>
                {
                    var instanceFolder = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 0, numLogFiles: 10);
                    var backupFolder = await BackupAsync(manager, context, instanceFolder);
                    Assert.True(_fileSystem.DirectoryExists(backupFolder));

                    manager.GarbageCollect(context).ShouldBeSuccess();
                    Assert.True(_fileSystem.DirectoryExists(backupFolder));

                    _clock.UtcNow += TimeSpan.FromDays(8);
                    manager.GarbageCollect(context).ShouldBeSuccess();

                    Assert.False(_fileSystem.DirectoryExists(backupFolder));
                });
        }

        [Fact]
        public Task DoesNotCollectUsefulLogs()
        {
            return WithLogManager(TimeSpan.FromDays(7),
                async (context, manager) =>
                {
                    var instanceFolder = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 10, numLogFiles: 10);
                    var backupFolder = await BackupAsync(manager, context, instanceFolder);
                    Assert.True(_fileSystem.DirectoryExists(backupFolder));

                    _clock.UtcNow += TimeSpan.FromDays(8);

                    var instanceFolder2 = await GenerateRocksDbInstanceFolderAsync(numSstFiles: 10, numLogFiles: 10);
                    var backupFolder2 = await BackupAsync(manager, context, instanceFolder2);
                    Assert.True(_fileSystem.DirectoryExists(backupFolder2));

                    _clock.UtcNow += TimeSpan.FromDays(2);

                    manager.GarbageCollect(context).ShouldBeSuccess();

                    Assert.False(_fileSystem.DirectoryExists(backupFolder));
                    Assert.True(_fileSystem.DirectoryExists(backupFolder2));
                });
        }

        private Task WithLogManager(TimeSpan retention, Func<OperationContext, RocksDbLogsManager, Task> action)
        {
            var backupFolder = _workingDirectory.Path / "backup";
            _fileSystem.CreateDirectory(backupFolder);
            Assert.True(_fileSystem.DirectoryExists(backupFolder));

            var tracingContext = new Context(TestGlobal.Logger);
            var operationContext = new OperationContext(tracingContext);

            var manager = new RocksDbLogsManager(_clock, _fileSystem, backupFolder, retention);
            return action(operationContext, manager);
        }

        private async Task CreateEmptyFileAsync(AbsolutePath path)
        {
            await _fileSystem.CreateEmptyFileAsync(path);
            Assert.True(_fileSystem.FileExists(path));
        }

        public async Task<AbsolutePath> GenerateRocksDbInstanceFolderAsync(int numSstFiles, int numLogFiles)
        {
            var name = Guid.NewGuid().ToString().Substring(0, 5);
            var path = _workingDirectory.Path / name;

            _fileSystem.CreateDirectory(path);
            Assert.True(_fileSystem.DirectoryExists(path));

            await CreateEmptyFileAsync(path / "MANIFEST");
            await CreateEmptyFileAsync(path / "OPTIONS");

            if (numSstFiles > 0)
            {
                foreach (var i in Enumerable.Range(0, numSstFiles))
                {
                    await CreateEmptyFileAsync(path / $"{i}.sst");
                }
            }

            if (numLogFiles > 0)
            {
                await CreateEmptyFileAsync(path / "LOG");

                foreach (var i in Enumerable.Range(0, numLogFiles - 1))
                {
                    await CreateEmptyFileAsync(path / $"LOG{i}");
                }
            }

            return path;
        }

        private static async Task<AbsolutePath> BackupAsync(RocksDbLogsManager manager, OperationContext context, AbsolutePath instanceFolder)
        {
            return (await manager.BackupAsync(context, instanceFolder)).ShouldBeSuccess().Value!;
        }
    }
}
