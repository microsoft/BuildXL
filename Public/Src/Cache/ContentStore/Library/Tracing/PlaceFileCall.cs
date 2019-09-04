// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a PlaceFile operation for tracing purposes.
    /// </summary>
    public sealed class PlaceFileCall<TTracer> : TracedCallWithInput<TTracer, ContentHash, PlaceFileResult>, IDisposable
        where TTracer : ContentSessionTracer
    {
        private readonly AbsolutePath _path;
        private readonly FileAccessMode _accessMode;
        private readonly FileReplacementMode _replacementMode;
        private readonly FileRealizationMode _realizationMode;

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<PlaceFileResult> RunAsync(
            TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            Func<Task<PlaceFileResult>> funcAsync)
        {
            using (var call = new PlaceFileCall<TTracer>(tracer, context, contentHash, path, accessMode, replacementMode, realizationMode))
            {
                return await call.RunSafeAsync(funcAsync);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlaceFileCall{TTracer}"/> class.
        /// </summary>
        private PlaceFileCall(
            TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode)
            : base(tracer, context, contentHash)
        {
            _path = path;
            _accessMode = accessMode;
            _replacementMode = replacementMode;
            _realizationMode = realizationMode;

            Tracer.PlaceFileStart(Context, contentHash, path, accessMode, replacementMode, realizationMode);
        }

        /// <inheritdoc />
        protected override PlaceFileResult CreateErrorResult(Exception exception)
        {
            return new PlaceFileResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.PlaceFileStop(Context, Input, Result, _path, _accessMode, _replacementMode, _realizationMode);
        }
    }
}
