
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Cache.Plugin.CacheCore
{
    using static CacheActivityRegistry;

    /// <summary>
    ///     Simple wrapper around <see cref="ICacheSession"/> which populates activity id
    ///     based on <see cref="CacheActivityRegistry"/>
    /// </summary>
    public sealed class RegisteredCacheActivityCacheSession(ICacheSession cache) : ICacheSession
    {
        /// <inheritdoc />
        private readonly ICacheSession m_cache = cache;

        /// <inheritdoc />
        public CacheId CacheId => m_cache.CacheId;

        /// <inheritdoc />
        public string CacheSessionId => m_cache.CacheSessionId;

        /// <inheritdoc />
        public bool IsClosed => m_cache.IsClosed;

        /// <inheritdoc />
        public bool StrictMetadataCasCoupling => m_cache.StrictMetadataCasCoupling;

        /// <inheritdoc />
        public Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default)
        {
            return m_cache.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<CasHash, Failure>> AddToCasAsync(string filename, FileState fileState, CasHash? hash = null, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default)
        {
            return m_cache.AddToCasAsync(filename, fileState, hash, urgencyHint, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash = null, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default)
        {
            return m_cache.AddToCasAsync(filestream, hash, urgencyHint, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<string, Failure>> CloseAsync(Guid activityId = default)
        {
            return m_cache.CloseAsync(GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId = default)
        {
            return m_cache.EnumerateSessionFingerprints(GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, OperationHints hints = default, Guid activityId = default)
        {
            return m_cache.EnumerateStrongFingerprints(weak, hints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, OperationHints hints = default, Guid activityId = default)
        {
            return m_cache.GetCacheEntryAsync(strong, hints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId = default)
        {
            return m_cache.GetStatisticsAsync(GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<StreamWithLength, Failure>> GetStreamAsync(CasHash hash, OperationHints hints = default, Guid activityId = default)
        {
            return m_cache.GetStreamAsync(hash, hints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId = default)
        {
            return m_cache.IncorporateRecordsAsync(strongFingerprints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, CancellationToken cancellationToken, OperationHints hints = default, Guid activityId = default)
        {
            return m_cache.PinToCasAsync(hash, cancellationToken, hints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, CancellationToken cancellationToken, OperationHints hints = default, Guid activityId = default)
        {
            return m_cache.PinToCasAsync(hashes, cancellationToken, hints, GetOrNewContextActivityId(activityId));
        }

        /// <inheritdoc />
        public Task<Possible<string, Failure>> ProduceFileAsync(CasHash hash, string filename, FileState fileState, OperationHints hints = default, Guid activityId = default, CancellationToken cancellationToken = default)
        {
            return m_cache.ProduceFileAsync(hash, filename, fileState, hints, GetOrNewContextActivityId(activityId), cancellationToken);
        }

        /// <inheritdoc />
        public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint = UrgencyHint.Nominal, Guid activityId = default)
        {
            return m_cache.ValidateContentAsync(hash, urgencyHint, GetOrNewContextActivityId(activityId));
        }
    }
}
