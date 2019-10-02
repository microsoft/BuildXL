// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of an OpenStream operation for tracing purposes.
    /// </summary>
    public sealed class OpenStreamCall<TTracer> : TracedCall<TTracer, OpenStreamResult>, IDisposable
        where TTracer : ContentSessionTracer
    {
        private readonly ContentHash _contentHash;

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<OpenStreamResult> RunAsync(
            TTracer tracer, OperationContext context, ContentHash contentHash, Func<Task<OpenStreamResult>> funcAsync)
        {
            using (var call = new OpenStreamCall<TTracer>(tracer, context, contentHash))
            {
                return await call.RunAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenStreamCall{TTracer}"/> class.
        /// </summary>
        private OpenStreamCall(TTracer tracer, OperationContext context, ContentHash contentHash)
            : base(tracer, context)
        {
            _contentHash = contentHash;

            Tracer.OpenStreamStart(Context, contentHash);
        }

        /// <inheritdoc />
        protected override OpenStreamResult CreateErrorResult(Exception exception)
        {
            return new OpenStreamResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.OpenStreamStop(Context, _contentHash, Result);
        }
    }
}
