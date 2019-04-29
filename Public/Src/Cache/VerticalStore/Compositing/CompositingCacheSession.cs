// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Cache.Compositing
{
    internal sealed class CompositingCacheSession : CompositingReadOnlyCacheSession, ICacheSession
    {
        private readonly ICacheSession m_metadataSession;
        private readonly ICacheSession m_casSession;

        internal CompositingCacheSession(
            ICacheSession metadataSession,
            ICacheSession casSesssion,
            ICache cache)
            : base(metadataSession, casSesssion, cache)
        {
            m_metadataSession = metadataSession;
            m_casSession = casSesssion;
        }

        public async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new AddOrGetActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(weak, casElement, hashElement, hashes, urgencyHint);

                // First, check if all the content has been pinned.
                if (Cache.StrictMetadataCasCoupling)
                {
                    List<CasHash> referencedHashes = new List<CasHash>(hashes);
                    referencedHashes.Add(casElement);

                    foreach (CasHash oneHash in referencedHashes)
                    {
                        if (!PinnedToCas.ContainsKey(oneHash))
                        {
                            return eventing.StopFailure(new UnpinnedCasEntryFailure(Cache.CacheId, oneHash));
                        }
                    }
                }

                // Then check the determinism bit...
                var testStrongFingerprint = new StrongFingerprint(weak, casElement, hashElement, Cache.CacheId);

                var existRecordCheck = await m_metadataSession.GetCacheEntryAsync(testStrongFingerprint, urgencyHint, eventing.Id);
                if (existRecordCheck.Succeeded)
                {
                    // There's an existing record, so we need the rules for determinism upgrade / downgrade. Which are complicated by the existance
                    // (or lack there of) of the associated content.

                    // First we'll check for determinism upgrades or other situations that don't require content probes.
                    var existingRecord = existRecordCheck.Result;

                    // If the determinsim is going from no determinism to some determinism OR the record is going from a non-tool
                    // determinism to a tool determinism, replace
                    if ((existingRecord.Determinism.IsNone && hashes.Determinism.IsDeterministic) ||
                        (hashes.Determinism.IsDeterministicTool && !existingRecord.Determinism.IsDeterministicTool))
                    {
                        return eventing.Returns(await m_metadataSession.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, eventing.Id));
                    }

                    // Before trying to probe for files, see if the entries are identical.
                    // If so, tell the caller we stored their record.
                    if (hashes == existingRecord && hashes.Determinism.EffectiveGuid == existingRecord.Determinism.EffectiveGuid)
                    {
                        return eventing.Returns(new FullCacheRecordWithDeterminism(CacheDeterminism.None));
                    }

                    // The rest of the determinism moves depend on if the files exist or not.
                    bool foundAllFiles = true;

                    var pinCheck = await m_casSession.PinToCasAsync(existRecordCheck.Result, urgencyHint, eventing.Id);

                    foreach (var oneOutput in pinCheck)
                    {
                        if (!oneOutput.Succeeded)
                        {
                            foundAllFiles = false;
                        }
                    }

                    // Ok, so now with the knowledge of file existance, if we're missing files, allow the call through no matter what.
                    if (!foundAllFiles)
                    {
                        return eventing.Returns(await m_metadataSession.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, eventing.Id));
                    }

                    // If the existing entry is more deterministic than what we're being given or
                    // the records are the same level, unless tool deterministic or single phase.
                    if ((existingRecord.Determinism.IsDeterministic && !hashes.Determinism.IsDeterministic) ||
                        (existingRecord.Determinism.IsDeterministicTool && !hashes.Determinism.IsDeterministicTool) ||
                        (existingRecord.Determinism.EffectiveGuid == hashes.Determinism.EffectiveGuid && !existingRecord.Determinism.IsDeterministicTool && !existingRecord.Determinism.IsSinglePhaseNonDeterministic))
                    {
                        // If the records are identical except for determinsim, return null.
                        if (existingRecord == hashes)
                        {
                            return eventing.Returns(new FullCacheRecordWithDeterminism(CacheDeterminism.None));
                        }

                        return eventing.Returns(new FullCacheRecordWithDeterminism(new FullCacheRecord(testStrongFingerprint, existingRecord)));
                    }

                    // And now the error conditions.
                    // If a tool determinism collection, or an attempt to go from deterministic to not deterministic.
                    if (existingRecord.Determinism.IsDeterministicTool && hashes.Determinism.IsDeterministicTool)
                    {
                        return eventing.StopFailure(new NotDeterministicFailure(Cache.CacheId, new FullCacheRecord(testStrongFingerprint, existingRecord), new FullCacheRecord(testStrongFingerprint, hashes)));
                    }
                }

                return eventing.Returns(await m_metadataSession.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, eventing.Id));
            }
        }

        public async Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new AddToCasStreamActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(filestream, urgencyHint);

                var result = await m_casSession.AddToCasAsync(filestream, hash, urgencyHint, eventing.Id);

                if (result.Succeeded)
                {
                    PinnedToCas.TryAdd(result.Result, 0);
                }

                return eventing.Returns(result);
            }
        }

        public async Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            using (var eventing = new AddToCasFilenameActivity(CompositingCache.EventSource, activityId, this))
            {
                eventing.Start(filename, fileState, urgencyHint);

                var result = await m_casSession.AddToCasAsync(filename, fileState, hash, urgencyHint, eventing.Id);

                if (result.Succeeded)
                {
                    PinnedToCas.TryAdd(result.Result, 0);
                }

                return eventing.Returns(result);
            }
        }

        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            return m_metadataSession.EnumerateSessionFingerprints(activityId);
        }

        public Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            return m_metadataSession.IncorporateRecordsAsync(strongFingerprints, activityId);
        }
    }
}
