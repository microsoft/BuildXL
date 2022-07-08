// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     An ICacheSession implemented with one level of content and memoization.
    /// </summary>
    public class OneLevelCacheSession : ReadOnlyOneLevelCacheSession, ICacheSession
    {
        /// <summary>
        ///     Gets the writable content session.
        /// </summary>
        public IContentSession ContentSession => (IContentSession)ContentReadOnlySession;

        /// <summary>
        ///     Gets the writable memoization session.
        /// </summary>
        public IMemoizationSession MemoizationSession => (IMemoizationSession)MemoizationReadOnlySession;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneLevelCacheSession" /> class.
        /// </summary>
        public OneLevelCacheSession(
            OneLevelCacheBase parent,
            string name,
            ImplicitPin implicitPin,
            IMemoizationSession memoizationSession,
            IContentSession contentSession)
            : base(parent, name, implicitPin, memoizationSession, contentSession)
        {
        }

        /// <inheritdoc />
        public async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            var result = await MemoizationSession.AddOrGetContentHashListAsync(
                context, strongFingerprint, contentHashListWithDeterminism, cts, urgencyHint);

            if (result.Succeeded && Parent is not null && result.ContentHashListWithDeterminism.ContentHashList is not null)
            {
                Parent.AddOrExtendPin(context, strongFingerprint.Selector.ContentHash);

                var contentHashList = result.ContentHashListWithDeterminism.ContentHashList.Hashes;
                foreach (var contentHash in contentHashList)
                {
                    Parent.AddOrExtendPin(context, contentHash);
                }
            }

            return result;
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return MemoizationSession.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync
            (
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint
            )
        {
            return ContentSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }
    }
}
