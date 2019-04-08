// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.Compositing
{
    internal class CompositingReadOnlyCacheSession : ICacheReadOnlySession
    {
        private readonly ICacheReadOnlySession m_metadataSession;
        private readonly ICacheReadOnlySession m_casSession;
        protected readonly ICache Cache;

        // Cas entries that have been pinned
        protected readonly ConcurrentDictionary<CasHash, int> PinnedToCas;

        internal CompositingReadOnlyCacheSession(
            ICacheReadOnlySession metadataSession,
            ICacheReadOnlySession casSesssion,
            ICache cache)
        {
            Cache = cache;
            m_metadataSession = metadataSession;
            m_casSession = casSesssion;
            PinnedToCas = new ConcurrentDictionary<CasHash, int>();
            PinnedToCas.TryAdd(CasHash.NoItem, 0);
        }

        public string CacheId => Cache.CacheId;

        public string CacheSessionId => m_metadataSession.CacheSessionId;

        public bool IsClosed => m_metadataSession.IsClosed || m_casSession.IsClosed;

        public bool StrictMetadataCasCoupling => Cache.StrictMetadataCasCoupling;

        public async Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            var casClose = m_casSession.CloseAsync(activityId);
            var metadataClose = m_metadataSession.CloseAsync(activityId);

            var metadataPossible = await metadataClose;
            var casPossible = await casClose;

            if (casPossible.Succeeded)
            {
                return metadataPossible;
            }

            if (metadataPossible.Succeeded)
            {
                return casPossible;
            }

            return new AggregateFailure(metadataPossible.Failure, casPossible.Failure);
        }

        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new EnumerateStrongFingerprintsActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(weak, urgencyHint);

                var ret = m_metadataSession.EnumerateStrongFingerprints(weak, urgencyHint, eventing.Id);
                eventing.Stop();
                return ret;
            }
        }

        public async Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new GetCacheEntryActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(strong, urgencyHint);

                return eventing.Returns(await m_metadataSession.GetCacheEntryAsync(strong, urgencyHint, eventing.Id));
            }
        }

        public async Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId)
        {
            using (var eventing = new GetStatisticsActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start();

                List<CacheSessionStatistics> stats = new List<CacheSessionStatistics>();

                // TODO:  We should have our own to report out too...
                // Make sure to ETW them too (but only our own and only upon first generation)
                // and add them to the list of stats
                var maybeStats = await m_casSession.GetStatisticsAsync();
                if (!maybeStats.Succeeded)
                {
                    return eventing.StopFailure(maybeStats.Failure);
                }

                stats.AddRange(maybeStats.Result);

                maybeStats = await m_metadataSession.GetStatisticsAsync();
                if (!maybeStats.Succeeded)
                {
                    return eventing.StopFailure(maybeStats.Failure);
                }

                stats.AddRange(maybeStats.Result);

                eventing.Stop();
                return stats.ToArray();
            }
        }

        public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            // TODO:  Implement content validation/remediation
            return Task.FromResult(new Possible<ValidateContentStatus, Failure>(ValidateContentStatus.NotSupported));
        }

        public async Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new GetStreamActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(hash, urgencyHint);
                return eventing.Returns(await m_casSession.GetStreamAsync(hash, urgencyHint, eventing.Id));
            }
        }

        public async Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new PinToCasMultipleActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(hashes, urgencyHint);

                var results = await m_casSession.PinToCasAsync(hashes, urgencyHint, eventing.Id);

                for (int i = 0; i < results.Length; i++)
                {
                    if (results[i].Succeeded)
                    {
                        PinnedToCas.TryAdd(hashes[i], 0);
                    }
                }

                return eventing.Returns(results);
            }
        }

        public async Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new PinToCasActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(hash, urgencyHint);

                var result = await m_casSession.PinToCasAsync(hash, urgencyHint, eventing.Id);
                if (result.Succeeded)
                {
                    PinnedToCas.TryAdd(hash, 0);
                }

                return eventing.Returns(result);
            }
        }

        public async Task<Possible<string, Failure>> ProduceFileAsync(CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new ProduceFileActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(hash, filename, fileState, urgencyHint);
                return eventing.Returns(await m_casSession.ProduceFileAsync(hash, filename, fileState, urgencyHint, activityId));
            }
        }
    }
}
