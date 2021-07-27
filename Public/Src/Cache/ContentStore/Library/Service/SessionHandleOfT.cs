// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Service.Grpc;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <nodoc />
    public interface ISessionData
    {
        /// <nodoc />
        public string Name { get; }

        /// <nodoc />
        public Capabilities Capabilities { get; }
    }

    /// <summary>
    /// Tracks a session's lifetime.
    /// </summary>
    public interface ISessionLifetimeManager
    {
        /// <summary>
        /// Increments the usage count.
        /// </summary>
        public void IncrementUsageCount();

        /// <summary>
        /// Decrements the usage count.
        /// </summary>
        public void DecrementUsageCount();

        /// <summary>
        /// Gets the number of current usages for the current session.
        /// </summary>
        public int CurrentUsageCount { get; }

        /// <summary>
        /// Reset the timeout based on the current time.
        /// </summary>
        public void BumpExpiration();

        /// <summary>
        /// Gets the session's expiration time, in ticks.
        /// </summary>
        public long SessionExpirationUtcTicks { get; }

        /// <nodoc />
        public DateTime SessionExpirationDateTime { get; }
    }

    /// <nodoc />
    public interface ISessionHandle<out TSession, out TSessionData> : ISessionLifetimeManager where TSessionData : ISessionData
    {
        /// <nodoc />
        public string CacheName { get; }

        /// <nodoc />
        public TSession Session { get; }

        /// <nodoc />
        public TSessionData SessionData { get; }

        /// <summary>
        /// Gets a string representation of a session handle with a given session id.
        /// </summary>
        public string ToString(int sessionId);
    }

    /// <summary>
    /// Handle to a session.
    /// </summary>
    public class SessionHandle<TSession, TSessionData> : ISessionHandle<TSession, TSessionData> where TSessionData : ISessionData
    {
        private int _currentUsageCount;
        private readonly long _timeoutTicks;

        /// <nodoc />
        public string CacheName { get; }

        /// <nodoc />
        public TSession Session { get; }

        /// <nodoc />
        public TSessionData SessionData { get; }

        /// <summary>
        /// Gets the session's expiration time, in ticks.
        /// </summary>
        public long SessionExpirationUtcTicks { get; private set; }

        /// <nodoc />
        public DateTime SessionExpirationDateTime => new DateTime(SessionExpirationUtcTicks).ToLocalTime();

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionHandle{TSession, TSessionData}"/> class.
        /// </summary>
        public SessionHandle(
            TSession session,
            TSessionData sessionData,
            string cacheName,
            long sessionExpirationUtcTicks,
            TimeSpan timeout)
        {
            Session = session;
            CacheName = cacheName;
            SessionExpirationUtcTicks = sessionExpirationUtcTicks;
            _timeoutTicks = timeout.Ticks;
            SessionData = sessionData;
        }

        /// <inheritdoc />
        public void BumpExpiration()
        {
            SessionExpirationUtcTicks = DateTime.UtcNow.Ticks + _timeoutTicks;
        }

        /// <inheritdoc />
        public void IncrementUsageCount()
        {
            Interlocked.Increment(ref _currentUsageCount);
        }

        /// <inheritdoc />
        public void DecrementUsageCount()
        {
            Interlocked.Decrement(ref _currentUsageCount);
        }

        /// <inheritdoc />
        public int CurrentUsageCount => _currentUsageCount;

        /// <summary>
        /// Gets a string representation of a session handle with a given session id.
        /// </summary>
        public string ToString(int sessionId)
        {
            return $"{sessionId.AsTraceableSessionId()} Name=[{SessionData.Name}] Expiration=[{SessionExpirationDateTime}] Capabilities=[{SessionData.Capabilities}] UsageCount=[{CurrentUsageCount}]";
        }
    }
}
