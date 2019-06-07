// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// One-level cache on top of an arbitrary MemoizationStore (CS2) ICache
    /// </summary>
    /// <remarks>
    /// APIs around tracking named sessions are not implemented
    /// </remarks>
    public sealed class MemoizationStoreAdapterCache : ICache, IDisposable
    {
        private readonly BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache m_cache;
        private readonly ILogger m_logger;
        private readonly IAbsFileSystem m_fileSystem;
        private readonly AbsolutePath m_statsFile;
        private bool m_isShutdown;
        private readonly bool m_replaceExistingOnPlaceFile;

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="cacheId">Telemetry ID for the cache.</param>
        /// <param name="innerCache">A CS2 ICache for which this layer will translate.</param>
        /// <param name="logger">For logging diagnostics.</param>
        /// <param name="statsFile">A file to write stats about the cache into.</param>
        /// <param name="replaceExistingOnPlaceFile">When true, replace existing file when placing file.</param>
        public MemoizationStoreAdapterCache(
            string cacheId,
            BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache innerCache,
            ILogger logger,
            AbsolutePath statsFile,
            bool replaceExistingOnPlaceFile = false)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(innerCache != null);
            Contract.Requires(logger != null);

            CacheId = cacheId;
            m_cache = innerCache;
            m_logger = logger;
            m_statsFile = statsFile;
            m_fileSystem = new PassThroughFileSystem(m_logger);
            m_replaceExistingOnPlaceFile = replaceExistingOnPlaceFile;
        }

        /// <summary>
        /// Hook for allowing the factory to start up the cache
        /// </summary>
        /// <returns>Whether startup was successful or not</returns>
        public async Task<Possible<bool, Failure>> StartupAsync()
        {
            var startupResult = await m_cache.StartupAsync(new Context(m_logger));

            if (startupResult.Succeeded)
            {
                return true;
            }

            return new CacheFailure(startupResult.ErrorMessage);
        }

        /// <inheritdoc />
        public Guid CacheGuid => m_cache.Id;

        /// <inheritdoc />
        public bool IsShutdown => m_isShutdown;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public bool IsDisconnected => false;

        /// <inheritdoc />
        public async Task<Possible<string, Failure>> ShutdownAsync()
        {
            Contract.Requires(!IsShutdown);

            m_isShutdown = true;

            try
            {
                try
                {
                    GetStatsResult stats = await m_cache.GetStatsAsync(new Context(m_logger));
                    if (stats.Succeeded)
                    {
                        using (Stream fileStream = await m_fileSystem.OpenAsync(m_statsFile,  FileAccess.ReadWrite, FileMode.CreateNew, FileShare.None))
                        {
                            using (StreamWriter sw = new StreamWriter(fileStream))
                            {
                                foreach (KeyValuePair<string, long> stat in stats.CounterSet.ToDictionaryIntegral())
                                {
                                    await sw.WriteLineAsync($"{stat.Key}={stat.Value}");
                                }
                            }
                        }
                    }
                    else
                    {
                        m_logger.Debug($"Stats call failed {stats.ErrorMessage}");
                    }
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                BoolResult shutdownResult = await m_cache.ShutdownAsync(new Context(m_logger));

                if (shutdownResult.Succeeded)
                {
                    return CacheId;
                }

                return new CacheFailure(shutdownResult.ErrorMessage);
            }
            finally
            {
                Dispose(); 
            }
        }

        /// <inheritdoc />
        public string CacheId { get; }

        /// <inheritdoc />
        public bool StrictMetadataCasCoupling => false;

        /// <inheritdoc />
        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            var context = new Context(m_logger);
            var createSessionResult = m_cache.CreateSession(
                context,
                $"{CacheId}-{sessionId}",
                ImplicitPin.PutAndGet);
            if (createSessionResult.Succeeded)
            {
                var innerCacheSession = createSessionResult.Session;
                var startupResult = await innerCacheSession.StartupAsync(context);
                if (startupResult.Succeeded)
                {
                    return new MemoizationStoreAdapterCacheCacheSession(innerCacheSession, m_cache, CacheId, m_logger, sessionId, m_replaceExistingOnPlaceFile);
                }
                else
                {
                    return new CacheFailure(startupResult.ErrorMessage);
                }
            }
            else
            {
                return new CacheFailure(createSessionResult.ErrorMessage);
            }
        }

        /// <inheritdoc />
        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!IsReadOnly);

            var context = new Context(m_logger);
            var createSessionResult = m_cache.CreateSession(context, $"{CacheId}-Anonymous", ImplicitPin.PutAndGet);
            if (createSessionResult.Succeeded)
            {
                var innerCacheSession = createSessionResult.Session;
                var startupResult = await innerCacheSession.StartupAsync(context);
                if (startupResult.Succeeded)
                {
                    return new MemoizationStoreAdapterCacheCacheSession(innerCacheSession, m_cache, CacheId, m_logger, null, m_replaceExistingOnPlaceFile);
                }
                else
                {
                    return new CacheFailure(startupResult.ErrorMessage);
                }
            }
            else
            {
                return new CacheFailure(createSessionResult.ErrorMessage);
            }
        }

        /// <inheritdoc />
        public async Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            Contract.Requires(!IsShutdown);

            var context = new Context(m_logger);
            var createSessionResult = m_cache.CreateReadOnlySession(
                context,
                $"{CacheId}-Anonymous",
                ImplicitPin.PutAndGet);
            if (createSessionResult.Succeeded)
            {
                var innerCacheSession = createSessionResult.Session;
                var startupResult = await innerCacheSession.StartupAsync(context);
                if (startupResult.Succeeded)
                {
                    return new MemoizationStoreAdapterCacheReadOnlySession(innerCacheSession, m_cache, CacheId, m_logger, null, m_replaceExistingOnPlaceFile);
                }
                else
                {
                    return new CacheFailure(startupResult.ErrorMessage);
                }
            }
            else
            {
                return new CacheFailure(createSessionResult.ErrorMessage);
            }
        }

        internal const string DummySessionName = "MemoizationStoreAdapterDummySession";

        /// <inheritdoc />
        public IEnumerable<Task<string>> EnumerateCompletedSessions()
        {
            Contract.Requires(!IsShutdown);

            return new[] { Task.FromResult(DummySessionName) };
        }

        /// <inheritdoc />
        public Possible<IEnumerable<Task<StrongFingerprint>>, Failure> EnumerateSessionStrongFingerprints(string sessionId)
        {
            Contract.Requires(!IsShutdown);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));

            if (sessionId != DummySessionName)
            {
                return new UnknownSessionFailure(CacheId, sessionId);
            }

            var context = new Context(m_logger);
            return new Possible<IEnumerable<Task<StrongFingerprint>>, Failure>(
                m_cache.EnumerateStrongFingerprints(context)
                    .Where(result => result.Succeeded)
                    .Select(result => Task.FromResult(result.Data.FromMemoization(CacheId)))
                    .ToEnumerable());
        }

        private bool m_disposed;

        private void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    m_cache.Dispose();
                    m_logger.Dispose();
                    m_fileSystem.Dispose();
                }

                m_disposed = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            Contract.Requires(!IsShutdown);

            // No messages to return.
        }
    }
}
