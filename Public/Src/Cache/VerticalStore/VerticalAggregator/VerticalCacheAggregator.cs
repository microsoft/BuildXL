// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.VerticalAggregator
{
    /// <summary>
    /// A cache aggregator that stiches a local and remote cache together.
    /// </summary>
    /// <remarks>
    /// For this version, policy control is outside the scope of implementation. Rather an approach that validates the most likely policies can
    /// be implemented is taken, with the assumption that if we can implement the most likely policies, we can implement others and will be able to
    /// add control mechanisms for those policies at a later time.
    ///
    /// This cache assumes that the remote cache can be flaky and that the local cache is durable and remains connected. A future idea may be to support
    /// a local cache that is also unreliable. (such as if aggregators were to be stacked and the L1 and L3 were to be up, with a L2 that is down.)
    /// </remarks>
    internal sealed class VerticalCacheAggregator : ICache
    {
        private readonly ICache m_localCache;
        private readonly ICache m_remoteCache;
        private readonly string m_cacheId;
        private readonly bool m_remoteIsReadOnly;

        /// <summary>
        /// Determines if the remote content store is readonly.
        /// This is different than m_remoteIsReadOnly, as this applies to only the content part of cache, and not all of it.
        /// This is also different than WriteThroughCasData, as this also applies to the calls to the remote triggered by metadata, and not just direct external content calls.
        /// </summary>
        private readonly bool m_remoteContentIsReadOnly;

        /// <summary>
        /// Indicates that CAS data should write through to remote CAS
        /// </summary>
        public readonly bool WriteThroughCasData;

        /// <summary>
        /// Our event source.
        /// </summary>
        public static readonly EventSource EventSource =
#if NET_FRAMEWORK_451
            new EventSource();
#else
            new EventSource("VerticalCacheAggregatorEvt", EventSourceSettings.EtwSelfDescribingEventFormat);
#endif

        internal VerticalCacheAggregator(ICache localCache, ICache remoteCache, bool remoteIsReadOnly, bool writeThroughCasData, bool remoteContentIsReadOnly)
        {
            m_localCache = localCache;
            m_remoteCache = remoteCache;
            m_cacheId = localCache.CacheId + "_" + remoteCache.CacheId;
            m_remoteIsReadOnly = remoteIsReadOnly || remoteCache.IsReadOnly;
            m_remoteContentIsReadOnly = remoteContentIsReadOnly;
            WriteThroughCasData = writeThroughCasData && !remoteContentIsReadOnly;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The ID of a vertical aggregator is the concatenation of local and remote cache ID's
        /// </remarks>
        public string CacheId => m_cacheId;

        /// <inheritdoc/>
        public Guid CacheGuid => m_remoteCache.IsDisconnected ? m_localCache.CacheGuid : m_remoteCache.CacheGuid;

        /// <inheritdoc/>
        public bool StrictMetadataCasCoupling => m_remoteIsReadOnly ? LocalCache.StrictMetadataCasCoupling : RemoteCache.StrictMetadataCasCoupling;

        /// <inheritdoc/>
        // Vertical aggregators report the state of their local caches as
        // the aggregator is not written to defend against a disconnected local
        // cache and will always try to use it. This will enable a stacked set
        // of VerticalAggregators to disconnect if the L2 should vanish.
        public bool IsDisconnected => LocalCache.IsDisconnected;

        /// <summary>
        /// The backing cache that this aggregator considers local
        /// </summary>
        /// <remarks>
        /// This API is used by external tools to walk the caches
        /// in order to do things like find the right cache to GC
        /// or run statistical analysis on.
        /// See the GC PowerShell scripts for examples.
        /// </remarks>
        public ICache LocalCache => m_localCache;

        /// <summary>
        /// The backing cache that this aggregator considers remote
        /// </summary>
        /// <remarks>
        /// This API is used by external tools to walk the caches
        /// in order to do things like find the right cache to GC
        /// or run statistical analysis on.
        /// See the GC PowerShell scripts for examples.
        /// </remarks>
        public ICache RemoteCache => m_remoteCache;

        /// <summary>
        /// If either of my child caches are shut down, we are shut down.
        /// </summary>
        public bool IsShutdown => m_localCache.IsShutdown || m_remoteCache.IsShutdown;

        /// <summary>
        /// If the local cache is read-only then the whole aggregator would be
        /// </summary>
        /// <remarks>
        /// Currently, the vertical aggregator will not accept a read-only local cache
        /// </remarks>
        public bool IsReadOnly => m_localCache.IsReadOnly;

        /// <inheritdoc/>
        public async Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            var localSessionTask = m_localCache.CreateSessionAsync();
            var remoteSessionTask = m_remoteCache.CreateReadOnlySessionAsync();

            var localSession = await localSessionTask;
            if (localSession.Succeeded)
            {
                var remoteSession = await remoteSessionTask;
                if (remoteSession.Succeeded)
                {
                    return new VerticalCacheAggregatorSession(
                        this,
                        Guid.NewGuid().ToString(),
                        true,
                        localSession.Result,
                        null,
                        remoteSession.Result,
                        remoteIsReadOnly: true,
                        remoteContentIsReadOnly: true);
                }

                Analysis.IgnoreResult(await localSession.Result.CloseAsync(), justification: "Okay to ignore close result");
                return remoteSession.Failure;
            }
            else
            {
                var remoteSession = await remoteSessionTask;
                if (remoteSession.Succeeded)
                {
                    Analysis.IgnoreResult(remoteSession.Result.CloseAsync(), justification: "Okay to ignore close result");
                    return localSession.Failure;
                }

                return new AggregateFailure(localSession.Failure, remoteSession.Failure);
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            var localSessionTask = m_localCache.CreateSessionAsync();

            Task<Possible<ICacheReadOnlySession, Failure>> remoteROSessionTask;
            Task<Possible<ICacheSession, Failure>> remoteSessionTask;

            if (m_remoteIsReadOnly)
            {
                remoteSessionTask = Task.FromResult(new Possible<ICacheSession, Failure>((ICacheSession)null));
                remoteROSessionTask = m_remoteCache.CreateReadOnlySessionAsync();
            }
            else
            {
                remoteSessionTask = m_remoteCache.CreateSessionAsync();
                remoteROSessionTask = Task.FromResult((await remoteSessionTask).Then<ICacheReadOnlySession>((cacheSession) => { return cacheSession; }));
            }

            return await CreateSessionAsync(localSessionTask, remoteROSessionTask, remoteSessionTask, Guid.NewGuid().ToString());
        }

        /// <inheritdoc/>
        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            var localSessionTask = m_localCache.CreateSessionAsync(sessionId);

            Task<Possible<ICacheReadOnlySession, Failure>> remoteROSessionTask;
            Task<Possible<ICacheSession, Failure>> remoteSessionTask;

            if (m_remoteIsReadOnly)
            {
                remoteSessionTask = Task.FromResult(new Possible<ICacheSession, Failure>((ICacheSession)null));
                remoteROSessionTask = m_remoteCache.CreateReadOnlySessionAsync();
            }
            else
            {
                remoteSessionTask = m_remoteCache.CreateSessionAsync(sessionId);
                remoteROSessionTask = Task.FromResult((await remoteSessionTask).Then<ICacheReadOnlySession>((cacheSession) => { return cacheSession; }));
            }

            return await CreateSessionAsync(localSessionTask, remoteROSessionTask, remoteSessionTask, sessionId);
        }

        private async Task<Possible<ICacheSession, Failure>> CreateSessionAsync(
            Task<Possible<ICacheSession, Failure>> localSessionTask,
            Task<Possible<ICacheReadOnlySession, Failure>> remoteROSessionTask,
            Task<Possible<ICacheSession, Failure>> remoteSessionTask,
            string sessionId)
        {
            var localSession = await localSessionTask;
            var remoteSession = await remoteSessionTask;
            var remoteRoSession = await remoteROSessionTask;

            if (localSession.Succeeded &&
                remoteSession.Succeeded &&
                remoteRoSession.Succeeded)
            {
                return new VerticalCacheAggregatorSession(
                    this,
                    sessionId,
                    false,
                    localSession.Result,
                    remoteSession.Result,
                    remoteRoSession.Result,
                    m_remoteIsReadOnly,
                    m_remoteContentIsReadOnly);
            }

            List<Failure> failures = new List<Failure>();
            if (localSession.Succeeded)
            {
                Analysis.IgnoreResult(await localSession.Result.CloseAsync(), justification: "Okay to ignore close result");
            }
            else
            {
                failures.Add(localSession.Failure);
            }

            if (remoteRoSession.Succeeded)
            {
                Analysis.IgnoreResult(await remoteRoSession.Result.CloseAsync(), justification: "Okay to ignore close result");
            }
            else
            {
                failures.Add(remoteRoSession.Failure);
            }

            // We don't need to worry about the writeable remote session, it was either closed by the R/O session above,
            // or was null.
            Failure firstFailure = failures[0];

            foreach (Failure oneFailure in failures)
            {
                if (oneFailure.GetType() != firstFailure.GetType())
                {
                    return new AggregateFailure(failures.ToArray());
                }
            }

            // If all the failures are the same type, return the first one.
            return firstFailure;
        }

        /// <inheritdoc/>
        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            // Well this could be expensive.
            var localCacheSessionsTask = m_localCache.EnumerateCompletedSessions();
            var remoteCacheSessionsTask = m_remoteCache.EnumerateCompletedSessions();

            List<Task<string>> ret = new List<Task<string>>(localCacheSessionsTask);
            ret.AddRange(remoteCacheSessionsTask);

            return ret;
        }

        private static IEnumerable<T> JoinEnumeration<T>(IEnumerable<T> items1, IEnumerable<T> items2)
        {
            if (items1 != null)
            {
                foreach (T item in items1)
                {
                    yield return item;
                }
            }

            if (items2 != null)
            {
                foreach (T item in items2)
                {
                    yield return item;
                }
            }
        }

        /// <inheritdoc/>
        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            // TODO: Consider if we should use a hashset to ensure we don't return the same session twice. Today we can return the same
            // session from each backing store.

            // If we want a multi-cache aggregator class, we should likely declare an enumerable of all the caches and iterate that
            // in a base class. For now....
            var localStrongFingerprints = m_localCache.EnumerateSessionStrongFingerprints(sessionId);
            IEnumerable<Task<StrongFingerprint>> local = null;
            if (localStrongFingerprints.Succeeded)
            {
                local = localStrongFingerprints.Result;
            }

            var remoteStrongFingerprints = m_remoteCache.EnumerateSessionStrongFingerprints(sessionId);
            IEnumerable<Task<StrongFingerprint>> remote = null;
            if (remoteStrongFingerprints.Succeeded)
            {
                remote = remoteStrongFingerprints.Result;
            }

            if ((local == null) && (remote == null))
            {
                return localStrongFingerprints.Failure;
            }

            return new Possible<IEnumerable<Task<StrongFingerprint>>, Failure>(JoinEnumeration(local, remote));
        }

        /// <inheritdoc/>
        public async Task<Possible<string, Failure>> ShutdownAsync()
        {
            var local = m_localCache.ShutdownAsync();
            var remote = m_remoteCache.ShutdownAsync();

            var localPossible = await local;
            var remotePossible = await remote;

            if (localPossible.Succeeded)
            {
                if (remotePossible.Succeeded)
                {
                    return CacheId;
                }

                return remotePossible.Failure;
            }

            if (remotePossible.Succeeded)
            {
                return localPossible.Failure;
            }

            return new AggregateFailure(localPossible.Failure, remotePossible.Failure);
        }

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            m_localCache.SuscribeForCacheStateDegredationFailures(notificationCallback);
            m_remoteCache.SuscribeForCacheStateDegredationFailures(notificationCallback);
        }
    }
}
