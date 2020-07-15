// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Set of extension methods for structured-like tracing using <see cref="ContentStoreInternalTracer"/>.
    /// </summary>
    public static class ContentStoreInternalTracerExtensions
    {
        /// <nodoc />
        public static Task<PutResult> PutFileAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            ContentHash contentHash,
            bool trustedHash,
            Func<Task<PutResult>> func) where TTracer : ContentSessionTracer
        {
            return PutFileCall<TTracer>.RunAsync(tracer, context, path, mode, contentHash, trustedHash, func);
        }

        /// <nodoc />
        public static Task<PutResult> PutFileAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            AbsolutePath path,
            FileRealizationMode mode,
            HashType hashType,
            bool trustedHash,
            Func<Task<PutResult>> func) where TTracer : ContentSessionTracer
        {
            return PutFileCall<TTracer>.RunAsync(tracer, context, path, mode, hashType, trustedHash, func);
        }

        /// <nodoc />
        public static Task<GetStatsResult> GetStatsAsync(
            this Tracer tracer,
            OperationContext context,
            Func<Task<GetStatsResult>> func)
        {
            return GetStatsCall<Tracer>.RunAsync(tracer, context, func);
        }

        /// <nodoc />
        public static Task<PutResult> PutStreamAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            Func<Task<PutResult>> func) where TTracer : ContentSessionTracer
        {
            return PutStreamCall<TTracer>.RunAsync(tracer, context, contentHash, func);
        }

        /// <nodoc />
        public static Task<PutResult> PutStreamAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            HashType hashType,
            Func<Task<PutResult>> func) where TTracer : ContentSessionTracer
        {
            return PutStreamCall<TTracer>.RunAsync(tracer, context, hashType, func);
        }

        /// <nodoc />
        public static Task<EvictResult> EvictAsync(
            this ContentStoreInternalTracer tracer, OperationContext context, ContentHash contentHash, Func<Task<EvictResult>> func)
        {
            return EvictCall.RunAsync(tracer, context, contentHash, func);
        }

        /// <nodoc />
        public static Task<PlaceFileResult> PlaceFileAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            Func<Task<PlaceFileResult>> func) where TTracer : ContentSessionTracer
        {
            return PlaceFileCall<TTracer>.RunAsync(
                tracer,
                context,
                contentHash,
                path,
                accessMode,
                replacementMode,
                realizationMode,
                func);
        }

        /// <nodoc />
        public static Task<PinResult> PinAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            Func<Task<PinResult>> func) where TTracer : ContentSessionTracer
        {
            return PinCall<TTracer>.RunAsync(tracer, context, contentHash, func);
        }

        /// <nodoc />
        public static Task<OpenStreamResult> OpenStreamAsync<TTracer>(
            this TTracer tracer,
            OperationContext context,
            ContentHash contentHash,
            Func<Task<OpenStreamResult>> func) where TTracer : ContentSessionTracer
        {
            return OpenStreamCall<TTracer>.RunAsync(tracer, context, contentHash, func);
        }

        /// <nodoc />
        public readonly struct DisposeActionTracer : IDisposable
        {
            private readonly StopwatchSlim _stopWatch;
            private readonly Action<TimeSpan> _stopAction;

            /// <nodoc />
            public DisposeActionTracer(Action<TimeSpan> stopAction) => (_stopWatch, _stopAction) = (StopwatchSlim.Start(), stopAction);

            /// <inheritdoc />
            public void Dispose()
            {
                _stopAction(_stopWatch.Elapsed);
            }
        }

        /// <nodoc />
        public static DisposeActionTracer PutFileExistingHardLink(this ContentStoreInternalTracer tracer)
        {
            tracer.PutFileExistingHardLinkStart();
            return new DisposeActionTracer(tracer.PutFileExistingHardLinkStop);
        }

        /// <nodoc />
        public static DisposeActionTracer PutFileNewHardLink(this ContentStoreInternalTracer tracer)
        {
            tracer.PutFileNewHardLinkStart();
            return new DisposeActionTracer(tracer.PutFileNewHardLinkStop);
        }

        /// <nodoc />
        public static DisposeActionTracer PutFileNewCopy(this ContentStoreInternalTracer tracer)
        {
            tracer.PutFileNewCopyStart();
            return new DisposeActionTracer(tracer.PutFileNewCopyStop);
        }

        /// <nodoc />
        public static DisposeActionTracer PutContentInternal(this ContentStoreInternalTracer tracer)
        {
            tracer.PutContentInternalStart();
            return new DisposeActionTracer(tracer.PutContentInternalStop);
        }

        /// <nodoc />
        public static DisposeActionTracer ApplyPerms(this ContentStoreInternalTracer tracer)
        {
            tracer.ApplyPermsStart();
            return new DisposeActionTracer(tracer.ApplyPermsStop);
        }
    }
}
