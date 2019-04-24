// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.InMemory
{
    /// <summary>
    /// A generic hash
    /// </summary>
    internal sealed class MemCache : ICache
    {
        private readonly string m_cacheId;

        private readonly Guid m_cacheGuid;

        private readonly bool m_strictMetadataCasCoupling;

        private bool m_shutdown = false;

        internal readonly ConcurrentDictionary<CasHash, byte[]> CasStorage = new ConcurrentDictionary<CasHash, byte[]>();

        internal readonly ConcurrentDictionary<WeakFingerprintHash, ConcurrentDictionary<StrongFingerprint, FullCacheRecord>> Fingerprints = new ConcurrentDictionary<WeakFingerprintHash, ConcurrentDictionary<StrongFingerprint, FullCacheRecord>>();

        // Store the recorded sessions here for later enumeration
        private readonly ConcurrentDictionary<string, List<StrongFingerprint>> m_sessionRecords = new ConcurrentDictionary<string, List<StrongFingerprint>>();

        // Store the in-flight sessions to prevent duplicates
        private readonly ConcurrentDictionary<string, int> m_openSessions = new ConcurrentDictionary<string, int>();

        // Helper method that enumerates into tasks since everything we have is local
        private static IEnumerable<Task<T>> EnumerateIntoTasks<T>(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                yield return Task.FromResult(item);
            }
        }

        internal MemCache(string cacheId, bool strictMetadataCasCoupling, bool isauthoritative)
        {
            m_cacheGuid = CacheDeterminism.NewCacheGuid();
            m_cacheId = cacheId;
            IsAuthoritative = isauthoritative;
            m_strictMetadataCasCoupling = strictMetadataCasCoupling;
        }

        internal Task<string> CloseSessionAsync(string sessionId)
        {
            int junk;
            if (!m_openSessions.TryRemove(sessionId, out junk))
            {
                // Why did this fail?  Do we care?  It is a session that we did not open?
                // Very bad, indeed - internal implementation error
                Contract.Assert(false);
            }

            return Task.FromResult(sessionId);
        }

        internal async Task<Possible<string, Failure>> CloseSessionAsync(string sessionId, IEnumerable<StrongFingerprint> strongFingerprints)
        {
            List<StrongFingerprint> fingerprints = new List<StrongFingerprint>(strongFingerprints);

            if (!m_sessionRecords.TryAdd(sessionId, fingerprints))
            {
                // How did this happen?  We check that the cache ID unique before we
                // started.  Some implementation error here.
                Contract.Assert(false);
            }

            return await CloseSessionAsync(sessionId);
        }

        #region ICache interface methods

        public string CacheId => m_cacheId;

        public Guid CacheGuid => m_cacheGuid;

        public bool StrictMetadataCasCoupling => m_strictMetadataCasCoupling;

        public bool IsShutdown => m_shutdown;

        public bool IsReadOnly => false;

        public bool IsAuthoritative { get; }

        // A cache on its own is connected to its own storage
        public bool IsDisconnected => false;

        public Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            Contract.Requires(!IsShutdown);

            return Task.FromResult<Possible<ICacheReadOnlySession, Failure>>(new MemCacheSession(this, true));
        }

        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);

            return Task.FromResult<Possible<ICacheSession, Failure>>(new MemCacheSession(this, false));
        }

        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            if (m_sessionRecords.ContainsKey(sessionId))
            {
                return Task.FromResult<Possible<ICacheSession, Failure>>(new DuplicateSessionIdFailure(m_cacheId, sessionId));
            }

            // Add it to our inflight sessions
            if (!m_openSessions.TryAdd(sessionId, 0))
            {
                return Task.FromResult<Possible<ICacheSession, Failure>>(new DuplicateSessionIdFailure(CacheId, sessionId));
            }

            return Task.FromResult<Possible<ICacheSession, Failure>>(new MemCacheSession(this, sessionId));
        }

        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            Contract.Requires(!IsShutdown);

            return EnumerateIntoTasks(m_sessionRecords.Keys);
        }

        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            List<StrongFingerprint> strongFingerprints;
            if (m_sessionRecords.TryGetValue(sessionId, out strongFingerprints))
            {
                List<Task<StrongFingerprint>> listTaskStrongFingerprints = new List<Task<StrongFingerprint>>();
                foreach (var strongFingerprint in strongFingerprints)
                {
                    listTaskStrongFingerprints.Add(Task.FromResult(strongFingerprint));
                }

                return new Possible<IEnumerable<Task<StrongFingerprint>>, Failure>(listTaskStrongFingerprints);
            }

            return new UnknownSessionFailure(CacheId, sessionId);
        }

        public Task<Possible<string, Failure>> ShutdownAsync()
        {
            Contract.Requires(!IsShutdown);

            m_shutdown = true;

            if (m_openSessions.Count > 0)
            {
                return Task.FromResult(new Possible<string, Failure>(new ShutdownWithOpenSessionsFailure(CacheId, m_openSessions.Keys)));
            }

            return Task.FromResult(new Possible<string, Failure>(CacheId));
        }

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            Contract.Requires(!IsShutdown);

            // No messages to return.
        }

        #endregion ICache interface methods
    }
}
