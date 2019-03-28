// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces.Test
{
    public class CallbackCacheSessionWrapper : CallbackCacheReadOnlySessionWrapper, ICacheSession
    {
        private ICacheSession m_realSession;

        public CallbackCacheSessionWrapper(ICacheSession realSession)
            : base(realSession)
        {
            m_realSession = realSession;
        }

        /// <summary>
        /// The underlying session
        /// </summary>
        public new ICacheSession WrappedSession => m_realSession;

        public Func<WeakFingerprintHash, CasHash, Hash, CasEntries, UrgencyHint, Guid, ICacheSession, Task<Possible<FullCacheRecordWithDeterminism, Failure>>> AddOrGetAsyncCallback;

        public Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = AddOrGetAsyncCallback;
            if (callback != null)
            {
                return callback(weak, casElement, hashElement, hashes, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, activityId);
            }
        }

        public Func<Stream, CasHash?, UrgencyHint, Guid, ICacheSession, Task<Possible<CasHash, Failure>>> AddToCasAsyncCallback;

        public Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? contentHash, UrgencyHint urgencyHint, Guid activityId)
        {
            var callback = AddToCasAsyncCallback;
            if (callback != null)
            {
                return callback(filestream, contentHash, urgencyHint, activityId, m_realSession);
            }
            else
            {
                return m_realSession.AddToCasAsync(filestream, contentHash, urgencyHint, activityId);
            }
        }

        public Func<string,
                    FileState,
                    CasHash?,
                    UrgencyHint,
                    Guid,
                    ICacheSession,
                    Task<Possible<CasHash, Failure>>> AddToCasFilenameAsyncCallback;

        public Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            var callback = AddToCasFilenameAsyncCallback;

            if (callback != null)
            {
                return callback(
                    filename,
                    fileState,
                    hash,
                    urgencyHint,
                    activityId,
                    m_realSession);
            }
            else
            {
                return m_realSession.AddToCasAsync(
                    filename,
                    fileState,
                    hash,
                    urgencyHint,
                    activityId);
            }
        }

        public Func<Guid, ICacheSession, IEnumerable<Task<StrongFingerprint>>> EnumerateSessionFingerprintsCallback;

        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            var callback = EnumerateSessionFingerprintsCallback;
            if (callback != null)
            {
                return callback(activityId, m_realSession);
            }
            else
            {
                return m_realSession.EnumerateSessionFingerprints(activityId);
            }
        }

        public Func<IEnumerable<Task<StrongFingerprint>>, Guid, ICacheSession, Task<Possible<int, Failure>>> IncorporateRecordsAsyncCallback;

        public Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            var callback = IncorporateRecordsAsyncCallback;
                if (callback != null)
            {
                return callback(strongFingerprints, activityId, m_realSession);
            }
            else
            {
                return m_realSession.IncorporateRecordsAsync(strongFingerprints, activityId);
            }
        }
    }
}
