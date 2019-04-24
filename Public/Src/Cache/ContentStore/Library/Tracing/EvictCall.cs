// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of an Evict operation for tracing purposes.
    /// </summary>
    public sealed class EvictCall : TracedCallWithInput<ContentStoreInternalTracer, ContentHash, EvictResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<EvictResult> RunAsync(
            ContentStoreInternalTracer tracer, OperationContext context, ContentHash contentHash, Func<Task<EvictResult>> funcAsync)
        {
            using (var call = new EvictCall(tracer, context, contentHash))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="EvictCall"/> class.
        /// </summary>
        private EvictCall(ContentStoreInternalTracer tracer, OperationContext context, ContentHash contentHash)
            : base(tracer, context, contentHash)
        {
            Tracer.EvictStart(contentHash);
        }

        /// <inheritdoc />
        protected override EvictResult CreateErrorResult(Exception exception)
        {
            return new EvictResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.EvictStop(Context, Input, Result);
        }
    }
}
