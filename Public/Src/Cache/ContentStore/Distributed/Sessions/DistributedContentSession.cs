// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
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

        private readonly SemaphoreSlim _putFileGate;

        private string _buildId = null;
        private ContentHash? _buildIdHash = null;
        private ConcurrentBigSet<ContentHash> _pendingProactivePuts = new ConcurrentBigSet<ContentHash>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedContentSession{T}"/> class.
        /// </summary>
        public DistributedContentSession(
            string name,
            IContentSession inner,
            IContentLocationStore contentLocationStore,
            ContentAvailabilityGuarantee contentAvailabilityGuarantee,
            DistributedContentCopier<T> contentCopier,
            MachineLocation localMachineLocation,
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
            _putFileGate = new SemaphoreSlim(settings.MaximumConcurrentPutFileOperations);

        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailure();
            if (Constants.TryExtractBuildId(Name, out _buildId) && Guid.TryParse(_buildId, out var buildIdGuid))
            {
                // Generate a fake hash for the build and register a content entry in the location store to represent
                // machines in the build ring
                _buildIdHash = new ContentHash(HashType.MD5, buildIdGuid.ToByteArray());

                Tracer.Info(context, $"Registering machine with build {_buildId} (build id hash: {_buildIdHash.Value.ToShortString()}");
                await ContentLocationStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(_buildIdHash.Value, _buildId.Length) }, context.Token, UrgencyHint.Nominal).ThrowIfFailure();
            }

            return BoolResult.Success;
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
            return PerformPutFileGatedOperationAsync(operationContext, () =>
            {
                return PutCoreAsync(
                    operationContext,
                    (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutFileAsync(operationContext, path, hashType, realizationMode, operationContext.Token, urgencyHint, wrapStream),
                    session => session.PutFileAsync(operationContext, hashType, path, realizationMode, operationContext.Token, urgencyHint));
            });
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
            // We are intentionally not gating PutStream operations because we don't expect a high number of them at
            // the same time.
            return PerformPutFileGatedOperationAsync(operationContext, () =>
            {
                return PutCoreAsync(
                    operationContext,
                    (decoratedStreamSession, wrapStream) => decoratedStreamSession.PutFileAsync(operationContext, path, contentHash, realizationMode, operationContext.Token, urgencyHint, wrapStream),
                    session => session.PutFileAsync(operationContext, contentHash, path, realizationMode, operationContext.Token, urgencyHint));
            });
        }

        private Task<PutResult> PerformPutFileGatedOperationAsync(OperationContext operationContext, Func<Task<PutResult>> func)
        {
            return _putFileGate.GatedOperationAsync(async (timeWaiting) =>
            {
                var gateOccupiedCount = Settings.MaximumConcurrentPutFileOperations - _putFileGate.CurrentCount;

                var result = await func();
                result.Metadata = new PutResult.ExtraMetadata()
                {
                    GateWaitTime = timeWaiting,
                    GateOccupiedCount = gateOccupiedCount,
                };

                return result;
            }, operationContext.Token);
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
            bool putBlob = false;
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
                    var putBlobResult = await ContentLocationStore.PutBlobAsync(context, result.ContentHash, recorder.RecordedBytes).FireAndForgetAndReturnTask(context);
                    putBlob = putBlobResult.Succeeded;
                }
            }
            else
            {
                result = await putAsync(Inner);
            }

            if (!result)
            {
                return result;
            }

            // Only perform proactive copy to other machines if we didn't put the blob into Redis
            if (!putBlob && Settings.EnableProactiveCopy)
            {
                // Since the rest of the operation is done asynchronously, create new context to stop cancelling operation prematurely.
                WithOperationContext(
                    context,
                    CancellationToken.None,
                    operationContext => RequestProactiveCopyIfNeededAsync(operationContext, result.ContentHash)
                ).FireAndForget(context);
            }

            return await RegisterPutAsync(context, UrgencyHint.Nominal, result);
        }

        private async Task<PutResult> RegisterPutAsync(OperationContext context, UrgencyHint urgencyHint, PutResult putResult)
        {
            if (putResult.Succeeded)
            {
                var updateResult = await ContentLocationStore.RegisterLocalLocationAsync(
                    context,
                    new[] { new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize) },
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
            if (!_pendingProactivePuts.Add(hash))
            {
                return BoolResult.SuccessTask;
            }

            return context.PerformOperationAsync(
                Tracer,
                traceErrorsOnly: true,
                operation: async () =>
                {
                    try
                    {
                        var hashArray = _buildIdHash != null
                            ? new[] { hash, _buildIdHash.Value }
                            : new[] { hash };

                        // First check in local location store, then global if failed.
                        var getLocationsResult = await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Local);
                        if (getLocationsResult.Succeeded && getLocationsResult.ContentHashesInfo[0].Locations.Count > Settings.ProactiveCopyLocationsThreshold)
                        {
                            _counters[Counters.GetLocationsSatisfiedFromLocal].Increment();
                            return BoolResult.Success;
                        }
                        else
                        {
                            getLocationsResult += await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ThrowIfFailure();
                            _counters[Counters.GetLocationsSatisfiedFromRemote].Increment();
                        }

                        if (getLocationsResult.ContentHashesInfo[0].Locations.Count > Settings.ProactiveCopyLocationsThreshold)
                        {
                            return BoolResult.Success;
                        }

                        IReadOnlyList<MachineLocation> buildRingMachines;

                        Task<BoolResult> copyToBuildRingMachineTask = BoolResult.SuccessTask;

                        // Get random machine inside build ring
                        if (_buildIdHash != null)
                        {
                            buildRingMachines = getLocationsResult.ContentHashesInfo[getLocationsResult.ContentHashesInfo.Count - 1].Locations;
                            var candidates = buildRingMachines.Where(m => !m.Equals(LocalCacheRootMachineLocation)).ToArray();
                            if (candidates.Length > 0)
                            {
                                var candidate = candidates[ThreadSafeRandom.Generator.Next(0, candidates.Length)];
                                Tracer.Info(context, $"{nameof(RequestProactiveCopyIfNeededAsync)}: Copying {hash.ToShortString()} to machine '{candidate}' in build ring (of {candidates.Length} machines).");
                                copyToBuildRingMachineTask = DistributedCopier.RequestCopyFileAsync(context, hash, candidate);
                            }
                        }
                        else
                        {
                            buildRingMachines = new[] { LocalCacheRootMachineLocation };
                        }

                        BoolResult result = BoolResult.Success;
                        var getLocationResult = ContentLocationStore.GetRandomMachineLocation(except: buildRingMachines);
                        if (getLocationResult.Succeeded)
                        {
                            var candidate = getLocationResult.Value;
                            Tracer.Info(context, $"{nameof(RequestProactiveCopyIfNeededAsync)}: Copying {hash.ToShortString()} to machine '{candidate}' outside build ring.");
                            result &= await DistributedCopier.RequestCopyFileAsync(context, hash, candidate);
                        }

                        return result & await copyToBuildRingMachineTask;
                    }
                    finally
                    {
                        _pendingProactivePuts.Remove(hash);
                    }
                });
        }

        /// <inheritdoc />
        protected override CounterSet GetCounters() =>
            base.GetCounters()
                .Merge(_counters.ToCounterSet());
    }
}
