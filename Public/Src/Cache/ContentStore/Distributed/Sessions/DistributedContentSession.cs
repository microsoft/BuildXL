// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// A content location based content session with an inner content session for storage.
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class DistributedContentSession<T> : ReadOnlyDistributedContentSession<T>, IContentSession
        where T : PathBase
    {
        private enum Counters
        {
            GetLocationsSatisfiedFromLocal,
            GetLocationsSatisfiedFromRemote
        }

        private readonly CounterCollection<Counters> _counters = new CounterCollection<Counters>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedContentSession{T}"/> class.
        /// </summary>
        public DistributedContentSession(
            string name,
            IContentSession inner,
            IContentLocationStore contentLocationStore,
            ContentAvailabilityGuarantee contentAvailabilityGuarantee,
            DistributedContentCopier<T> contentCopier,
            byte[] localMachineLocation,
            PinCache pinCache = null,
            ContentTrackerUpdater contentTrackerUpdater = null,
            DistributedContentStoreSettings settings = default)
            : base(
                name,
                inner,
                contentLocationStore,
                contentAvailabilityGuarantee,
                contentCopier,
                localMachineLocation,
                pinCache: pinCache,
                contentTrackerUpdater: contentTrackerUpdater,
                settings)
        {
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return PutCoreAsync(
                operationContext,
                (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutFileAsync(operationContext, path, hashType, realizationMode, operationContext.Token, urgencyHint, wrapStream),
                session => session.PutFileAsync(operationContext, hashType, path, realizationMode, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return PutCoreAsync(
                operationContext,
                (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutFileAsync(operationContext, path, contentHash, realizationMode, operationContext.Token, urgencyHint, wrapStream),
                session => session.PutFileAsync(operationContext, contentHash, path, realizationMode, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return PutCoreAsync(
                operationContext,
                (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutStreamAsync(operationContext, hashType, wrapStream(stream), operationContext.Token, urgencyHint),
                session => session.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return PutCoreAsync(
                operationContext,
                (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutStreamAsync(operationContext, contentHash, wrapStream(stream), operationContext.Token, urgencyHint),
                session => session.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint));
        }

        /// <summary>
        /// Executes a put operation, while providing the logic to retrieve the bytes that were put through a RecordingStream.
        /// RecordingStream makes it possible to see the actual bytes that are being read by the inner ContentSession.
        /// </summary>
        private async Task<PutResult> PutCoreAsync(
            OperationContext context,
            Func<IDecoratedStreamContentSession, Func<Stream, Stream>, Task<PutResult>> putRecordedAsync,
            Func<IContentSession, Task<PutResult>> putAsync)
        {
            PutResult result;
            if (ContentLocationStore.AreBlobsSupported && Inner is IDecoratedStreamContentSession decoratedStreamSession)
            {
                RecordingStream recorder = null;
                result = await putRecordedAsync(decoratedStreamSession, stream =>
                {
                    if (stream.CanSeek && stream.Length <= ContentLocationStore.MaxBlobSize)
                    {
                        recorder = new RecordingStream(inner: stream, size: stream.Length);
                        return recorder;
                    }

                    return stream;
                });

                if (result && recorder != null)
                {
                    // Fire and forget since this step is optional.
                    await ContentLocationStore.PutBlobAsync(context, result.ContentHash, recorder.RecordedBytes).FireAndForgetAndReturnTask(context);
                }
            }
            else
            {
                result = await putAsync(Inner);
            }

            var putResult = await RegisterPutAsync(context, UrgencyHint.Nominal, result);

            if (putResult.Succeeded && Settings.EnableProactiveCopy)
            {
                RequestProactiveCopyIfNeededAsync(context, putResult.ContentHash).FireAndForget(context);
            }

            return putResult;
        }

        private async Task<PutResult> RegisterPutAsync(OperationContext context, UrgencyHint urgencyHint, PutResult putResult)
        {
            if (putResult.Succeeded)
            {
                var updateResult = await ContentLocationStore.RegisterLocalLocationAsync(
                    context,
                    new [] { new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize) },
                    context.Token,
                    urgencyHint);

                if (!updateResult.Succeeded)
                {
                    return new PutResult(updateResult, putResult.ContentHash);
                }
            }

            return putResult;
        }

        private Task<BoolResult> RequestProactiveCopyIfNeededAsync(OperationContext context, ContentHash hash)
        {
            return context.PerformOperationAsync(
                Tracer,
                traceErrorsOnly: true,
                operation: async () =>
                {
                    var hashArray = new[] { hash };

                    // First check in local location store, then global if failed.
                    var getLocationsResult = await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Local);
                    if (getLocationsResult.Succeeded && getLocationsResult.ContentHashesInfo[0].Locations.Count > Settings.ProactiveCopyLocationsThreshold)
                    {
                        _counters[Counters.GetLocationsSatisfiedFromLocal].Increment();
                    }
                    else
                    {
                        getLocationsResult = await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Global);

                        if (getLocationsResult.Succeeded)
                        {
                            _counters[Counters.GetLocationsSatisfiedFromRemote].Increment();
                        }
                    }

                    if (!getLocationsResult.Succeeded)
                    {
                        return new BoolResult(getLocationsResult);
                    }

                    if (getLocationsResult.ContentHashesInfo[0].Locations.Count > Settings.ProactiveCopyLocationsThreshold)
                    {
                        return BoolResult.Success;
                    }

                    var getLocationResult = ContentLocationStore.GetRandomMachineLocation(except: LocalCacheRootMachineLocation);

                    if (!getLocationResult.Succeeded)
                    {
                        return new BoolResult(getLocationResult);
                    }

                    return await DistributedCopier.RequestCopyFileAsync(context, hash, getLocationResult.Value);
                });
        }

        /// <inheritdoc />
        protected override CounterSet GetCounters() =>
            base.GetCounters()
                .Merge(_counters.ToCounterSet());
    }
}
