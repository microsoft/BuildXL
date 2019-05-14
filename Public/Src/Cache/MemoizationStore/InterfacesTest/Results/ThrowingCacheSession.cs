// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    internal class ThrowingCacheSession : ICacheSession
    {
        public ThrowingCacheSession(string name = null)
        {
            Name = name;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool StartupCompleted { get; }

        /// <inheritdoc />
        public bool StartupStarted { get; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new System.NotImplementedException();
        }
    }
}
