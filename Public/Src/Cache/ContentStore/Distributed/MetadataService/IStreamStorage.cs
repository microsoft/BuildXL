// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public interface IStreamStorage : IStartupShutdownSlim
    {
        /// <summary>
        /// Stores the contents of the stream
        /// </summary>
        Task<BoolResult> StoreAsync(OperationContext context, string storageId, Stream stream);

        /// <summary>
        /// Reads the content of the stream and returns the result
        /// </summary>
        Task<TResult> ReadAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync)
            where TResult : ResultBase;
    }
}
