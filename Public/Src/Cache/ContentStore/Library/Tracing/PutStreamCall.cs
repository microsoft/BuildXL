// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a PutStream operation for tracing purposes.
    /// </summary>
    public sealed class PutStreamCall<TTracer> : TracedCall<TTracer, PutResult>, IDisposable
        where TTracer : ContentSessionTracer
    {
        private readonly ContentHash _contentHash;

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PutResult> RunAsync(
            TTracer tracer, OperationContext context, HashType hashType, Func<Task<PutResult>> funcAsync)
        {
            using (var call = new PutStreamCall<TTracer>(tracer, context, hashType))
            {
                return await call.RunSafeAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PutResult> RunAsync(
            TTracer tracer, OperationContext context, ContentHash contentHash, Func<Task<PutResult>> funcAsync)
        {
            using (var call = new PutStreamCall<TTracer>(tracer, context, contentHash))
            {
                return await call.RunSafeAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutStreamCall{TTracer}"/> class.
        /// </summary>
        private PutStreamCall(TTracer tracer, OperationContext context, HashType hashType)
            : this(tracer, context, new ContentHash(hashType))
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutStreamCall{TTracer}"/> class.
        /// </summary>
        private PutStreamCall(TTracer tracer, OperationContext context, ContentHash contentHash)
            : base(tracer, context)
        {
            Contract.Requires(contentHash.HashType != HashType.Unknown);

            _contentHash = contentHash;
            Tracer.PutStreamStart(context, contentHash);
        }

        /// <inheritdoc />
        protected override PutResult CreateErrorResult(Exception exception)
        {
            return new PutResult(exception, _contentHash);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.PutStreamStop(Context, Result);
        }
    }
}
