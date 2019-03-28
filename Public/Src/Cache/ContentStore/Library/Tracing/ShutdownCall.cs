// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a Shutdown operation for tracing purposes.
    /// </summary>
    public sealed class ShutdownCall<TTracer> : TracedCall<TTracer, BoolResult>, IDisposable
        where TTracer : Tracer
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(TTracer tracer, Context context, Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new ShutdownCall<TTracer>(tracer, context))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ShutdownCall{TTracer}"/> class.
        /// </summary>
        private ShutdownCall(TTracer tracer, Context context)
            : base(tracer, context)
        {
            Tracer.ShutdownStart(Context);
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.ShutdownStop(Context, Result);
        }
    }
}
