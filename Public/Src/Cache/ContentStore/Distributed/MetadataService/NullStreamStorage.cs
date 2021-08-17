// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Null implementation of <see cref="IStreamStorage"/>
    /// </summary>
    public class NullStreamStorage : StartupShutdownSlimBase, IStreamStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(NullStreamStorage));

        public Task<TResult> ReadAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync) where TResult : ResultBase
        {
            return Task.FromResult(new ErrorResult("Null stream storage does not provide streams.").AsResult<TResult>());
        }

        public Task<BoolResult> StoreAsync(OperationContext context, string storageId, Stream stream)
        {
            return BoolResult.SuccessTask;
        }
    }
}
