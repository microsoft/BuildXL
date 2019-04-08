// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of an Calibrate operation for tracing purposes.
    /// </summary>
    public sealed class CalibrateCall : TracedCall<ContentStoreInternalTracer, CalibrateResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<CalibrateResult> RunAsync(
            ContentStoreInternalTracer tracer, OperationContext context, Func<Task<CalibrateResult>> funcAsync)
        {
            using (var call = new CalibrateCall(tracer, context))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CalibrateCall" /> class.
        /// </summary>
        private CalibrateCall(ContentStoreInternalTracer tracer, OperationContext context)
            : base(tracer, context)
        {
            Tracer.CalibrateStart(context);
        }

        /// <inheritdoc />
        protected override CalibrateResult CreateErrorResult(Exception exception)
        {
            return new CalibrateResult(exception.ToString());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.CalibrateStop(Context, Result);
        }
    }
}
