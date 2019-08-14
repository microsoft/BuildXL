// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     An IReadOnlyContentSession implemented over an IContentStoreInternal
    /// </summary>
    public class ReadOnlyFileSystemContentSession : ContentSessionBase, IHibernateContentSession
    {
        /// <summary>
        ///     The internal content store backing the session.
        /// </summary>
        protected readonly IContentStoreInternal Store;

        private readonly PinContext _pinContext;
        private readonly ImplicitPin _implicitPin;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(FileSystemContentSession));

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReadOnlyFileSystemContentSession" /> class.
        /// </summary>
        public ReadOnlyFileSystemContentSession(string name, IContentStoreInternal store, ImplicitPin implicitPin)
            : base(name)
        {
            Store = store;
            _pinContext = Store.CreatePinContext();
            _implicitPin = implicitPin;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext operatonContext)
        {
            await _pinContext.DisposeAsync();

            var statsResult = await Store.GetStatsAsync(operatonContext);
            if (statsResult.Succeeded)
            {
                statsResult.CounterSet.LogOrderedNameValuePairs(s => Tracer.Debug(operatonContext, s));
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
        protected override Task<PinResult> PinCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return Store.PinAsync(operationContext, contentHash, _pinContext);
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return Store.OpenStreamAsync(operationContext, contentHash, MakePinRequest(ImplicitPin.Get));
        }

        /// <inheritdoc />
        protected override Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PlaceFileAsync(operationContext, contentHash, path, accessMode, replacementMode, realizationMode, MakePinRequest(ImplicitPin.Put));
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            return (await Store.PinAsync(operationContext, contentHashes, _pinContext)).AsTasks();
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Store.PlaceFileAsync(operationContext, hashesWithPaths, accessMode, replacementMode, realizationMode, MakePinRequest(ImplicitPin.Get));
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return _pinContext.GetContentHashes();
        }

        /// <inheritdoc />
        public async Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            var contentHashList = contentHashes as List<ContentHash> ?? contentHashes.ToList();
            var results = (await Store.PinAsync(context, contentHashList, _pinContext)).ToList();

            var failed = results.Where(r => !r.Item.Succeeded);
            foreach (var result in failed)
            {
                Tracer.Warning(context, $"Failed to pin contentHash=[{contentHashList[result.Index]}]");
            }
        }

        /// <summary>
        ///     Build a PinRequest based on whether auto-pin configuration matches request.
        /// </summary>
        protected PinRequest? MakePinRequest(ImplicitPin implicitPin)
        {
            return (implicitPin & _implicitPin) != ImplicitPin.None ? new PinRequest(_pinContext) : (PinRequest?)null;
        }
    }
}
