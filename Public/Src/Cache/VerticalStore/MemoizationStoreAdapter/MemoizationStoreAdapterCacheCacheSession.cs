// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;
using ICacheSession = BuildXL.Cache.Interfaces.ICacheSession;
using StrongFingerprint = BuildXL.Cache.Interfaces.StrongFingerprint;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// CacheSession for MemoizationStoreAdapterCache
    /// </summary>
    public sealed class MemoizationStoreAdapterCacheCacheSession : MemoizationStoreAdapterCacheReadOnlySession, ICacheSession
    {
        private BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ICacheSession CacheSession => (BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ICacheSession)ReadOnlyCacheSession;

        /// <summary>
        /// Error message when failing to make space.
        /// </summary>
        /// <remarks>
        /// This message must be kept in sync with the one in CloudStore codebase.
        /// </remarks>
        private static readonly string[] s_outOfSpaceError =
        {
            "Error: Failed to make space",
            "Not purging because content cannot fit",
        };

        private volatile bool m_outOfSpace;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="cacheSession">Backing CacheSession for content and fingerprint calls</param>
        /// <param name="cache">Backing cache.</param>
        /// <param name="cacheId">Id of the parent cache that spawned the session.</param>
        /// <param name="logger">Diagnostic logger</param>
        /// <param name="sessionId">Telemetry ID for the session.</param>
        /// <param name="replaceExistingOnPlaceFile">If true, replace existing file on placing file from cache.</param>
        public MemoizationStoreAdapterCacheCacheSession(
            BuildXL.Cache.MemoizationStore.Interfaces.Sessions.ICacheSession cacheSession,
            BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache cache,
            string cacheId,
            ILogger logger,
            string sessionId = null,
            bool replaceExistingOnPlaceFile = false)
            : base(cacheSession, cache, cacheId, logger, sessionId, replaceExistingOnPlaceFile)
        {
        }

        /// <inheritdoc />
        public async Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            if (m_outOfSpace)
            {
                return new CacheFailure(s_outOfSpaceError[0]);
            }

            PutResult result;
            if (hash.HasValue)
            {
                result = await CacheSession.PutFileAsync(
                    new Context(Logger),
                    hash.Value.ToContentHash(),
                    new BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath(filename),
                    fileState.ToMemoization(),
                    CancellationToken.None);
            }
            else
            {
                result = await CacheSession.PutFileAsync(
                    new Context(Logger),
                    ContentHashingUtilities.HashInfo.HashType,
                    new BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath(filename),
                    fileState.ToMemoization(),
                    CancellationToken.None);
            }

            if (result.Succeeded)
            {
                return result.ContentHash.FromMemoization();
            }

            string outOfSpaceError;

            if (!m_outOfSpace && IsOutOfSpacePut(result, out outOfSpaceError))
            {
                m_outOfSpace = true;
            }

            return new CacheFailure(result.ErrorMessage);
        }

        /// <inheritdoc />
        public async Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            if (m_outOfSpace)
            {
                return new CacheFailure(s_outOfSpaceError[0]);
            }

            PutResult result;
            if (hash.HasValue)
            {
                result = await CacheSession.PutStreamAsync(new Context(Logger), hash.Value.ToContentHash(), filestream, CancellationToken.None);
            }
            else
            {
                result = await CacheSession.PutStreamAsync(new Context(Logger), ContentHashingUtilities.HashInfo.HashType, filestream, CancellationToken.None);
            }

            if (result.Succeeded)
            {
                return result.ContentHash.FromMemoization();
            }

            string outOfSpaceError;

            if (!m_outOfSpace && IsOutOfSpacePut(result, out outOfSpaceError))
            {
                m_outOfSpace = true;
            }

            return new CacheFailure(result.ErrorMessage);
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            var addResult = await CacheSession.AddOrGetContentHashListAsync(
                new Context(Logger),
                new BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint(
                    weak.ToMemoization(),
                    new Selector(casElement.ToMemoization(), hashElement.RawHash.ToByteArray())),
                hashes.ToMemoization(),
                CancellationToken.None);

            var strong = new StrongFingerprint(weak, casElement, hashElement, CacheId);
            switch (addResult.Code)
            {
                case AddOrGetContentHashListResult.ResultCode.Success:
                    SessionEntries?.TryAdd(strong, 1);

                    return addResult.ContentHashListWithDeterminism.ContentHashList == null
                        ? new FullCacheRecordWithDeterminism(addResult.ContentHashListWithDeterminism.Determinism.FromMemoization())
                        : new FullCacheRecordWithDeterminism(new FullCacheRecord(strong, addResult.ContentHashListWithDeterminism.FromMemoization()));

                case AddOrGetContentHashListResult.ResultCode.SinglePhaseMixingError:
                    return new SinglePhaseMixingFailure(CacheId);
                case AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError:
                    return new NotDeterministicFailure(
                        CacheId,
                        new FullCacheRecord(strong, addResult.ContentHashListWithDeterminism.FromMemoization()),
                        new FullCacheRecord(strong, hashes));
                case AddOrGetContentHashListResult.ResultCode.Error:
                    return new CacheFailure(addResult.ErrorMessage);
                default:
                    return new CacheFailure("Unrecognized AddOrGetContentHashListAsync result code: " + addResult.Code + ", error message: " + (addResult.ErrorMessage ?? string.Empty));
            }
        }

        /// <inheritdoc />
        public async Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            var sfpList = new List<StrongFingerprint>();
            int count = 0;
            foreach (var sfpTask in strongFingerprints)
            {
                var sfp = await sfpTask;
                sfpList.Add(sfp);

                SessionEntries?.TryAdd(sfp, 1);
                count++;
            }

            BoolResult incorporateResult =
                await CacheSession.IncorporateStrongFingerprintsAsync(
                    new Context(Logger),
                    sfpList.Select(sfp => Task.FromResult(new BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint(
                        sfp.WeakFingerprint.ToMemoization(),
                        new Selector(sfp.CasElement.ToMemoization(), sfp.HashElement.RawHash.ToByteArray())))),
                    CancellationToken.None);

            return incorporateResult.Succeeded ? count : new Possible<int, Failure>(new CacheFailure(incorporateResult.ErrorMessage));
        }

        /// <inheritdoc />
        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            if (SessionEntries != null)
            {
                foreach (StrongFingerprint fingerprint in SessionEntries.Keys)
                {
                    yield return Task.FromResult(fingerprint);
                }
            }
        }

        private static bool IsOutOfSpacePut(PutResult putResult, out string error)
        {
            Contract.Requires(!putResult.Succeeded);
            return IsOutOfSpaceError(putResult.ErrorMessage, out error);
        }

        internal static bool IsOutOfSpaceError(string result, out string error)
        {
            error = null;

            foreach (var s in s_outOfSpaceError)
            {
                if (result.Contains(s))
                {
                    error = s;
                    return true;
                }
            }

            return false;
        }
    }
}
