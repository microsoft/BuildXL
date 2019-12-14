// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal class RocksDbLogsManager
    {
        private readonly Tracer _tracer = new Tracer(nameof(RocksDbLogsManager));
        private readonly IClock _clock;
        private readonly IAbsFileSystem _fileSystem;

        private readonly AbsolutePath _backupPath;
        private readonly TimeSpan _retention;

        private readonly LockSet<AbsolutePath> _locks = new LockSet<AbsolutePath>();

        public RocksDbLogsManager(IClock clock, IAbsFileSystem fileSystem, AbsolutePath backupPath, TimeSpan retention)
        {
            Contract.Requires(retention > TimeSpan.Zero);

            _clock = clock;
            _fileSystem = fileSystem;
            _backupPath = backupPath;
            _retention = retention;
        }

        public async Task<Result<AbsolutePath>> BackupAsync(OperationContext context, AbsolutePath instancePath, string? name = null)
        {
            int numCopiedFiles = 0;
            AbsolutePath? backupPath = null;
            return await context.PerformOperationAsync(_tracer, async () =>
            {
                var backupTime = _clock.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var backupName = backupTime;
                if (!string.IsNullOrEmpty(name))
                {
                    backupName += $"-{name}";
                }
                backupPath = _backupPath / backupName;

                // Unlikely, but it is possible for GC to start running and think that it should purge this directory,
                // this avoids the scenario.
                using var _ = await _locks.AcquireAsync(backupPath);

                if (_fileSystem.DirectoryExists(backupPath))
                {
                    _fileSystem.DeleteDirectory(backupPath, DeleteOptions.All);
                }
                _fileSystem.CreateDirectory(backupPath);

                // See: https://github.com/facebook/rocksdb/wiki/rocksdb-basics#database-debug-logs
                _fileSystem.EnumerateFiles(instancePath, "*LOG*", false,
                    async fileInfo =>
                    {
                        var fileName = fileInfo.FullPath.FileName;
                        var targetFilePath = backupPath / fileName;

                        await _fileSystem.CopyFileAsync(fileInfo.FullPath, targetFilePath, replaceExisting: true);

                        ++numCopiedFiles;
                    });

                if (numCopiedFiles == 0)
                {
                    _fileSystem.DeleteDirectory(backupPath, DeleteOptions.All);
                }

                return new Result<AbsolutePath>(backupPath);
            }, extraEndMessage: _ => $"From=[{instancePath}] To=[{backupPath?.ToString() ?? "Unknown"}] NumCopiedFiles=[{numCopiedFiles}]");
        }

        public BoolResult GarbageCollect(OperationContext context)
        {
            return context.PerformOperation(_tracer, () =>
            {
                // Only a single GC can happen at any time
                using var gcLockHandle = _locks.TryAcquire(_backupPath);
                if (gcLockHandle is null)
                {
                    return BoolResult.Success;
                }

                var backups = _fileSystem.EnumerateDirectories(_backupPath, EnumerateOptions.None).ToList();
                foreach (var backup in backups)
                {
                    // Another thread (i.e. an on-going backup) has the lock, so we just move on
                    using var pathLockHandle = _locks.TryAcquire(backup);
                    if (pathLockHandle is null)
                    {
                        continue;
                    }

                    var creationTimeUtc = _fileSystem.GetDirectoryCreationTimeUtc(backup);
                    if (_clock.UtcNow - creationTimeUtc >= _retention)
                    {
                        _fileSystem.DeleteDirectory(backup, DeleteOptions.All);
                    }
                }

                return BoolResult.Success;
            });
        }
    }
}
