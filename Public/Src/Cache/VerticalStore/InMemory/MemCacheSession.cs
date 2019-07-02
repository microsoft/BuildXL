// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.InMemory
{
    internal sealed class MemCacheSession : ICacheSession
    {
        /// <summary>
        /// This is the actual cache underlying this session
        /// </summary>
        internal readonly MemCache Cache;

        private readonly string m_sessionId;

        private readonly bool m_readOnly;

        internal readonly ConcurrentDictionary<StrongFingerprint, int> SessionEntries;

        // Cas entries that have been pinned
        private readonly ConcurrentDictionary<CasHash, int> m_pinnedToCas;

        // Set to true when the session is closed
        private bool m_closed = false;

        private MemCacheSession(MemCache cache, string sessionId, bool readOnly)
        {
            Contract.Requires(cache != null);

            Cache = cache;
            m_sessionId = sessionId;
            m_readOnly = readOnly;
            m_pinnedToCas = new ConcurrentDictionary<CasHash, int>();

            // No-item is always already here (no special pinning required)
            m_pinnedToCas.TryAdd(CasHash.NoItem, 0);

            SessionEntries = (!readOnly && !string.IsNullOrEmpty(sessionId)) ? new ConcurrentDictionary<StrongFingerprint, int>() : null;
        }

        // Sessions with an ID are never read-only
        internal MemCacheSession(MemCache cache, string sessionId)
            : this(cache, sessionId, false)
        {
            Contract.Requires(!string.IsNullOrEmpty(sessionId));
        }

        // Sessions that may be read-only can never have an ID
        internal MemCacheSession(MemCache cache, bool readOnly)
            : this(cache, null, readOnly)
        {
        }

        // Add a record to the session if we are recording a session
        private void AddSessionRecord(FullCacheRecord record)
        {
            if (SessionEntries != null)
            {
                SessionEntries.TryAdd(record.StrongFingerprint, 1);
            }
        }

        // Return true if any of the CasHashes passed in are missing
        private bool HasMissingContent(IEnumerable<CasHash> casHashes)
        {
            foreach (CasHash casHash in casHashes)
            {
                if (!Cache.CasStorage.ContainsKey(casHash))
                {
                    return true;
                }
            }

            return false;
        }

        private Possible<FullCacheRecordWithDeterminism, Failure> AddOrGet(WeakFingerprintHash weak, CasHash casElement, BuildXL.Cache.Interfaces.Hash hashElement, CasEntries hashes)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(hashes.IsValid);

            Contract.Assert(!m_readOnly);

            // We check the Cas entries if we are strict
            if (StrictMetadataCasCoupling)
            {
                // Check that the content is valid.
                if (!m_pinnedToCas.ContainsKey(casElement))
                {
                    return new UnpinnedCasEntryFailure(CacheId, casElement);
                }

                foreach (CasHash hash in hashes)
                {
                    if (!m_pinnedToCas.ContainsKey(hash))
                    {
                        return new UnpinnedCasEntryFailure(CacheId, hash);
                    }
                }
            }

            var strongFingerprints = Cache.Fingerprints.GetOrAdd(weak, (key) => new ConcurrentDictionary<StrongFingerprint, FullCacheRecord>());

            var record = strongFingerprints.AddOrUpdate(
                new StrongFingerprint(weak, casElement, hashElement, Cache.CacheId),
                (strong) => new FullCacheRecord(strong, hashes),
                (strong, oldRecord) =>
                    {
                        // Do no harm here - we will recheck this outside to produce the error that it was a bad attempt
                        if (oldRecord.CasEntries.Determinism.IsSinglePhaseNonDeterministic != hashes.Determinism.IsSinglePhaseNonDeterministic)
                        {
                            return oldRecord;
                        }

                        // We replace if we are SinglePhaseNonDeterministic *or*
                        // if we are upgrading the determinism.
                        if (hashes.Determinism.IsSinglePhaseNonDeterministic ||
                            (!oldRecord.CasEntries.Determinism.IsDeterministicTool &&
                            (hashes.Determinism.IsDeterministic && !hashes.Determinism.Equals(oldRecord.CasEntries.Determinism))) ||
                            HasMissingContent(oldRecord.CasEntries))
                        {
                            oldRecord = new FullCacheRecord(strong, hashes);
                        }

                        return oldRecord;
                    });

            if (record.CasEntries.Determinism.IsSinglePhaseNonDeterministic != hashes.Determinism.IsSinglePhaseNonDeterministic)
            {
                return new SinglePhaseMixingFailure(CacheId);
            }

            AddSessionRecord(record);
            if (record.CasEntries.Equals(hashes))
            {
                record = null;
            }
            else
            {
                // Check if tool determinism did not make it in - that means a very bad thing happened
                if (hashes.Determinism.IsDeterministicTool)
                {
                    return new NotDeterministicFailure(Cache.CacheId, record, new FullCacheRecord(record.StrongFingerprint, hashes));
                }
            }

            if (record == null)
            {
                return new FullCacheRecordWithDeterminism(hashes.GetFinalDeterminism(Cache.IsAuthoritative, Cache.CacheGuid, CacheDeterminism.NeverExpires));
            }
            else
            {
                return new FullCacheRecordWithDeterminism(new FullCacheRecord(record.StrongFingerprint, record.CasEntries.GetModifiedCasEntriesWithDeterminism(Cache.IsAuthoritative, Cache.CacheGuid, CacheDeterminism.NeverExpires)));
            }
        }

        #region ICacheReadOnlySession methods

        public string CacheId => Cache.CacheId;

        public string CacheSessionId => m_sessionId;

        public bool IsClosed => m_closed;

        public bool StrictMetadataCasCoupling => Cache.StrictMetadataCasCoupling;

        public async Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            if (m_closed)
            {
                // Already closed or no entries - Close can be called again without problem
                return m_sessionId;
            }

            m_closed = true;

            if (SessionEntries == null)
            {
                return m_sessionId;
            }

            // We abandon the session if it is empty
            if (SessionEntries.Count == 0)
            {
                return await Cache.CloseSessionAsync(m_sessionId);
            }

            return await Cache.CloseSessionAsync(m_sessionId, SessionEntries.Keys);
        }

        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            ConcurrentDictionary<StrongFingerprint, FullCacheRecord> strongFingerprints;
            if (Cache.Fingerprints.TryGetValue(weak, out strongFingerprints))
            {
                foreach (StrongFingerprint strongFingerprint in strongFingerprints.Keys)
                {
                    yield return Task.FromResult(new Possible<StrongFingerprint, Failure>(strongFingerprint));
                }
            }
        }

        public Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            return Task.Run(() =>
            {
                ConcurrentDictionary<StrongFingerprint, FullCacheRecord> strongFingerprints;
                if (Cache.Fingerprints.TryGetValue(strong.WeakFingerprint, out strongFingerprints))
                {
                    FullCacheRecord cacheRecord;
                    if (strongFingerprints.TryGetValue(strong, out cacheRecord))
                    {
                        // Belongs in this session
                        AddSessionRecord(cacheRecord);

                        if (Cache.IsAuthoritative)
                        {
                            return new Possible<CasEntries, Failure>(cacheRecord.CasEntries.GetModifiedCasEntriesWithDeterminism(Cache.IsAuthoritative, Cache.CacheGuid, CacheDeterminism.NeverExpires));
                        }
                        else
                        {
                            return new Possible<CasEntries, Failure>(cacheRecord.CasEntries);
                        }
                    }
                }

                return new NoMatchingFingerprintFailure(strong);
            });
        }

        public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return Task.Run(() =>
            {
                if (!m_pinnedToCas.ContainsKey(hash))
                {
                    if (!Cache.CasStorage.ContainsKey(hash))
                    {
                        return new NoCasEntryFailure(Cache.CacheId, hash);
                    }

                    m_pinnedToCas.TryAdd(hash, 0);
                }

                return new Possible<string, Failure>(Cache.CacheId);
            });
        }

        public async Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries casEntries, UrgencyHint urgencyHint, Guid activityId)
        {
            Possible<string, Failure>[] retValues = new Possible<string, Failure>[casEntries.Count];

            for (int i = 0; i < casEntries.Count; i++)
            {
                retValues[i] = await PinToCasAsync(casEntries[i], urgencyHint, activityId);
            }

            return retValues;
        }

        public async Task<Possible<string, Failure>> ProduceFileAsync(
            CasHash hash,
            string filename,
            FileState fileState,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            Possible<Stream, Failure> casStream = await GetStreamAsync(hash, urgencyHint, activityId);

            if (!casStream.Succeeded)
            {
                return casStream.Failure;
            }

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(filename));
                using (FileStream fs = new FileStream(filename, FileMode.CreateNew, FileAccess.Write))
                {
                    await casStream.Result.CopyToAsync(fs);
                }
            }
            catch (Exception e)
            {
                return new ProduceFileFailure(CacheId, hash, filename, e);
            }

            return filename;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return Task.Run(() =>
            {
                if (!m_pinnedToCas.ContainsKey(hash))
                {
                    return new UnpinnedCasEntryFailure(CacheId, hash);
                }

                byte[] fileBytes;

                if (!Cache.CasStorage.TryGetValue(hash, out fileBytes))
                {
                    return new NoCasEntryFailure(Cache.CacheId, hash);
                }

                return new Possible<Stream, Failure>(new MemoryStream(fileBytes));
            });
        }

        public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId)
        {
            return Task.FromResult(new Possible<CacheSessionStatistics[], Failure>(new CacheSessionStatistics[0]));
        }

        // Note that this cache is able to remediate even in read-only sessions...
        public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return Task.Run<Possible<ValidateContentStatus, Failure>>(() =>
            {
                if (CasHash.NoItem.Equals(hash))
                {
                    return ValidateContentStatus.Ok;
                }

                byte[] fileBytes;

                if (!Cache.CasStorage.TryGetValue(hash, out fileBytes))
                {
                    return ValidateContentStatus.Remediated;
                }

                CasHash contentHash = new CasHash(ContentHashingUtilities.HashBytes(fileBytes).ToHashByteArray());

                if (contentHash == hash)
                {
                    return ValidateContentStatus.Ok;
                }

                // It is a corrupted data entry - remove it
                Cache.CasStorage.TryRemove(hash, out fileBytes);

                // Also remove it from the set of pinned content (if it was there)
                int junk;
                m_pinnedToCas.TryRemove(hash, out junk);

                return ValidateContentStatus.Remediated;
            });
        }

        #endregion ICacheReadOnlySession methods

        #region ICacheSession methods

        public Task<Possible<CasHash, Failure>> AddToCasAsync(Stream inputStream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(inputStream != null);
            Contract.Assert(!m_readOnly);

            return Task.Run<Possible<CasHash, Failure>>(() =>
            {
                using (var memoryStream = new MemoryStream())
                {
                    // TODO: Use CloudStore HashingStream when it can work with MemoryStream destination.
                    inputStream.CopyTo(memoryStream);
                    byte[] buffer = memoryStream.ToArray();

                    ContentHash contentHash = ContentHashingUtilities.HashBytes(buffer);
                    CasHash casHash = new CasHash(contentHash.ToHashByteArray());
                    Cache.CasStorage.TryAdd(casHash, buffer);
                    m_pinnedToCas.TryAdd(casHash, 0);

                    return casHash;
                }
            });
        }

        public async Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filename != null);

            Contract.Assert(!m_readOnly);

            using (var fileData = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                return await AddToCasAsync(fileData, null, urgencyHint, activityId);
            }
        }

        public Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, BuildXL.Cache.Interfaces.Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(hashes.IsValid);
            Contract.Assert(!m_readOnly);

            return Task.Run(() => AddOrGet(weak, casElement, hashElement, hashes));
        }

        public async Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(strongFingerprints != null);
            Contract.Assert(!m_readOnly);

            int count = 0;

            if (SessionEntries != null)
            {
                foreach (var strongTask in strongFingerprints)
                {
                    StrongFingerprint strong = await strongTask;

                    if (!SessionEntries.ContainsKey(strong))
                    {
                        if (Cache.StrictMetadataCasCoupling)
                        {
                            // Validate that we have the strong fingerprint to cas mapping.
                            ConcurrentDictionary<StrongFingerprint, FullCacheRecord> strongPrints;
                            if (!Cache.Fingerprints.TryGetValue(strong.WeakFingerprint, out strongPrints))
                            {
                                // Weak fingerprint not found
                                return new NoMatchingFingerprintFailure(strong);
                            }

                            if (!strongPrints.ContainsKey(strong))
                            {
                                // Strong fingerprint not found
                                return new NoMatchingFingerprintFailure(strong);
                            }
                        }

                        // Add it to our session since we don't have it already
                        SessionEntries.TryAdd(new StrongFingerprint(strong.WeakFingerprint, strong.CasElement, strong.HashElement, CacheId), 1);
                        count++;
                    }
                }
            }

            return count;
        }

        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            Contract.Requires(IsClosed);
            Contract.Assert(!m_readOnly);

            if (SessionEntries != null)
            {
                foreach (StrongFingerprint strong in SessionEntries.Keys)
                {
                    yield return Task.FromResult(strong);
                }
            }
        }

        #endregion ICacheSession methods
    }
}
