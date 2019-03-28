// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Grpc
{
    internal class TestHangingContentSession : IContentSession, IHibernateContentSession
    {
        private readonly bool _useCancellationToken;
        private readonly SemaphoreSlim _unresponsiveHashStartedSemaphore;

        public string Name { get; }

        internal TestHangingContentSession(string name, bool useCancellationToken, SemaphoreSlim unresponsiveHashStartedSemaphore)
        {
            Name = name;
            _useCancellationToken = useCancellationToken;
            _unresponsiveHashStartedSemaphore = unresponsiveHashStartedSemaphore;
        }

        public bool StartupCompleted { get; private set; }

        public bool StartupStarted { get; private set; }

        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public void Dispose()
        {
        }

        public bool ShutdownCompleted { get; private set; }

        public bool ShutdownStarted { get; private set; }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public async Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PinAsync), cts);
            return new PinResult(result);
        }

        public async Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(OpenStreamAsync), cts);
            return new OpenStreamResult(result);
        }

        public async Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PlaceFileAsync), cts);
            return new PlaceFileResult(result);
        }

        public async Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PinAsync), cts);
            return contentHashes.Select(hashWithPath => new PinResult(result)).AsIndexed().AsTasks();
        }

        public async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PlaceFileAsync), cts);
            return hashesWithPaths.Select(hashWithPath => new PlaceFileResult(result)).AsIndexed().AsTasks();
        }

        public async Task<PutResult> PutFileAsync(
            Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PutFileAsync), cts);
            return new PutResult(result, new ContentHash(hashType));
        }

        public async Task<PutResult> PutFileAsync(
            Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PutFileAsync), cts);
            return new PutResult(result, contentHash);
        }

        public async Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PutStreamAsync), cts);
            return new PutResult(result, new ContentHash(hashType));
        }

        public async Task<PutResult> PutStreamAsync(
            Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            BoolResult result = await UnresponsiveUntilCancelledAsync(context, nameof(PutStreamAsync), cts);
            return new PutResult(result, contentHash);
        }

        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return new List<ContentHash>();
        }

        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return Task.FromResult(0);
        }

        private async Task<BoolResult> UnresponsiveUntilCancelledAsync(Context context, string operationName, CancellationToken cts)
        {
            try
            {
                _unresponsiveHashStartedSemaphore.Release();
                if (_useCancellationToken)
                {
                    await Task.Delay(Timeout.Infinite, cts);
                }
                else
                {
                    // ReSharper disable once MethodSupportsCancellation
                    await Task.Delay(Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                context.TraceMessage(Severity.Debug, $"{operationName} threw an exception during unresponsiveness for a test: {ex}");
                throw;
            }

            return new BoolResult($"{operationName} was cancelled");
        }
    }
}
