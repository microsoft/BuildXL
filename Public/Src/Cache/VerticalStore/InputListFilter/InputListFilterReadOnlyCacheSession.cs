// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.InputListFilter
{
    internal class InputListFilterReadOnlyCacheSession : ICacheReadOnlySession
    {
        private readonly ICacheReadOnlySession m_session;
        protected readonly InputListFilterCache Cache;

        internal InputListFilterReadOnlyCacheSession(ICacheReadOnlySession session, InputListFilterCache cache)
        {
            Cache = cache;
            m_session = session;
        }

        public string CacheId => m_session.CacheId;

        public string CacheSessionId => m_session.CacheSessionId;

        public bool IsClosed => m_session.IsClosed;

        public bool StrictMetadataCasCoupling => m_session.StrictMetadataCasCoupling;

        public Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            return m_session.CloseAsync(activityId);
        }

        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.EnumerateStrongFingerprints(weak, urgencyHint, activityId);
        }

        public Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.GetCacheEntryAsync(strong, urgencyHint, activityId);
        }

        public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId)
        {
            return m_session.GetStatisticsAsync(activityId);
        }

        public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.ValidateContentAsync(hash, urgencyHint, activityId);
        }

        public Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.GetStreamAsync(hash, urgencyHint, activityId);
        }

        public Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.PinToCasAsync(hashes, urgencyHint, activityId);
        }

        public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.PinToCasAsync(hash, urgencyHint, activityId);
        }

        public Task<Possible<string, Failure>> ProduceFileAsync(CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.ProduceFileAsync(hash, filename, fileState, urgencyHint, activityId);
        }
    }
}
