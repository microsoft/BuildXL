// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Sessions
{
    public class FileSystemContentSession : ContentSessionBase, ITrustedContentSession, IHibernateContentSession, IContentNotFoundRegistration, ILocalContentSessionProvider
    {
        /// <summary>
        ///     The internal content store backing the session.
        /// </summary>
        protected readonly FileSystemContentStoreInternal Store;

        private readonly PinContext _pinContext;
        private readonly ImplicitPin _implicitPin;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(FileSystemContentSession));

        /// <inheritdoc />
        protected override bool TraceErrorsOnly => true; // This type adds nothing in terms of tracing. So configure it to trace errors only.

        private readonly List<Func<Context, ContentHash, Task>> _contentNotFoundListener = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="FileSystemContentSession" /> class.
        /// </summary>
        public FileSystemContentSession(string name, FileSystemContentStoreInternal store, ImplicitPin implicitPin)
            : base(name)
        {
            Store = store;
            _pinContext = Store.CreatePinContext();
            _implicitPin = implicitPin;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext operationContext)
        {
            await _pinContext.DisposeAsync();

            var statsResult = await Store.GetStatsAsync(operationContext);
            if (statsResult.Succeeded)
            {
                Tracer.TraceStatisticsAtShutdown(operationContext, statsResult.CounterSet, prefix: "FileSystemContentSessionStats");
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();
            _pinContext.Dispose();
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PinAsync(operationContext, contentHash, _pinContext);
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.OpenStreamAsync(operationContext, contentHash, MakePinRequest(ImplicitPin.Get));
        }

        /// <inheritdoc />
        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var result = await Store.PlaceFileAsync(
                operationContext,
                contentHash,
                path,
                accessMode,
                replacementMode,
                realizationMode,
                MakePinRequest(ImplicitPin.Put));

            if (result.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound && _contentNotFoundListener.Any())
            {
                await NotifyContentNotFoundListeners(operationContext, contentHash);
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            return EnumerableExtensions.AsTasks<Indexed<PinResult>>(
                (await Store.PinAsync(operationContext, contentHashes, _pinContext, options: null)));
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var results = await Store.PlaceFileAsync(
                operationContext,
                hashesWithPaths,
                accessMode,
                replacementMode,
                realizationMode,
                MakePinRequest(ImplicitPin.Get));

            if (_contentNotFoundListener.Any())
            {
                foreach (var result in results)
                {
                    var indexedItem = await result;
                    if (indexedItem.Item.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
                    {
                        await NotifyContentNotFoundListeners(operationContext, hashesWithPaths[indexedItem.Index].Hash);
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return _pinContext.GetContentHashes();
        }

        /// <inheritdoc />
        async Task IHibernateContentSession.PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            var contentHashList = contentHashes as List<ContentHash> ?? contentHashes.ToList();
            // Passing 'RePinFromHibernation' to use more optimal pinning logic.
            var results = Enumerable.ToList<Indexed<PinResult>>(
                (await Store.PinAsync(context, contentHashList, _pinContext, new PinBulkOptions() { RePinFromHibernation = true })));

            var failed = results.Where(r => !r.Item.Succeeded);
            foreach (var result in failed)
            {
                Tracer.Warning(context, $"Failed to pin contentHash=[{contentHashList[result.Index]}]");
            }
        }

        /// <inheritdoc />
        Task<BoolResult> IHibernateContentSession.ShutdownEvictionAsync(Context context)
        {
            return Store.ShutdownEvictionAsync(context);
        }

        /// <summary>
        ///     Build a PinRequest based on whether auto-pin configuration matches request.
        /// </summary>
        protected PinRequest? MakePinRequest(ImplicitPin implicitPin)
        {
            return (implicitPin & _implicitPin) != ImplicitPin.None ? new PinRequest(_pinContext) : (PinRequest?)null;
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
            return Store.PutFileAsync(operationContext, path, realizationMode, hashType, MakePinRequest(ImplicitPin.Put));
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
            return Store.PutFileAsync(operationContext, path, realizationMode, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(
            Context context,
            ContentHashWithSize contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return Store.PutTrustedFileAsync(context, path, realizationMode, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PutStreamAsync(operationContext, stream, hashType, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PutStreamAsync(operationContext, stream, contentHash, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        public AbsolutePath TryGetWorkingDirectory(AbsolutePath? pathHint)
        {
            return Store.TempFolder;
        }

        /// <inheritdoc />
        public IContentSession? TryGetLocalContentSession()
        {
            return this;
        }

        /// <inheritdoc/>
        public void AddContentNotFoundOnPlaceListener(Func<Context, ContentHash, Task> listener)
        {
            _contentNotFoundListener.Add(listener);
        }

        private async Task NotifyContentNotFoundListeners(OperationContext operationContext, ContentHash contentHash)
        {
            // Notify listeners that content was not found
            foreach (var listener in _contentNotFoundListener!)
            {
                await listener(operationContext, contentHash);
            }
        }
    }
}
