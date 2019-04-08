// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a Pin operation for tracing purposes.
    /// </summary>
    public sealed class PinCall<TTracer> : TracedCallWithInput<TTracer, ContentHash, PinResult>, IDisposable
        where TTracer : ContentSessionTracer
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PinResult> RunAsync(
            TTracer tracer, OperationContext context, ContentHash contentHash, Func<Task<PinResult>> funcAsync)
        {
            using (var call = new PinCall<TTracer>(tracer, context, contentHash))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinCall{TTracer}"/> class.
        /// </summary>
        private PinCall(TTracer tracer, OperationContext context, ContentHash contentHash)
            : base(tracer, context, contentHash)
        {
            Tracer.PinStart(Context);
        }

        /// <inheritdoc />
        protected override PinResult CreateErrorResult(Exception exception)
        {
            return new PinResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.PinStop(Context, Input, Result);
        }
    }
}
