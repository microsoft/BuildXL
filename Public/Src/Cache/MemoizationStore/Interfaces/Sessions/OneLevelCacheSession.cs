// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        private IContentSession ContentSession => (IContentSession)_contentReadOnlySession;

        /// <summary>
        ///     Gets the writable memoization session.
        /// </summary>
        private IMemoizationSession MemoizationSession => (IMemoizationSession)_memoizationReadOnlySession;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneLevelCacheSession" /> class.
        /// </summary>
        public OneLevelCacheSession(string name, ImplicitPin implicitPin, IMemoizationSession memoizationSession, IContentSession contentSession)
            : base(name, implicitPin, memoizationSession, contentSession)
        {
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync
            (
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint
            )
        {
            return MemoizationSession.AddOrGetContentHashListAsync(
                context, strongFingerprint, contentHashListWithDeterminism, cts, urgencyHint);
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
