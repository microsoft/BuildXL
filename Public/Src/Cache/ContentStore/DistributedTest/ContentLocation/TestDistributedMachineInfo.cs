// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Stores;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Distributed
{
    public class TestDistributedMachineInfo : StartupShutdownSlimBase, IDistributedMachineInfo, ILocalContentStore
    {
        public MemoryContentDirectory Directory { get; }
        public ILocalContentStore LocalContentStore => this;
        public MachineId LocalMachineId { get; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(TestDistributedMachineInfo));

        private readonly AbsolutePath _localContentDirectoryPath;
        private readonly IAbsFileSystem _fileSystem;

        public TestDistributedMachineInfo(int machineId, string localContentDirectoryPath, IAbsFileSystem fileSystem, AbsolutePath machineInfoRoot)
        {
            LocalMachineId = new MachineId(machineId);
            Directory = new MemoryContentDirectory(fileSystem, machineInfoRoot);
            _fileSystem = fileSystem;
            _localContentDirectoryPath = new AbsolutePath(localContentDirectoryPath);
        }

        public override async Task<BoolResult> StartupAsync(Context context)
        {
            if (_fileSystem.FileExists(_localContentDirectoryPath))
            {
                await _fileSystem.CopyFileAsync(_localContentDirectoryPath, Directory.FilePath, replaceExisting: true);
            }

            await Directory.StartupAsync(context).ThrowIfFailure();

            return BoolResult.Success;
        }

        public bool Contains(ContentHash hash)
        {
            return Directory.TryGetFileInfo(hash, out _);
        }

        public Task<IReadOnlyList<ContentInfo>> GetContentInfoAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public bool TryGetContentInfo(ContentHash hash, out ContentInfo info)
        {
            if (Directory.TryGetFileInfo(hash, out var fileInfo))
            {
                info = new ContentInfo(hash, fileInfo.FileSize, fileInfo.LastAccessedTimeUtc);
                return true;
            }
            else
            {
                info = default;
                return false;
            }
        }

        public void UpdateLastAccessTimeIfNewer(ContentHash hash, DateTime newLastAccessTime)
        {
            if (Directory.TryGetFileInfo(hash, out var fileInfo))
            {
                fileInfo.UpdateLastAccessed(newLastAccessTime);
            }
        }
    }
}
