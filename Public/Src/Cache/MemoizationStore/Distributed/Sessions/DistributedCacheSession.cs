// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Sessions
{
    /// <summary>
    /// Distributed cache session
    /// </summary>
    public class DistributedCacheSession : ReadOnlyDistributedCacheSession, ICacheSession
    {
        private readonly ICacheSession _innerCacheSession;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DistributedCacheSession" /> class.
        /// </summary>
        public DistributedCacheSession(
            ILogger logger,
            string name,
            ICacheSession innerCacheSession,
            Guid innerCacheId,
            IMetadataCache metadataCache,
            DistributedCacheSessionTracer tracer,
            ReadThroughMode readThroughModeMode)
            : base(logger, name, innerCacheSession, innerCacheId, metadataCache, tracer, readThroughModeMode)
        {
            Contract.Requires(logger != null);
            Contract.Requires(innerCacheSession != null);
            Contract.Requires(metadataCache != null);

            _innerCacheSession = innerCacheSession;
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return AddOrGetContentHashListCall.RunAsync(DistributedTracer, OperationContext(context), strongFingerprint, async () =>
            {
                // Metadata cache assumes no guarantees about the fingerprints added, hence invalidate the cache and serve the request using backing store
                var cacheResult = await MetadataCache.DeleteFingerprintAsync(context, strongFingerprint);
                if (!cacheResult.Succeeded)
                {
                    Logger.Error($"Error while removing fingerprint {strongFingerprint} from metadata cache. Result: {cacheResult}.");
                }

                return await _innerCacheSession.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, cts, urgencyHint);
            });
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _innerCacheSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }
    }
}
