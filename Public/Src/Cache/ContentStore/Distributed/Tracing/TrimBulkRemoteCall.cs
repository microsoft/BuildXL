// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of a TrimBulk operation for tracing purposes.
    /// </summary>
    public sealed class TrimBulkRemoteCall : TracedCallWithInput<RedisContentLocationStoreTracer, IReadOnlyList<ContentHashAndLocations>, BoolResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashAndLocations> contentHashesWithLocations,
            Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new TrimBulkRemoteCall(tracer, context, contentHashesWithLocations))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrimBulkRemoteCall"/> class.
        /// </summary>
        private TrimBulkRemoteCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHashAndLocations> contentHashesWithLocations)
            : base(tracer, context, contentHashesWithLocations)
        {
            Tracer.TrimBulkRemoteStart();
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.TrimBulkRemoteStop(Context, Input, Result);
        }
    }
}
