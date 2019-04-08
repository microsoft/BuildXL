// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces.Test
{
    public class CallbackCacheReadOnlySessionWrapper : ICacheReadOnlySession
    {
        private readonly ICacheReadOnlySession m_realSession;

        public CallbackCacheReadOnlySessionWrapper(ICacheReadOnlySession realCache)
        {
            m_realSession = realCache;
        }

        /// <summary>
        /// The underlying session
        /// </summary>
        public ICacheReadOnlySession WrappedSession => m_realSession;

        public Func<ICacheReadOnlySession, string> CacheIdCallback;

        public string CacheId
        {
            get
            {
                var callback = CacheIdCallback;
                if (callback != null)
                {
                    return callback(m_realSession);
                }
                else
                {
                    return m_realSession.CacheId;
                }
            }
        }

        public Func<ICacheReadOnlySession, string> CacheSessionIdCallback;

        public string CacheSessionId
        {
            get
            {
                var callback = CacheSessionIdCallback;
                if (callback != null)
                {
                    return callback(m_realSession);
                }
                else
                {
                    return m_realSession.CacheSessionId;
                }
            }
        }

        public Func<ICacheReadOnlySession, bool> IsClosedCallback;

        public bool IsClosed
        {
            get
            {
                var callback = IsClosedCallback;
                if (callback != null)
                {
                    return callback(m_realSession);
                }
                else
                {
                    return m_realSession.IsClosed;
                }
            }
        }

        public Func<ICacheReadOnlySession, bool> StrictMetadataCasCouplingCallback;

        public bool StrictMetadataCasCoupling
        {
            get
            {
                var callback = StrictMetadataCasCouplingCallback;
                if (callback != null)
                {
                    return callback(m_realSession);
                }
                else
                {
                    return m_realSession.StrictMetadataCasCoupling;
                }
            }
        }

        public Func<Guid, ICacheReadOnlySession, Task<Possible<string, Failure>>> CloseAsyncCallback;

        public Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            var callback = CloseAsyncCallback;
            if (callback != null)
            {
                return callback(activityId, m_realSession);
            }
            else
            {
                return m_realSession.CloseAsync(activityId);
            }
        }

        public Func<WeakFingerprintHash, UrgencyHint, Guid, ICacheReadOnlySession, IEnumerable<Task<Possible<StrongFingerprint, Failure>>>> EnumerateStrongFingerprintsCallback;

        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = EnumerateStrongFingerprintsCallback;
            if (callback != null)
            {
                return callback(weak, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.EnumerateStrongFingerprints(weak, urgencyHint, activityId);
            }
        }

        public Func<StrongFingerprint, UrgencyHint, Guid, ICacheReadOnlySession, Task<Possible<CasEntries, Failure>>> GetCacheEntryAsyncCallback;

        public Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = GetCacheEntryAsyncCallback;
            if (callback != null)
            {
                return callback(strong, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.GetCacheEntryAsync(strong, urgencyHint, activityId);
            }
        }

        public Func<Guid, ICacheReadOnlySession, Task<Possible<CacheSessionStatistics[], Failure>>> GetStatisticsAsyncCallback;

        public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId = default(Guid))
        {
            var callback = GetStatisticsAsyncCallback;
            if (callback != null)
            {
                return callback(activityId, m_realSession);
            }
            else
            {
                return m_realSession.GetStatisticsAsync(activityId);
            }
        }

        public Func<CasHash, UrgencyHint, Guid, ICacheReadOnlySession, Task<Possible<Stream, Failure>>> GetStreamAsyncCallback;

        public Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = GetStreamAsyncCallback;
            if (callback != null)
            {
                return callback(hash, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.GetStreamAsync(hash, urgencyHint, activityId);
            }
        }

        public Func<CasEntries, UrgencyHint, Guid, ICacheReadOnlySession, Task<Possible<string, Failure>[]>> PinToCasMultipleAsyncCallback;

        public Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = PinToCasMultipleAsyncCallback;
            if (callback != null)
            {
                return callback(hashes, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.PinToCasAsync(hashes, urgencyHint, activityId);
            }
        }

        public Func<CasHash, UrgencyHint, Guid, ICacheReadOnlySession, Task<Possible<string, Failure>>> PinToCasAsyncCallback;

        public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = PinToCasAsyncCallback;
            if (callback != null)
            {
                return callback(hash, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.PinToCasAsync(hash, urgencyHint, activityId);
            }
        }

        public Func<CasHash, UrgencyHint, Guid, ICacheReadOnlySession, Task<Possible<ValidateContentStatus, Failure>>> ValidateContentAsyncCallback;

        public Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = ValidateContentAsyncCallback;
            if (callback != null)
            {
                return callback(hash, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.ValidateContentAsync(hash, urgencyHint, activityId);
            }
        }

        public Func<CasHash,
            string,
            FileState,
            UrgencyHint,
            Guid, ICacheReadOnlySession,
            Task<Possible<string, Failure>>> ProduceFileAsyncCallback;

        public Task<Possible<string, Failure>> ProduceFileAsync(
            CasHash hash,
            string filename,
            FileState fileState,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            var callback = ProduceFileAsyncCallback;
            if (callback != null)
            {
                return callback(
                    hash,
                    filename,
                    fileState,
                    urgencyHint,
                    activityId,
                    m_realSession);
            }
            else
            {
                return m_realSession.ProduceFileAsync(
                    hash,
                    filename,
                    fileState,
                    urgencyHint,
                    activityId);
            }
        }
    }
}
