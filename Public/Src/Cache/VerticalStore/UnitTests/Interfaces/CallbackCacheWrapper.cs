// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces.Test
{
    /// <summary>
    /// Cache that enables each call to an encapsulated cache to be replaced with a different implementation
    /// </summary>
    /// <remarks>
    /// Used for testing consumers of caches to allow easy access to error paths in the consumer.
    ///
    /// All session created from a wrapped cache are also wrapped to enable functionality mutation.
    /// </remarks>
    public class CallbackCacheWrapper : ICache
    {
        private readonly ICache m_realCache;

        public CallbackCacheWrapper(ICache realCache)
        {
            m_realCache = realCache;
        }

        /// <summary>
        /// The cache that is being wrapped.
        /// </summary>
        public ICache WrappedCache => m_realCache;

        public Func<ICache, Guid> CacheGuidGetCallback;

        public Guid CacheGuid
        {
            get
            {
                if (CacheGuidGetCallback != null)
                {
                    return CacheGuidGetCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.CacheGuid;
                }
            }
        }

        public Func<ICache, string> CacheIdGetCallback;

        public string CacheId
        {
            get
            {
                if (CacheIdGetCallback != null)
                {
                    return CacheIdGetCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.CacheId;
                }
            }
        }

        public Func<ICache, bool> IsReadOnlyCallback;

        public bool IsReadOnly
        {
            get
            {
                if (IsReadOnlyCallback != null)
                {
                    return IsReadOnlyCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.IsReadOnly;
                }
            }
        }

        public Func<ICache, bool> IsShutdownCallback;

        public bool IsShutdown
        {
            get
            {
                if (IsShutdownCallback != null)
                {
                    return IsShutdownCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.IsShutdown;
                }
            }
        }

        public Func<ICache, bool> StrickMetadataCasCouplingCallback;

        public bool StrictMetadataCasCoupling
        {
            get
            {
                if (IsReadOnlyCallback != null)
                {
                    return IsReadOnlyCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.StrictMetadataCasCoupling;
                }
            }
        }

        public Func<ICache, bool> IsDisconnectedCallback;

        public bool IsDisconnected
        {
            get
            {
                if (IsDisconnectedCallback != null)
                {
                    return IsDisconnectedCallback.Invoke(m_realCache);
                }
                else
                {
                    return m_realCache.IsDisconnected;
                }
            }
        }

        public Func<ICache, Task<Possible<ICacheReadOnlySession, Failure>>> CreateReadOnlySessionAsyncCallback;

        public async Task<Possible<CallbackCacheReadOnlySessionWrapper, Failure>> CreateReadOnlySessionAsync()
        {
            Possible<ICacheReadOnlySession, Failure> session;

            if (CreateReadOnlySessionAsyncCallback != null)
            {
                session = await CreateReadOnlySessionAsyncCallback.Invoke(m_realCache);
            }
            else
            {
                session = await m_realCache.CreateReadOnlySessionAsync();
            }

            if (!session.Succeeded)
            {
                return session.Failure;
            }

            if (session.Result is CallbackCacheReadOnlySessionWrapper)
            {
                return (CallbackCacheReadOnlySessionWrapper)session.Result;
            }
            else
            {
                return new CallbackCacheReadOnlySessionWrapper(session.Result);
            }
        }

        public Func<ICache, Task<Possible<ICacheSession, Failure>>> CreateSessionAsyncCallback;

        public async Task<Possible<CallbackCacheSessionWrapper, Failure>> CreateSessionAsync()
        {
            Possible<ICacheSession, Failure> session;

            if (CreateSessionAsyncCallback != null)
            {
                session = await CreateSessionAsyncCallback.Invoke(m_realCache);
            }
            else
            {
                session = await m_realCache.CreateSessionAsync();
            }

            if (!session.Succeeded)
            {
                return session.Failure;
            }

            if (session.Result is CallbackCacheSessionWrapper)
            {
                return (CallbackCacheSessionWrapper)session.Result;
            }
            else
            {
                return new CallbackCacheSessionWrapper(session.Result);
            }
        }

        public Func<string, ICache, Task<Possible<ICacheSession, Failure>>> CreateNamedSessionAsyncCallback;

        public async Task<Possible<CallbackCacheSessionWrapper, Failure>> CreateSessionAsync(string sessionId)
        {
            Possible<ICacheSession, Failure> session;

            if (CreateNamedSessionAsyncCallback != null)
            {
                session = await CreateNamedSessionAsyncCallback.Invoke(sessionId, m_realCache);
            }
            else
            {
                session = await m_realCache.CreateSessionAsync(sessionId);
            }

            if (!session.Succeeded)
            {
                return session.Failure;
            }

            if (session.Result is CallbackCacheSessionWrapper)
            {
                return (CallbackCacheSessionWrapper)session.Result;
            }
            else
            {
                return new CallbackCacheSessionWrapper(session.Result);
            }
        }

        public Func<ICache, IEnumerable<Task<string>>> EnumerateCompletedSessionsCallback;

        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            if (EnumerateCompletedSessionsCallback != null)
            {
                return EnumerateCompletedSessionsCallback.Invoke(m_realCache);
            }
            else
            {
                return m_realCache.EnumerateCompletedSessions();
            }
        }

        public Func<string, ICache, Possible<IEnumerable<Task<StrongFingerprint>>, Failure>> EnumerateSessionStrongFingerprintsCallback;

        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            if (EnumerateSessionStrongFingerprintsCallback != null)
            {
                return EnumerateSessionStrongFingerprintsCallback.Invoke(sessionId, m_realCache);
            }
            else
            {
                return m_realCache.EnumerateSessionStrongFingerprints(sessionId);
            }
        }

        public Func<ICache, Task<Possible<string, Failure>>> ShutdownAsyncCallback;

        public Task<Possible<string, Failure>> ShutdownAsync()
        {
            if (ShutdownAsyncCallback != null)
            {
                return ShutdownAsyncCallback.Invoke(m_realCache);
            }
            else
            {
                return m_realCache.ShutdownAsync();
            }
        }

        async Task<Possible<ICacheSession, Failure>> ICache.CreateSessionAsync(string sessionId)
        {
            var ret = await CreateSessionAsync(sessionId);
            if (ret.Succeeded)
            {
                return ret.Result;
            }
            else
            {
                return ret.Failure;
            }
        }

        async Task<Possible<ICacheSession, Failure>> ICache.CreateSessionAsync()
        {
            var ret = await CreateSessionAsync();
            if (ret.Succeeded)
            {
                return ret.Result;
            }
            else
            {
                return ret.Failure;
            }
        }

        async Task<Possible<ICacheReadOnlySession, Failure>> ICache.CreateReadOnlySessionAsync()
        {
            var ret = await CreateReadOnlySessionAsync();
            if (ret.Succeeded)
            {
                return ret.Result;
            }
            else
            {
                return ret.Failure;
            }
        }

        public Action<Action<Failure>, ICache> SuscribeForUserNotificationsCallback;

        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            if (SuscribeForUserNotificationsCallback != null)
            {
                SuscribeForUserNotificationsCallback(notificationCallback, m_realCache);
            }
            else
            {
                m_realCache.SuscribeForCacheStateDegredationFailures(notificationCallback);
            }
        }
    }
}
