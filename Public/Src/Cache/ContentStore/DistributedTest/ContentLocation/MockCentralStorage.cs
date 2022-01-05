// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using System.IO;
using System;
using BuildXL.Cache.ContentStore.Hashing;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MockCentralStorage : CentralStreamStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(MockCentralStorage));

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly Dictionary<string, byte[]> _storage;

        public MockCentralStorage(Dictionary<string, byte[]> storage = null)
        {
            _storage = storage ?? new Dictionary<string, byte[]>();
        }

        protected override Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable)
        {
            return BoolResult.SuccessTask;
        }

        protected override Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            byte[] bytes;
            lock (_storage)
            {
                bytes = _storage[storageId];
            }
            File.WriteAllBytes(targetFilePath.Path, bytes);

            return BoolResult.SuccessTask;
        }

        protected override Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false)
        {
            var bytes = File.ReadAllBytes(file.Path);
            lock (_storage)
            {
                _storage[name] = bytes;
            }
            
            return Task.FromResult(Result.Success(name));
        }

        protected override Task<TResult> ReadCoreAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync)
        {
            var stream = GetStream(storageId);

            return readStreamAsync(stream.WithLength());
        }

        public MemoryStream GetStream(string storageId)
        {
            MemoryStream stream;
            lock (_storage)
            {
                stream = new MemoryStream(_storage[storageId], writable: false);
            }

            return stream;
        }

        protected override async Task<BoolResult> StoreCoreAsync(OperationContext context, string storageId, Stream stream)
        {
            using var memoryStream = new MemoryStream();

            await stream.CopyToAsync(memoryStream);
            lock (_storage)
            {
                _storage[storageId] = memoryStream.ToArray();
            }

            return BoolResult.Success;
        }
    }
}
