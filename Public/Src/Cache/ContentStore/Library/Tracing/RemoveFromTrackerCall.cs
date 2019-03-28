// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a RemoveFromTravcker operation for tracing purposes.
    /// </summary>
    public sealed class RemoveFromTrackerCall<TTracer> : TracedCall<TTracer, StructResult<long>>, IDisposable
        where TTracer : ContentStoreTracer
    {
        /// <summary>n
        ///     Run the call.
        /// </summary>
        public static async Task<StructResult<long>> RunAsync(TTracer tracer, OperationContext context, Func<Task<StructResult<long>>> funcAsync)
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
        protected override StructResult<long> CreateErrorResult(Exception exception)
        {
            return new StructResult<long>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.RemoveFromTrackerStop(Context, Result);
        }
    }
}
