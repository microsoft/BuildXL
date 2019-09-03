// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a PutFile operation for tracing purposes.
    /// </summary>
    public sealed class PutFileCall<TTracer> : TracedCall<TTracer, PutResult>, IDisposable
        where TTracer : ContentSessionTracer
    {
        private readonly ContentHash _contentHash;

        private readonly bool _trustedHash;

        private readonly AbsolutePath _path;

        private readonly FileRealizationMode _mode;

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PutResult> RunAsync(
            TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            HashType hashType,
            bool trustedHash,
            Func<Task<PutResult>> funcAsync)
        {
            using (var call = new PutFileCall<TTracer>(tracer, context, path, mode, hashType, trustedHash: trustedHash))
            {
                return await call.RunSafeAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PutResult> RunAsync(
            TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            ContentHash contentHash,
            bool trustedHash,
            Func<Task<PutResult>> funcAsync)
        {
            using (var call = new PutFileCall<TTracer>(tracer, context, path, mode, contentHash, trustedHash: trustedHash))
            {
                return await call.RunSafeAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutFileCall{TTracer}"/> class.
        /// </summary>
        private PutFileCall(
            TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            HashType hashType,
            bool trustedHash)
            : base(tracer, context)
        {
            Contract.Requires(hashType != HashType.Unknown);

            _contentHash = new ContentHash(hashType);
            _trustedHash = trustedHash;
            _path = path;
            _mode = mode;
            Tracer.PutFileStart(Context, path, mode, hashType, trustedHash);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PutFileCall{TTracer}"/> class.
        /// </summary>
        private PutFileCall(
            TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            ContentHash contentHash,
            bool trustedHash)
            : base(tracer, context)
        {
            Contract.Requires(contentHash.HashType != HashType.Unknown);

            _contentHash = contentHash;
            _trustedHash = trustedHash;
            _path = path;
            _mode = mode;
            Tracer.PutFileStart(Context, path, mode, contentHash, _trustedHash);
        }

        /// <inheritdoc />
        protected override PutResult CreateErrorResult(Exception exception)
        {
            return new PutResult(exception, _contentHash);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.PutFileStop(Context, Result, _trustedHash, _path, _mode);
        }
    }
}
