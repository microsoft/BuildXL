// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a RemoveFromTracker operation for tracing purposes.
    /// </summary>
    public sealed class RemoveFromTrackerCall<TTracer> : TracedCall<TTracer, BoolResult>, IDisposable
        where TTracer : ContentStoreTracer
    {
        /// <summary>n
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(TTracer tracer, OperationContext context, Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new RemoveFromTrackerCall<TTracer>(tracer, context))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoveFromTrackerCall{TTracer}"/> class.
        /// </summary>
        private RemoveFromTrackerCall(TTracer tracer, OperationContext context)
            : base(tracer, context)
        {
            Tracer.RemoveFromTrackerStart();
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.RemoveFromTrackerStop(Context, Result);
        }
    }
}
