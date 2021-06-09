// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.ContentStore.Hashing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.ContractsLight;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MockCentralStorage : CentralStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(MockCentralStorage));

        private readonly Dictionary<string, byte[]> _storage = new Dictionary<string, byte[]>();

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
    }
}
