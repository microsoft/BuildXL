// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.VerticalAggregator
{
    /// <summary>
    /// A wrapper cache that forwards error messages to consuemers who later call <c ref="SuscribeForCacheStateDegrationFailures">SuscribeForCacheStateDegrationFailures</c>
    /// </summary>
    internal sealed class MessageForwardingCache : ICache
    {
        private readonly IEnumerable<Failure> m_initialMessages;
        private readonly ICache m_cache;

        internal MessageForwardingCache(IEnumerable<Failure> initialMessages, ICache cache)
        {
            m_cache = cache;
            m_initialMessages = initialMessages;
        }

        public Guid CacheGuid
        {
            get
            {
                return m_cache.CacheGuid;
            }
        }

        public string CacheId
        {
            get
            {
                return m_cache.CacheId;
            }
        }

        public bool IsDisconnected
        {
            get
            {
                return m_cache.IsDisconnected;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return m_cache.IsReadOnly;
            }
        }

        public bool IsShutdown
        {
            get
            {
                return m_cache.IsShutdown;
            }
        }

        public bool StrictMetadataCasCoupling
        {
            get
            {
                return m_cache.StrictMetadataCasCoupling;
            }
        }

        public Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            return m_cache.CreateReadOnlySessionAsync();
        }

        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            return m_cache.CreateSessionAsync();
        }

        public Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            return m_cache.CreateSessionAsync(sessionId);
        }

        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            return m_cache.EnumerateCompletedSessions();
        }

        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            return m_cache.EnumerateSessionStrongFingerprints(sessionId);
        }

        public Task<Possible<string, Failure>> ShutdownAsync()
        {
            return m_cache.ShutdownAsync();
        }

        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            m_cache.SuscribeForCacheStateDegredationFailures(notificationCallback);

            foreach (Failure failure in m_initialMessages)
            {
                notificationCallback(failure);
            }
        }
    }
}
