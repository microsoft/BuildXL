// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using ContentStore.Grpc;
#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    using static CheckpointManifest;

    /// <summary>
    /// <see cref="CentralStorage"/> which uses uses distributed CAS as cache aside for a fallback central storage
    /// </summary>
    public class DistributedCentralStorage : CachingCentralStorage, IDistributedContentCopierHost
    {
        private readonly ILocationStore _locationStore;
        private readonly ICheckpointStore? _checkpointStore;
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
            _checkpointStore = configuration.IsCheckpointAware ? _locationStore as ICheckpointStore : null;

            _copierWorkingDirectory = new DisposableDirectory(copier.FileSystem, PrivateCas!.RootPath / "Temp");
        }

        #region IDistributedContentCopierHost Members

        AbsolutePath IDistributedContentCopierHost.WorkingFolder => _copierWorkingDirectory.Path;

        void IDistributedContentCopierHost.ReportReputation(MachineLocation location, MachineReputation reputation)
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

                using var stream = streamResult.Stream!;
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

        protected override async Task<bool> TryRetrieveFromExternalCacheAsync(OperationContext context, ContentHash hash)
        {
            var putResult = await CopyLocalAndPutAsync(context, hash);
            if (!putResult.Succeeded)
            {
                Tracer.Debug(context, $"Falling back to blob storage. Error={putResult}");
            }
            else
            {
                Counters[CentralStorageCounters.TryGetFileFromPeerSucceeded].Increment();
            }

            return putResult.Succeeded;
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
            foreach (var item in contentInfo)
            {
                TryCompleteCheckpointCopy(item.Hash);
            }

            return _locationStore.RegisterLocalLocationAsync(context, contentInfo);
        }

        public override bool HasContent(ContentHash contentHash)
        {
            if (base.HasContent(contentHash))
            {
                return true;
            }

            if (_checkpointStore?.IsActiveCheckpointFile(contentHash) == true)
            {
                // Claim to have active checkpoint files so that stream content will be called and
                // we can wait on copy to complete
                return true;
            }

            return false;
        }

        public override Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            var operationContext = OperationContext(context);
            bool hasContent = base.HasContent(contentHash);
            bool? isCheckpointFile = _checkpointStore?.IsActiveCheckpointFile(contentHash);
            return operationContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    hasContent = base.HasContent(contentHash);

                    if (!hasContent)
                    {
                        if (tryGetCheckpointCopyOperation(out var operation))
                        {
                            await operationContext.PerformOperationAsync(
                                Tracer,
                                async () =>
                                {
                                    // Wait for copy to complete or for configured delay
                                    await Task.WhenAny(operation.CopyCompletion.Task, Task.Delay(Configuration.PropagationDelay)).Unwrap();

                                    return BoolResult.Success;
                                },
                                traceOperationStarted: false,
                                caller: "WaitForCheckpointFile",
                                extraEndMessage: r => $"Hash={contentHash.ToShortString()}, Status={operation.CopyCompletion.Task.Status}").ThrowIfFailure();
                        }
                    }

                    bool tryGetCheckpointCopyOperation([NotNullWhen(true)] out CopyOperation? operation)
                    {
                        if (isCheckpointFile != true)
                        {
                            operation = default;
                            return false;
                        }

                        operation = new CopyOperation();
                        _checkpointCopies.TryAdd(contentHash, operation, Configuration.PropagationDelay, extendExpiryIfExists: true);

                        return _checkpointCopies.TryGetValue(contentHash, out operation);
                    }

                    return await base.StreamContentAsync(context, contentHash);
                },
                traceOperationStarted: false,
                extraEndMessage: r => $"HasContent=[{hasContent}] IsCheckpointFile=[{isCheckpointFile}] ResultCode=[{r.Code}]");
        }

        private void TryCompleteCheckpointCopy(ContentHash contentHash)
        {
            if (_checkpointStore != null && _checkpointCopies.TryGetValue(contentHash, out var operation))
            {
                operation.CopyCompletion.TrySetResult(true);
            }
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

        /// <summary>
        /// Provides information about active checkpoint files
        /// </summary>
        public interface ICheckpointStore
        {
            bool IsActiveCheckpointFile(ShortHash hash);
        }
    }
}
