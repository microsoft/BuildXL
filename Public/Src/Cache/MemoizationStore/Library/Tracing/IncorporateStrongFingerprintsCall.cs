// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     Instance of an IncorporateStrongFingerprints operation for tracing purposes.
    /// </summary>
    public sealed class IncorporateStrongFingerprintsCall : TracedCall<MemoizationStoreTracer, BoolResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<BoolResult> RunAsync(MemoizationStoreTracer tracer, Context context, Func<Task<BoolResult>> funcAsync)
        {
            using (var call = new IncorporateStrongFingerprintsCall(tracer, context))
            {
                return await call.RunAsync(funcAsync).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IncorporateStrongFingerprintsCall"/> class.
        /// </summary>
        private IncorporateStrongFingerprintsCall(MemoizationStoreTracer tracer, Context context)
            : base(tracer, context)
        {
            Tracer.IncorporateStrongFingerprintsStart(Context);
        }

        /// <inheritdoc />
        protected override BoolResult CreateErrorResult(Exception exception)
        {
            return new BoolResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.IncorporateStrongFingerprintsStop(Context, Result);
        }
    }
}
