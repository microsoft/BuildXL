// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Base implementation of a central storage for large stream content.
    /// </summary>
    public abstract class CentralStreamStorage : CentralStorage, IStreamStorage
    {
        /// <inheritdoc />
        public override bool AllowMultipleStartupAndShutdowns => true;

        /// <summary>
        /// <see cref="ReadAsync{TResult}"/>
        /// </summary>
        protected abstract Task<TResult> ReadCoreAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync)
            where TResult : ResultBase;

        /// <summary>
        /// <see cref="StoreAsync"/>
        /// </summary>
        protected abstract Task<BoolResult> StoreCoreAsync(OperationContext context, string storageId, Stream stream);

        /// <inheritdoc />
        public Task<BoolResult> StoreAsync(OperationContext context, string storageId, Stream stream)
        {
            return context.PerformOperationAsync(
                Tracer,
                () => StoreCoreAsync(context, storageId, stream),
                extraEndMessage: _ => $"StorageId=[{storageId}]");
        }

        /// <inheritdoc />
        public Task<TResult> ReadAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync)
            where TResult : ResultBase
        {
            return context.PerformOperationAsync(
                Tracer,
                () => ReadCoreAsync(context, storageId, readStreamAsync),
                extraEndMessage: _ => $"StorageId=[{storageId}]");
        }
    }
}
