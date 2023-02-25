// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;
using ContentStore.Grpc;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{

    /// <summary>
    /// <see cref="CentralStorage"/> which uses uses distributed CAS as cache aside for a fallback central storage
    /// </summary>
    public class DistributedCentralStorage : CachingCentralStorage, IDistributedContentCopierHost
    {
        private readonly ILocationStore _locationStore;
        private readonly DistributedContentCopier _copier;

        private readonly DisposableDirectory _copierWorkingDirectory;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedCentralStorage));

        /// <inheritdoc />
        protected override string PreprocessStorageId(string storageId) => storageId;

        /// <nodoc />
        private readonly VolatileMap<ShortHash, CopyOperation> _checkpointCopies;

        /// <nodoc />
        public DistributedCentralStorage(
            DistributedCentralStoreConfiguration configuration,
            ILocationStore locationStore,
            DistributedContentCopier copier,
            CentralStorage fallbackStorage,
            IClock clock)
            : base(configuration, fallbackStorage, copier.FileSystem)
        {
            _copier = copier;
            _locationStore = locationStore;
            _checkpointCopies = new VolatileMap<ShortHash, CopyOperation>(clock);

            _copierWorkingDirectory = new DisposableDirectory(copier.FileSystem, PrivateCas!.RootPath / "Temp");
        }

        #region IDistributedContentCopierHost Members

        AbsolutePath IDistributedContentCopierHost.WorkingFolder => _copierWorkingDirectory.Path;

        void IDistributedContentCopierHost.ReportReputation(Context context, MachineLocation location, MachineReputation reputation)
        {
            // Don't report reputation as this component modifies machine locations so they won't be recognized
            // by the machine reputation tracker
        }

        #endregion IDistributedContentCopierHost Members

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            _copierWorkingDirectory.Dispose();

            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override async Task<Result<ContentHashWithSize>> TryGetAndPutFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            var result = await base.TryGetAndPutFileAsync(context, storageId, targetFilePath, isImmutable);

            if (result.TryGetValue(out var contentHashWithSize))
            {
                // Register that the machine now has the content
                await RegisterContent(context, contentHashWithSize).ThrowIfFailure();
            }

            return result;
        }

        /// <summary>
        /// TODO: try to refactor this to use the same logic as ReadOnlyDistributedContentSession.
        /// </summary>
        private Task<PushFileResult> PushCheckpointFileAsync(OperationContext context, ContentHashWithSize hashWithSize)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var destinationMachineResult = _locationStore.GetRandomMachineLocation();
                if (!destinationMachineResult.Succeeded)
                {
                    return new PushFileResult(destinationMachineResult, "Failed to get a location to proactively copy the checkpoint file.");
                }

                var destionationMachine = destinationMachineResult.Value;

                var streamResult = await PrivateCas.OpenStreamAsync(context, hashWithSize.Hash, pinRequest: null);
                if (!streamResult.Succeeded)
                {
                    return new PushFileResult(streamResult, "Should have been able to open the stream from the local CAS");
                }

                using var stream = streamResult.Stream;
                return await _copier.PushFileAsync(
                    context,
                    hashWithSize,
                    destionationMachine,
                    stream,
                    isInsideRing: false,
                    CopyReason.ProactiveCheckpointCopy,
                    ProactiveCopyLocationSource.Random,
                    attempt: 0);
            },
            extraStartMessage: $"Hash=[{hashWithSize.Hash.ToShortString()}]",
            extraEndMessage: _ => $"Hash=[{hashWithSize.Hash.ToShortString()}]");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryRetrieveFromExternalCacheAsync(OperationContext context, ContentHash hash)
        {
            var result = await CopyLocalAndPutAsync(context, hash);
            if (result.Succeeded)
            {
                Counters[CentralStorageCounters.TryGetFileFromPeerSucceeded].Increment();
            }

            return result;
        }

        private Task<PutResult> CopyLocalAndPutAsync(OperationContext operationContext, ContentHash hash)
        {
            return operationContext.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var result = await _locationStore.GetBulkAsync(context, hash).ThrowIfFailure();
                    var hashInfo = result.ContentHashesInfo[0];
                    if (hashInfo.Locations?.Count > 0)
                    {
                        return await _copier.TryCopyAndPutAsync(
                            context,
                            new DistributedContentCopier.CopyRequest(
                                this,
                                hashInfo,
                                CopyReason.CentralStorage,
                                args => PrivateCas.PutFileAsync(context, args.tempLocation, FileRealizationMode.Move, hash, pinRequest: null),
                                // Most of these transfers are large files (sst files), but they are also already
                                // compressed, so compressing over it would only waste cycles.
                                CopyCompression.None
                            ));
                    }

                    return new PutResult(hash, "Insufficient replicas");
                },
                traceErrorsOnly: true,
                extraEndMessage: _ => $"ContentHash=[{hash}]",
                timeout: Configuration.PeerToPeerCopyTimeout);
        }


        protected override async Task<PutResult> PutFileAsync(OperationContext context, AbsolutePath file, ContentHash? hash, bool isImmutable = false, bool isUpload = false)
        {
            var putResult = await base.PutFileAsync(context, file, hash, isImmutable);
            if (putResult.Succeeded)
            {
                var contentInfo = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
                await RegisterContent(context, contentInfo).ThrowIfFailure();

                if (isUpload && Configuration.ProactiveCopyCheckpointFiles)
                {
                    var hashWithSize = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
                    var pushResult = await PushCheckpointFileAsync(context, hashWithSize)
                        .FireAndForgetOrInlineAsync(context, Configuration.InlineCheckpointProactiveCopies)
                        .ThrowIfFailureAsync();
                }
            }

            return putResult;
        }

        private ValueTask<BoolResult> RegisterContent(OperationContext context, params ContentHashWithSize[] contentInfo)
        {
           return _locationStore.RegisterLocalLocationAsync(context, contentInfo);
        }

        public override Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            var operationContext = OperationContext(context);
            return operationContext.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return base.StreamContentAsync(context, contentHash);
                },
                traceOperationStarted: false,
                extraEndMessage: r => $"HasContent=[{HasContent(contentHash)}]");
        }

        private class CopyOperation
        {
            public TaskSourceSlim<bool> CopyCompletion { get; } = new TaskSourceSlim<bool>();
        }

        /// <summary>
        /// Defines content location store functionality needed for <see cref="DistributedCentralStorage"/>
        /// </summary>
        public interface ILocationStore
        {
            /// <summary>
            /// Gets a random machine location
            /// </summary>
            Result<MachineLocation> GetRandomMachineLocation();

            /// <summary>
            /// Gets content locations for content
            /// </summary>
            Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, ContentHash hash);

            /// <summary>
            /// Registers content location for current machine
            /// </summary>
            ValueTask<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo);
        }
    }
}
