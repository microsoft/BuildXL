// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Checks two level caches for errors. Checks each level individually first, then checks between them for determinism errors.
    /// If the cache is specified via json, only a VerticalCacheAggregator can be checked. Otherwise, must specify the cache instances individually.
    /// </summary>
    public sealed class TwoLevelCacheChecker
    {
        /// <summary>
        /// SingleCacheChecker instance for the local cache
        /// </summary>
        private readonly SingleCacheChecker m_localChecker;

        /// <summary>
        /// SingleCacheChecker instance for the remote cache
        /// </summary>
        private readonly SingleCacheChecker m_remoteChecker;

        /// <summary>
        /// Total numbers of session in cache being checked. This value is
        /// only valid if the check was done through the sessions.
        /// </summary>
        internal int NumSessions => m_remoteChecker.NumSessions;

        /// <summary>
        /// This is the number of sessions that were enumerated for their
        /// strong fingerprints.
        /// </summary>
        internal int NumSessionsChecked => m_remoteChecker.NumSessionsChecked;

        /// <summary>
        /// The number of full cache records that were checked in the remote
        /// cache.
        /// </summary>
        internal int NumFullCacheRecords => m_remoteChecker.AllFullCacheRecords.Count;

        /// <summary>
        /// Guid of the remote cache
        /// </summary>
        private readonly Guid m_remoteGuid;

        /// <summary>
        /// Creates two SingleCacheChecker instances, one for the local cache and one for the remote cache
        /// </summary>
        /// <param name="localCache">Local cache to check</param>
        /// <param name="remoteCache">Remote cache to check</param>
        /// <param name="checkCASContent">If true, each CAS content file will be downloaded and rehashed to ensure that the content has not been corrupted since initially being put in cache</param>
        public TwoLevelCacheChecker(ICache localCache, ICache remoteCache, bool checkCASContent)
        {
            m_localChecker = new SingleCacheChecker(localCache, checkCASContent, true);
            m_remoteChecker = new SingleCacheChecker(remoteCache, checkCASContent, false);
            m_remoteGuid = remoteCache.CacheGuid;
        }

        /// <summary>
        /// Checks for discrepancies in the CasEntries between the two caches for each StrongFingerprint
        /// </summary>
        private IEnumerable<CacheError> CheckDeterminism()
        {
            var localDictionary = m_localChecker.AllFullCacheRecords;
            var remoteDictionary = m_remoteChecker.AllFullCacheRecords;
            foreach (var entry in localDictionary)
            {
                Possible<CasEntries, Failure> possibleCasEntries = entry.Value.Result;
                if (!possibleCasEntries.Succeeded)
                {
                    // A failure here indicates that the remote cache has a full cache record that the local cache does not. This is not an error.
                    continue;
                }

                CasEntries casEntries = possibleCasEntries.Result;
                if (casEntries.Determinism.IsDeterministicTool)
                {
                    if (!remoteDictionary[entry.Key].Equals(entry.Value))
                    {
                        yield return new CacheError(CacheErrorType.DeterminismError, "Tool Determinism Error on StrongFingerprint: " + entry.Key);
                    }
                }
                else if (casEntries.Determinism.Guid.Equals(m_remoteGuid))
                {
                    if (!remoteDictionary[entry.Key].Equals(entry.Value))
                    {
                        yield return new CacheError(CacheErrorType.DeterminismError, "Cache Determinism Error on StrongFingerprint: " + entry.Key);
                    }
                }
            }
        }

        private async Task<IEnumerable<CacheError>> CheckCache(Task<IEnumerable<CacheError>> remoteCacheChecker)
        {
            // Check remote cache
            var remoteCacheErrors = await remoteCacheChecker;

            // Check local cache
            var localCacheErrors = await m_localChecker.CheckCache(m_remoteChecker.GetStrongFingerprints());

            IEnumerable<CacheError> errors = remoteCacheErrors.Concat(localCacheErrors);

            IEnumerable<CacheError> determinismErrors = CheckDeterminism();

            errors = errors.Concat(determinismErrors);

            return errors;
        }

        /// <summary>
        /// First individually checks the remote and local caches for
        /// consistency errors, then checks both for determinism errors
        /// </summary>
        /// <remarks>The remote cache must support sessions but the local does
        /// not have to</remarks>
        /// <param name="sessionRegex">Filters which sessions are used</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>All the errors found</returns>
        public Task<IEnumerable<CacheError>> CheckCache(Regex sessionRegex, ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            return CheckCache(m_remoteChecker.CheckCache(sessionRegex, weakFingerprintsFound));
        }

        /// <summary>
        /// First individually checks the remote and local caches for
        /// consistency errors, then checks both for determinism errors
        /// </summary>
        /// <param name="weakFingerprints">Weak fingerprints to enunerate
        /// strong fingerprints from to then do check with</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>All the errors found</returns>
        public Task<IEnumerable<CacheError>> CheckCache(IEnumerable<WeakFingerprintHash> weakFingerprints, ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            return CheckCache(m_remoteChecker.CheckCache(weakFingerprints, weakFingerprintsFound));
        }
    }
}
