// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a Purge operation for tracing purposes.
    /// </summary>
    public sealed class PurgeCall : TracedCall<ContentStoreInternalTracer, PurgeResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PurgeResult> RunAsync(
            ContentStoreInternalTracer tracer, OperationContext context, Func<Task<PurgeResult>> funcAsync)
        {
            using (var call = new PurgeCall(tracer, context))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PurgeCall"/> class.
        /// </summary>
        private PurgeCall(ContentStoreInternalTracer tracer, OperationContext context)
            : base(tracer, context)
        {
            Tracer.PurgeStart(Context);
        }

        /// <inheritdoc />
        protected override PurgeResult CreateErrorResult(Exception exception)
        {
            return new PurgeResult(exception.ToString());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.PurgeStop(Context, Result);
        }
    }
}
