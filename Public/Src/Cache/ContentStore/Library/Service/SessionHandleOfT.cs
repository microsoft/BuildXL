// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

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

    /// <nodoc />
    public interface ISessionHandle<out TSession, out TSessionData>
        where TSessionData : ISessionData
    {
        /// <nodoc />
        public string CacheName { get; }

        /// <nodoc />
        public TSession Session { get; }

        /// <nodoc />
        public TSessionData SessionData { get; }

        /// <summary>
        /// Gets the session's expiration time, in ticks.
        /// </summary>
        public long SessionExpirationUtcTicks { get; }

        /// <nodoc />
        public DateTime SessionExpirationDateTime { get; }

        /// <summary>
        /// Reset the timeout based on the current time.
        /// </summary>
        public void BumpExpiration();

        /// <summary>
        /// Gets a string representation of a session handle with a given session id.
        /// </summary>
        public string ToString(int sessionId);
    }

    /// <summary>
    /// Handle to a session.
    /// </summary>
    public class SessionHandle<TSession, TSessionData> : ISessionHandle<TSession, TSessionData>
        where TSessionData : ISessionData
    {
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

        /// <summary>
        /// Reset the timeout based on the current time.
        /// </summary>
        public void BumpExpiration()
        {
            SessionExpirationUtcTicks = DateTime.UtcNow.Ticks + _timeoutTicks;
        }

        /// <summary>
        /// Gets a string representation of a session handle with a given session id.
        /// </summary>
        public string ToString(int sessionId)
        {
            return $"id=[{sessionId}] name=[{SessionData.Name}] expiration=[{SessionExpirationDateTime}] capabilities=[{SessionData.Capabilities}]";
        }
    }
}
