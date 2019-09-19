// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// This is the interface to a "Cache" from which a cache session
    /// is aquired.  It also has mechanisms to enumerate prior sessions
    /// and provide information on prior sessions.
    /// </summary>
    public interface ICache
    {
        /// <summary>
        /// This gets the CacheId for this cache
        /// </summary>
        /// <returns>
        /// The identifier for this cache - used for telemetry
        /// </returns>
        [JetBrains.Annotations.NotNull]
        string CacheId { get; }

        /// <summary>
        /// This is the unique GUID for the given cache
        /// prestent store.
        /// </summary>
        /// <remarks>
        /// It will be used also for storing
        /// who provided the ByCache determinism for data.
        ///
        /// Why is this not the same as CacheId?  Why have
        /// CacheId?  CacheId is some user/human understandable
        /// name for a cache - maybe just "local" or "remote"
        /// or "WindowsL2" or whatever name the user has.
        /// However, the CacheGuid is unique for every persisted
        /// data store.  Multiple different "local" caches
        /// are different and would be different in the Guid.
        /// This is ment to uniquly identify a persisted store.
        ///
        /// Aggregators either have their own unique Guid or
        /// use the Guid of the cache that is providing the
        /// determinism (as the lower level caches are just
        /// projections over that)
        /// </remarks>
        Guid CacheGuid { get; }

        /// <summary>
        /// Returns true if shutdown has been called on this
        /// cache instance.
        /// </summary>
        bool IsShutdown { get; }

        /// <summary>
        /// Returns true if this cache can only open read-only sessions
        /// </summary>
        bool IsReadOnly { get; }

        /// <summary>
        /// Returns true if this cache is configured for strict metadata to CAS coupling
        /// </summary>
        /// <remarks>
        /// A cache that has strict metadata to CAS coupling means that adding a
        /// metadata record requires that the CAS content referenced in that metadata
        /// record is also available via that cache.  This requires that the CAS
        /// content be made available before the metadata that references it is
        /// added via the AddOrGet() operation.
        /// Not having this strict coupling is usually only useful for caches that
        /// are operating as a lower-level cache with another higher level providing
        /// the strong coupling required.
        /// </remarks>
        bool StrictMetadataCasCoupling { get; }

        /// <summary>
        /// Shutdown this cache instance
        /// </summary>
        /// <returns>
        /// Returns the cache ID that was shut down or any
        /// potential failure during shutdown.
        /// </returns>
        /// <remarks>
        /// Shutdown allows the cache to clean up state.  However, it
        /// may not always be called due to process termination.
        /// Shutdown will fail if any named sessions are still open.
        /// Shutdown may not be called more than once.
        /// </remarks>
        Task<Possible<string, Failure>> ShutdownAsync();

        /// <summary>
        /// Create a new session with the given name
        /// </summary>
        /// <param name="sessionId">Unique name for this cache session</param>
        /// <returns>The ICacheSession</returns>
        /// <remarks>
        /// This will fail if the session name is not unique.
        /// The scope of uniqueness refers to both inprogress sessions and sessions
        /// that have been completed relative to the backing store for this cache.
        /// </remarks>
        Task<Possible<ICacheSession, Failure>> CreateSessionAsync([JetBrains.Annotations.NotNull]string sessionId);

        /// <summary>
        /// Create a session without a session name - thus no
        /// session tracking
        /// </summary>
        /// <returns>The ICacheSession</returns>
        /// <remarks>
        /// This session will not track the cache artifacts
        /// for the session and thus will not add to the cache
        /// session tracking history.
        /// </remarks>
        Task<Possible<ICacheSession, Failure>> CreateSessionAsync();

        /// <summary>
        /// A read-only session is created without name as there
        /// is no metadata that can be contributed to that cache.
        /// </summary>
        /// <returns>
        /// A readonly cache session
        /// </returns>
        /// <remarks>
        /// The session will not have a session record.
        /// </remarks>
        Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync();

        /// <summary>
        /// Returns the set of completed sessions known by this cache
        /// </summary>
        /// <returns>
        /// List of session IDs that are known by this cache.
        /// </returns>
        IEnumerable<Task<string>> EnumerateCompletedSessions();

        /// <summary>
        /// Returns the set of strong fingerprints that belong to this cache
        /// session.
        /// </summary>
        /// <param name="sessionId">The session ID to get</param>
        /// <returns>
        /// The enumeration of strong fingerprints that were consumed and/or
        /// produced in the build.  An error may be returned if the session
        /// did not exist.
        /// </returns>
        Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId);

        /// <summary>
        /// Returns true if the cache has been disconnected
        /// </summary>
        bool IsDisconnected { get; }

        /// <summary>
        /// Allows a cache consumer to suscribe to recieve notifications for errors that have
        /// changed the operating state of the cache.
        /// </summary>
        /// <remarks>
        /// Some ICache implementations can have errors occur and begin to operate in a reduced cacpacity
        /// mode instead of failing requests. Examples include the VerticalAggregator failing to contact
        /// its remote cache on construction and using only the local cache.
        ///
        /// These errors may be presented to the end user as a warning instead of a failure.
        ///
        /// Don't block / throw from the callback.
        /// </remarks>
        /// <param name="notificationCallback">Lamda expression to process the error.</param>
        void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback);
    }
}
