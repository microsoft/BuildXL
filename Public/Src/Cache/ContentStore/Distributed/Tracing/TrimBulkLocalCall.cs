// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of a TrimBulkLocal operation for tracing purposes.
    /// </summary>
    public sealed class TrimBulkLocalCall : TracedCall<RedisContentLocationStoreTracer, BoolResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            MachineId? machineId,
            Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new TrimBulkLocalCall(tracer, context, contentHashes, machineId))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrimBulkLocalCall"/> class.
        /// </summary>
        private TrimBulkLocalCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            MachineId? machineId)
            : base(tracer, context)
        {
            Tracer.TrimBulkLocalStart(context, contentHashes, machineId);
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.TrimBulkLocalStop(Context, Result);
        }
    }
}
