// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Checks for errors that can be detected through the
    /// ICache interface for a single cache. Does not do any
    /// determinism checks.
    /// </summary>
    public sealed class SingleCacheChecker
    {
        /// <summary>
        /// The cache being checked
        /// </summary>
        private readonly ICache m_cache;

        /// <summary>
        /// A read only session to the cache being checked
        /// </summary>
        private readonly ICacheReadOnlySession m_readOnlySession;

        /// <summary>
        /// If true, each CAS content file will be downloaded and
        /// rehashed to ensure that the content has not been
        /// corrupted since initially being put in cache
        /// </summary>
        private readonly bool m_checkCASContent;

        /// <summary>
        /// If true, this indicates to the checker that the current
        /// cache being checked is a local cache. A local cache is
        /// a cache that has another cache that it is deterministic
        /// relative to and draws data from
        /// </summary>
        private readonly bool m_isLocalCache;

        private int m_numSessions = 0;

        private int m_numSessionsChecked = 0;

        /// <summary>
        /// Total numbers of session in cache being checked. This value is
        /// only valid if the check was done through the sessions.
        /// </summary>
        internal int NumSessions => m_numSessions;

        /// <summary>
        /// This is the number of sessions that were enumerated for their
        /// strong fingerprints.
        /// </summary>
        internal int NumSessionsChecked => m_numSessionsChecked;

        /// <summary>
        /// Stores all the unique StrongFingerprints and their
        /// respective CasEntries found so far for the cache being
        /// checked
        /// </summary>
        internal ConcurrentDictionary<StrongFingerprint, Task<Possible<CasEntries, Failure>>> AllFullCacheRecords { get; } =
            new ConcurrentDictionary<StrongFingerprint, Task<Possible<CasEntries, Failure>>>();

        /// <summary>
        /// Stores all the unique CasHashes that have been checked
        /// so far. This data structure is used to avoid rechecking
        /// CasHashes
        /// </summary>
        internal ConcurrentDictionary<CasHash, Task<byte>> AllCasHashes { get; } =
            new ConcurrentDictionary<CasHash, Task<byte>>();

        /// <summary>
        /// Gets all the StrongFingerprints that were found for the
        /// cache being checked
        /// </summary>
        public IEnumerable<StrongFingerprint> GetStrongFingerprints()
        {
            return AllFullCacheRecords.Keys;
        }

        /// <summary>
        /// Uses the cache instance that will be checked to generate
        /// a read only session to the cache
        /// </summary>
        /// <param name="cache">The cache instance to check</param>
        /// <param name="checkCASContent">If true, each CAS content file
        /// will be downloaded and rehashed to ensure that the content
        /// has not been corrupted since initially being put in cache</param>
        /// <param name="isLocalCache">Specifies whether the cache is local</param>
        public SingleCacheChecker(ICache cache, bool checkCASContent, bool isLocalCache = false)
        {
            m_cache = cache;
            m_readOnlySession = m_cache.CreateReadOnlySessionAsync().Result.Result;
            m_checkCASContent = checkCASContent;
            m_isLocalCache = isLocalCache;
        }

        /// <summary>
        /// Uses the specified json config string to initialize the
        /// cache and then uses that cache instance to generate a read
        /// only session to the cache
        /// </summary>
        /// <param name="cacheConfigJSONData">Json string that represents
        /// the cache to check</param>
        /// <param name="checkCASContent">If true, each CAS content file
        /// will be downloaded and rehashed to ensure that the content has
        /// not been corrupted since initially being put in cache</param>
        /// <param name="isLocalCache">Specifies whether the cache is local</param>
        public SingleCacheChecker(string cacheConfigJSONData, bool checkCASContent, bool isLocalCache = false)
        {
            m_cache = CacheFactory.InitializeCacheAsync(cacheConfigJSONData, default(Guid)).Result.Result;
            m_readOnlySession = m_cache.CreateReadOnlySessionAsync().Result.Result;
            m_checkCASContent = checkCASContent;
            m_isLocalCache = isLocalCache;
        }

        /// <summary>
        /// Gets the file corresponding to the given CasHash and checks
        /// to see if the file contents hash to the same CasHash value
        /// </summary>
        /// <param name="originalCasHash">CasHash value to check</param>
        /// <param name="errors">Where any cache errors found get stored</param>
        private async Task RehashContentsAsync(CasHash originalCasHash, ConcurrentDictionary<CacheError, int> errors)
        {
            if (originalCasHash.Equals(CasHash.NoItem))
            {
                // No need to rehash the NoItem cas hash
                return;
            }

            Possible<Stream, Failure> possibleStream = await m_readOnlySession.GetStreamAsync(originalCasHash);
            if (!possibleStream.Succeeded)
            {
                errors.TryAdd(new CacheError(CacheErrorType.CasHashError, "CasHash " + originalCasHash + " not found in CAS"), 0);
                return;
            }

            using (Stream stream = possibleStream.Result)
            {
                ContentHash contentHash = await ContentHashingUtilities.HashContentStreamAsync(stream);
                Hash newHash = new Hash(contentHash);
                CasHash newCasHash = new CasHash(newHash);
                if (!originalCasHash.Equals(newCasHash))
                {
                    errors.TryAdd(new CacheError(CacheErrorType.CasHashError, "The data of CasHash " + originalCasHash + " has been altered in the CAS"), 0);
                }
            }
        }

        /// <summary>
        /// Attempts to pin the specified CasHash. Returns true if the
        /// pinning succeeds.
        /// </summary>
        /// <param name="casHash">CasHash value to attempt to pin</param>
        private async Task<bool> AttemptToPinAsync(CasHash casHash)
        {
            if (casHash.Equals(CasHash.NoItem))
            {
                return true;
            }

            Possible<string, Failure> pinAttempt = await m_readOnlySession.PinToCasAsync(casHash);
            return pinAttempt.Succeeded;
        }

        /// <summary>
        /// Checks the specified CasHash for potential problems by first
        /// attempting to pin it and then optionally rehashes the file
        /// contents.
        /// </summary>
        /// <param name="casHash">CasHash value to check</param>
        /// <param name="errors">Where any cache errors found get stored</param>
        private async Task CheckCasHashAsync(CasHash casHash, ConcurrentDictionary<CacheError, int> errors)
        {
            await AllCasHashes.GetOrAdd(casHash, async (cH) =>
            {
                if (!(await AttemptToPinAsync(casHash)))
                {
                    // CasHash failed to be pinned
                    errors.TryAdd(new CacheError(CacheErrorType.CasHashError, "CasHash " + casHash + " not found in CAS"), 0);
                    return 0;
                }
                if (m_checkCASContent)
                {
                    await RehashContentsAsync(casHash, errors);
                }
                return 0;
            });
        }

        /// <summary>
        /// Checks for any errors with the cas element
        /// and with each of the cas entries
        /// </summary>
        /// <param name="casElement">cas element of a strong fingerprint to check</param>
        /// <param name="casEntries">cas entries to check</param>
        /// <param name="errors">Where any cache errors found get stored</param>
        private async Task CheckCas(CasHash casElement, CasEntries casEntries, ConcurrentDictionary<CacheError, int> errors)
        {
            // Check CasElement
            await CheckCasHashAsync(casElement, errors);

            for (int i = 0; i < casEntries.Count(); i++)
            {
                // Check each CasHash
                CasHash casHash = casEntries[i];
                await CheckCasHashAsync(casHash, errors);
            }
        }

        /// <summary>
        /// Checks for errors in any of the cas entries for the whole cache
        /// </summary>
        /// <param name="errors">Where any cache errors found get stored</param>
        private IEnumerable<Task> CheckAllCacheEntries(ConcurrentDictionary<CacheError, int> errors)
        {
            // Check each record
            foreach (var kv in AllFullCacheRecords)
            {
                yield return Task.Run(async () =>
                {
                    var possibleCasEntries = await kv.Value.ConfigureAwait(false);
                    if (!possibleCasEntries.Succeeded)
                    {
                        if (!m_isLocalCache)
                        {
                            // If a local cache is missing a cas entries, this is not an error
                            errors.TryAdd(new CacheError(CacheErrorType.StrongFingerprintError, "The CasEntries for StrongFingerprint " + kv.Key + " could not be found."), 0);
                        }
                    }
                    else
                    {
                        await CheckCas(kv.Key.CasElement, possibleCasEntries.Result, errors);
                    }
                });
            }
        }

        /// <summary>
        /// Asynchronously loads all of the cache entries associated with the
        /// given strong fingerprints
        /// </summary>
        /// <param name="strongFingerprintTasks">
        /// Strong fingerprints of the cache entries to load
        /// </param>
        /// <param name="errors">
        /// Where any cache errors found get stored
        /// </param>
        /// <param name="weakFingerprintsFound">
        /// If not null, all weak fingerpints found will be added
        /// </param>
        private IEnumerable<Task> LoadCacheEntriesAsync(
            IEnumerable<Task<StrongFingerprint>> strongFingerprintTasks,
            ConcurrentDictionary<CacheError, int> errors,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound)
        {
            foreach (Task<StrongFingerprint> strongFingerprintTask in strongFingerprintTasks)
            {
                yield return Task.Run(async () =>
                {
                    StrongFingerprint strongFingerprint = await strongFingerprintTask.ConfigureAwait(false);
                    if (strongFingerprint == null)
                    {
                        errors.TryAdd(new CacheError(CacheErrorType.SessionError, "Null StrongFingerprint"), 0);
                        return;
                    }

                    Func<StrongFingerprint, Task<Possible<CasEntries, Failure>>> delegateToUse;
                    if (weakFingerprintsFound == null)
                    {
                        delegateToUse = (sfp) => m_readOnlySession.GetCacheEntryAsync(sfp);
                    }
                    else
                    {
                        delegateToUse = (sfp) =>
                        {
                            weakFingerprintsFound.TryAdd(sfp.WeakFingerprint, 0);
                            return m_readOnlySession.GetCacheEntryAsync(sfp);
                        };
                    }

                    // Only check StrongFingerprint if never seen before
                    Analysis.IgnoreResult(
                        await AllFullCacheRecords.GetOrAdd(strongFingerprint, delegateToUse).ConfigureAwait(false)
                    );
                });
            }
        }

        /// <summary>
        /// Loads all the unqiue cache entries for all of the specified sessions
        /// </summary>
        /// <param name="enumeratedCompletedSessions">Sessions to load cache entries from</param>
        /// <param name="errors">Where any cache errors found get stored</param>
        /// <param name="sessionRegex">Acts as a filter for which sessions to load cache entries from</param>
        /// <param name="weakFingerprintsFound"> If not null, all weak fingerpints found will be added</param>
        private IEnumerable<Task> LoadAllCacheEntriesAsync(
            IEnumerable<Task<string>> enumeratedCompletedSessions,
            ConcurrentDictionary<CacheError, int> errors,
            Regex sessionRegex,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound)
        {
            foreach (Task<string> completedSession in enumeratedCompletedSessions)
            {
                yield return Task.Run(async () =>
                {
                    string sessionId = await completedSession.ConfigureAwait(false);
                    Interlocked.Increment(ref m_numSessions);
                    if (sessionRegex.IsMatch(sessionId))
                    {
                        Interlocked.Increment(ref m_numSessionsChecked);
                        IEnumerable<Task<StrongFingerprint>> strongFingerprints = m_cache.EnumerateSessionStrongFingerprints(sessionId).Result;

                        IEnumerable<Task> cachEntryLoaders = LoadCacheEntriesAsync(strongFingerprints, errors, weakFingerprintsFound);

                        foreach (Task task in cachEntryLoaders.OutOfOrderTasks())
                        {
                            await task;
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Uses the EnumerateCompletedSessions method of the ICache
        /// interface to check for errors in the cache.
        /// </summary>
        /// <param name="sessionRegex">Acts as a filter for which sessions to check.</param>
        /// <param name="weakFingerprintsFound"> If not null, all weak fingerpints found will be added</param>
        /// <returns>All the errors found</returns>
        public async Task<IEnumerable<CacheError>> CheckCache(
            Regex sessionRegex,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Assume(sessionRegex != null);

            m_numSessions = 0;
            m_numSessionsChecked = 0;

            ConcurrentDictionary<CacheError, int> errors = new ConcurrentDictionary<CacheError, int>();

            // Get all the sessions
            IEnumerable<Task<string>> enumeratedCompletedSessions = m_cache.EnumerateCompletedSessions();

            // Load the cache entries for each session
            IEnumerable<Task> allCacheEntriesLoaders = LoadAllCacheEntriesAsync(enumeratedCompletedSessions, errors, sessionRegex, weakFingerprintsFound);
            foreach (Task task in allCacheEntriesLoaders.OutOfOrderTasks())
            {
                await task;
            }

            // Check the cache entries for all sessions
            IEnumerable<Task> allCacheEntriesCheckers = CheckAllCacheEntries(errors);
            foreach (Task task in allCacheEntriesCheckers.OutOfOrderTasks())
            {
                await task;
            }

            return errors.Keys;
        }

        /// <summary>
        /// Checks the cache for consistency errors through the provided
        /// enumeration of strong fingerprints
        /// </summary>
        /// <param name="strongFingerprints">The strong fingerprints to use</param>
        /// <param name="weakFingerprintsFound"> If not null, all weak fingerpints found will be added</param>
        /// <returns>All the errors found</returns>
        public async Task<IEnumerable<CacheError>> CheckCache(
            IEnumerable<StrongFingerprint> strongFingerprints,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            m_numSessions = 0;
            m_numSessionsChecked = 0;

            ConcurrentDictionary<CacheError, int> errors = new ConcurrentDictionary<CacheError, int>();

            IEnumerable<Task<StrongFingerprint>> strongFingerprintTasks = strongFingerprints.EnumerateIntoTasks();

            // Load all the cache entries
            IEnumerable<Task> allCacheEntriesLoaders = LoadCacheEntriesAsync(strongFingerprintTasks, errors, weakFingerprintsFound);
            foreach (Task task in allCacheEntriesLoaders.OutOfOrderTasks())
            {
                await task;
            }

            // Check the cache entries for all strong fingerprints
            IEnumerable<Task> allCacheEntriesCheckers = CheckAllCacheEntries(errors);
            foreach (Task task in allCacheEntriesCheckers.OutOfOrderTasks())
            {
                await task;
            }

            return errors.Keys;
        }

        /// <summary>
        /// Checks the cache for consistency errors through the provided
        /// enumeration of weak fingerprints
        /// </summary>
        /// <param name="weakFingerprints">The weak fingerprints to use</param>
        /// <param name="weakFingerprintsFound"> If not null, all weak fingerpints found will be added</param>
        /// <returns>All the errors found</returns>
        public Task<IEnumerable<CacheError>> CheckCache(
            IEnumerable<WeakFingerprintHash> weakFingerprints,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            IEnumerable<StrongFingerprint> strongFingerprints = weakFingerprints.ProduceStrongFingerprints(m_readOnlySession);
            return CheckCache(strongFingerprints, weakFingerprintsFound);
        }
    }
}
