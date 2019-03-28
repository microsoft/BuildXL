// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.InputListFilter
{
    /// <summary>
    /// A cache that can do some filtering of observed inputs to prevent certain fingerprints
    /// from being added to the cache
    /// </summary>
    /// <remarks>
    /// This cache is mainly to help hunt down a problem we are seeing currently but the overall
    /// design allows for some extra, build configuration controlled, rules to be applied to items
    /// that can be cached based on observed inputs.
    /// </remarks>
    internal sealed class InputListFilterCache : ICache
    {
        private readonly ICache m_cache;

        // The regex that must match at least one line in the input list
        // Note that this may be null, in which case there is nothing to do.
        internal readonly Regex MustIncludeRegex;

        // The regex that must not match any line in the input list
        // Note that this may be null, in which case there is nothing to do.
        internal readonly Regex MustNotIncludeRegex;

        internal InputListFilterCache(ICache cache, Regex mustInclude, Regex mustNotInclude)
        {
            m_cache = cache;

            MustIncludeRegex = mustInclude;
            MustNotIncludeRegex = mustNotInclude;
        }

        #region ICache interface methods

        public string CacheId => m_cache.CacheId;

        public Guid CacheGuid => m_cache.CacheGuid;

        public bool StrictMetadataCasCoupling => m_cache.StrictMetadataCasCoupling;

        public bool IsShutdown => m_cache.IsShutdown;

        public bool IsReadOnly => m_cache.IsReadOnly;

        public bool IsDisconnected => m_cache.IsDisconnected;

        public async Task<Possible<ICacheReadOnlySession, Failure>> CreateReadOnlySessionAsync()
        {
            var maybeSession = await m_cache.CreateReadOnlySessionAsync();
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            return new InputListFilterReadOnlyCacheSession(maybeSession.Result, this);
        }

        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync()
        {
            var maybeSession = await m_cache.CreateSessionAsync();
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            return new InputListFilterCacheSession(maybeSession.Result, this);
        }

        public async Task<Possible<ICacheSession, Failure>> CreateSessionAsync(string sessionId)
        {
            var maybeSession = await m_cache.CreateSessionAsync(sessionId);
            if (!maybeSession.Succeeded)
            {
                return maybeSession;
            }

            return new InputListFilterCacheSession(maybeSession.Result, this);
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

        /// <inheritdoc/>
        public void SuscribeForCacheStateDegredationFailures(Action<Failure> notificationCallback)
        {
            m_cache.SuscribeForCacheStateDegredationFailures(notificationCallback);
        }
        #endregion ICache interface methods
    }
}
