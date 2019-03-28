// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Handle to a session.
    /// </summary>
    public class SessionHandle
    {
        private int _inUseCount;
        private readonly long _timeoutTicks;

        /// <summary>
        /// name of cache.
        /// </summary>
        public readonly string CacheName;

        /// <summary>
        /// Implicit pin
        /// </summary>
        public readonly ImplicitPin ImplicitPin;

        /// <summary>
        /// Session name
        /// </summary>
        public readonly string SessionName;

        /// <summary>
        /// Capabilities of the session
        /// </summary>
        public readonly Capabilities SessionCapabilities;

        /// <summary>
        /// The session.
        /// </summary>
        public IContentSession Session;

        /// <summary>
        /// Gets the session's expiration time, in ticks.
        /// </summary>
        public long SessionExpirationUtcTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionHandle"/> class.
        /// </summary>
        public SessionHandle(
            IContentSession session,
            string sessionName,
            Capabilities sessionCapabilities,
            ImplicitPin implicitPin,
            string cacheName,
            int inUseCount,
            long sessionExpirationUtcTicks,
            TimeSpan timeout
            )
        {
            Session = session;
            SessionName = sessionName;
            SessionCapabilities = sessionCapabilities;
            ImplicitPin = implicitPin;
            CacheName = cacheName;
            _inUseCount = inUseCount;
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

        /// <nodoc />
        public string ToString(int sessionId)
        {
            return $"id=[{sessionId}] name=[{SessionName}] expiration=[{SessionExpirationUtcTicks}] capabilities=[{SessionCapabilities}] inUseCount=[{_inUseCount}]";
        }

        /// <nodoc />
        public int IncrementInUseCount() => Interlocked.Increment(ref _inUseCount);

        /// <nodoc />
        public bool DecrementInUseCount() => Interlocked.Decrement(ref _inUseCount) <= 0;
    }
}
