// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     Instance of a GetContentHashList operation for tracing purposes.
    /// </summary>
    public sealed class GetContentHashListCall : TracedCall<MemoizationStoreTracer, GetContentHashListResult>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<GetContentHashListResult> RunAsync(
            MemoizationStoreTracer tracer, Context context, StrongFingerprint fingerprint, Func<Task<GetContentHashListResult>> asyncFunc)
        {
            using (var call = new GetContentHashListCall(tracer, context, fingerprint))
            {
                return await call.RunAsync(asyncFunc).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentHashListCall"/> class.
        /// </summary>
        private GetContentHashListCall(MemoizationStoreTracer tracer, Context context, StrongFingerprint fingerprint)
            : base(tracer, context)
        {
            Tracer.GetContentHashListStart(context, fingerprint);
        }

        /// <inheritdoc />
        protected override GetContentHashListResult CreateErrorResult(Exception exception)
        {
            return new GetContentHashListResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.GetContentHashListStop(Context, Result);
        }
    }
}
