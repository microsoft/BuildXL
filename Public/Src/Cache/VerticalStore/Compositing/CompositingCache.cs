// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.Compositing
{
    /// <summary>
    /// A cache that read metadata from one underlying cache and CAS data from a second.
    /// </summary>
    internal sealed class CompositingCache : ICache
    {
        private readonly string m_cacheId;

        private readonly bool m_strictMetadataCasCoupling;

        private readonly ICache m_metadataCache;
        private readonly ICache m_casCache;

        /// <summary>
        /// Our event source.
        /// </summary>
        public static readonly EventSource EventSource = 
#if NET_FRAMEWORK_451
            new EventSource();
#else
            new EventSource("CompositingCacheEvt", EventSourceSettings.EtwSelfDescribingEventFormat);
#endif

        internal CompositingCache(ICache metadatCache, ICache casCache, string cacheId, bool strictMetadataCasCoupling)
        {
            m_metadataCache = metadatCache;
            m_casCache = casCache;
            m_cacheId = cacheId;
            m_strictMetadataCasCoupling = strictMetadataCasCoupling;
        }

        #region ICache interface methods

        public string CacheId => m_cacheId;

        public Guid CacheGuid => m_metadataCache.CacheGuid;

        public bool StrictMetadataCasCoupling => m_strictMetadataCasCoupling;

        public bool IsShutdown => m_metadataCache.IsShutdown && m_casCache.IsShutdown;

        public bool IsReadOnly => m_metadataCache.IsReadOnly || m_casCache.IsReadOnly;

        public bool IsDisconnected => m_metadataCache.IsDisconnected || m_casCache.IsDisconnected;

        public async Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            var maybeSession = await m_metadataCache.CreateReadOnlySessionAsync();
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            var metadataSession = maybeSession.Result;

            maybeSession = await m_casCache.CreateReadOnlySessionAsync();
            if (!maybeSession.Succeeded)
            {
                Analysis.IgnoreResult(await metadataSession.CloseAsync(), justification: "Okay to ignore close status");
                return maybeSession;
            }

            var casSession = maybeSession.Result;

            return new CompositingReadOnlyCacheSession(metadataSession, casSession, this);
        }

        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            var maybeSession = await m_metadataCache.CreateSessionAsync();
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            var metadataSession = maybeSession.Result;

            maybeSession = await m_casCache.CreateSessionAsync();
            if (!maybeSession.Succeeded)
            {
                Analysis.IgnoreResult(await metadataSession.CloseAsync(), justification: "Okay to ignore close status");
                return maybeSession;
            }

            var casSession = maybeSession.Result;

            return new CompositingCacheSession(metadataSession, casSession, this);
        }

        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            var maybeSession = await m_metadataCache.CreateSessionAsync(sessionId);
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            var metadataSession = maybeSession.Result;

            maybeSession = await m_casCache.CreateSessionAsync();
            if (!maybeSession.Succeeded)
            {
                Analysis.IgnoreResult(await metadataSession.CloseAsync(), justification: "Okay to ignore close status");
                return maybeSession;
            }

            var casSession = maybeSession.Result;

            return new CompositingCacheSession(metadataSession, casSession, this);
        }

        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            return m_metadataCache.EnumerateCompletedSessions();
        }

        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            return m_metadataCache.EnumerateSessionStrongFingerprints(sessionId);
        }

        public async Task<Possible<string, Failure>> ShutdownAsync()
        {
            var metadata = m_metadataCache.ShutdownAsync();
            var cas = m_casCache.ShutdownAsync();

            var metadataPossible = await metadata;
            var casPossible = await cas;

            if (metadataPossible.Succeeded)
            {
                if (casPossible.Succeeded)
                {
                    return CacheId;
                }

                return casPossible.Failure;
            }

            if (casPossible.Succeeded)
            {
                return metadataPossible.Failure;
            }

            return new AggregateFailure(metadataPossible.Failure, casPossible.Failure);
        }

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            m_casCache.SuscribeForCacheStateDegredationFailures(notificationCallback);
            m_metadataCache.SuscribeForCacheStateDegredationFailures(notificationCallback);
        }

        #endregion ICache interface methods
    }
}
