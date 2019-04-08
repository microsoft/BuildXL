// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Handle to a session.
    /// </summary>
    public class SessionHandle<TSession>
    {
        private readonly long _timeoutTicks;

        /// <nodoc />
        public string CacheName { get; }

        /// <nodoc />
        public string SessionName { get; }

        /// <nodoc />
        public TSession Session { get; }

        /// <nodoc />
        public ImplicitPin ImplicitPin { get; }

        /// <nodoc />
        public Capabilities SessionCapabilities { get; }

        /// <summary>
        /// Gets the session's expiration time, in ticks.
        /// </summary>
        public long SessionExpirationUtcTicks { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionHandle{T}"/> class.
        /// </summary>
        public SessionHandle(
            TSession session,
            string sessionName,
            string cacheName,
            ImplicitPin implicitPin,
            Capabilities capabilities,
            long sessionExpirationUtcTicks,
            TimeSpan timeout)
        {
            Session = session;
            SessionName = sessionName;
            CacheName = cacheName;
            ImplicitPin = implicitPin;
            SessionCapabilities = capabilities;
            SessionExpirationUtcTicks = sessionExpirationUtcTicks;
            _timeoutTicks = timeout.Ticks;
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
            return $"id=[{sessionId}] name=[{SessionName}] expiration=[{SessionExpirationUtcTicks}] capabilities=[{SessionCapabilities}]";
        }
    }
}
