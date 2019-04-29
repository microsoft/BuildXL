// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Instance of a GetBulk operation for tracing purposes.
    /// </summary>
    public sealed class GetBulkCall : TracedCallWithInput<RedisContentLocationStoreTracer, IReadOnlyList<ContentHash>, GetBulkLocationsResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<GetBulkLocationsResult> RunAsync(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            Func<Task<GetBulkLocationsResult>> funcAsync)
        {
            using (var call = new GetBulkCall(tracer, context, contentHashes))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GetBulkCall"/> class.
        /// </summary>
        private GetBulkCall(
            RedisContentLocationStoreTracer tracer,
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes)
            : base(tracer, context, contentHashes)
        {
            Tracer.GetBulkStart();
        }

        /// <inheritdoc />
        protected override GetBulkLocationsResult CreateErrorResult(Exception exception)
        {
            return new GetBulkLocationsResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.GetBulkStop(Context, Input, Result);
        }
    }
}
