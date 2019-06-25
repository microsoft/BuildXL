// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Utilities;

// ReSharper disable InconsistentNaming
namespace BuildXL.Cache.VerticalAggregator
{
    /// <summary>
    /// A cache aggregator that stiches a local and remote cache together.
    /// </summary>
    /// <remarks>
    /// Speed of implementation was chosen over speed of execution and efficiency.
    /// </remarks>
    public sealed class VerticalCacheAggregatorSession : ICacheSession
    {
        private readonly VerticalCacheAggregator m_cache;
        private readonly string m_cacheId;
        private readonly string m_sessionId;
        private readonly bool m_isReadOnly;
        private readonly bool m_remoteIsReadOnly;
        private readonly bool m_remoteContentIsReadOnly;
        private bool m_isClosed = false;
        private readonly ICacheSession m_localSession;
        private readonly ICacheSession m_remoteSession;
        private readonly ICacheReadOnlySession m_remoteROSession;
        private readonly SessionCounters m_sessionCounters;
        private readonly Lazy<Dictionary<string, double>> m_finalStats;

        internal VerticalCacheAggregatorSession(
            VerticalCacheAggregator cache,
            string sessionId,
            bool isReadOnly,
            ICacheSession localSession,
            ICacheSession remoteSession,
            ICacheReadOnlySession remoteROSession,
            bool remoteIsReadOnly,
            bool remoteContentIsReadOnly)
        {
            Contract.Requires(cache != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(sessionId));
            Contract.Requires(localSession != null);
            Contract.Requires(remoteROSession != null);
            Contract.Requires(remoteIsReadOnly || remoteSession != null);

            m_cache = cache;
            m_sessionId = sessionId;
            m_isReadOnly = isReadOnly;
            m_localSession = localSession;
            m_remoteSession = remoteSession;
            m_remoteROSession = remoteROSession;
            m_cacheId = localSession.CacheId + "_" + remoteROSession.CacheId;
            m_remoteIsReadOnly = remoteIsReadOnly;
            m_sessionCounters = new SessionCounters();
            m_finalStats = Lazy.Create(ExportStats);
            m_remoteContentIsReadOnly = remoteContentIsReadOnly;
        }

        /// <inheritdoc/>
        public string CacheSessionId => m_sessionId;

        /// <inheritdoc/>
        public string CacheId => m_cacheId;

        /// <inheritdoc/>
        public bool StrictMetadataCasCoupling => m_cache.StrictMetadataCasCoupling;

        /// <nodoc/>
        public bool IsReadOnly => m_isReadOnly;

        /// <inheritdoc/>
        public bool IsClosed => m_isClosed;

        /// <summary>
        /// Local session object.
        /// </summary>
        /// <remarks>
        /// For testing use.
        /// </remarks>
        public ICacheSession LocalSession => m_localSession;

        /// <summary>
        /// Remote session object.
        /// </summary>
        /// <remarks>
        /// For testing use.
        /// </remarks>
        public ICacheSession RemoteSession => m_remoteSession;

        /// <summary>
        /// Remote Readonly session object.
        /// </summary>
        /// <remarks>
        /// For testing use.
        /// </remarks>
        public ICacheReadOnlySession RemoteRoSession => m_remoteROSession;

        /// <inheritdoc/>
        public async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash inputList, Hash hashOfInputListContents, CasEntries casEntries, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(casEntries.IsValid);

            Contract.Assume(!IsReadOnly);
            using (var counters = m_sessionCounters.AddOrGetCounter())
            {
                using (var eventing = new AddOrGetActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start(weak, inputList, hashOfInputListContents, casEntries, urgencyHint);
                    try
                    {
                        // If the remote is our source of determinism, we always want to know if it has a better answer than we have now.
                        // Namely since there's no use pushing all the CAS content to it, just so we can find out we lost a race.
                        // OR if the add is SinglePhaseDeterministic, just skip being efficient. Because we're going to want to Add it.
                        CasEntries finalCasEntries;
                        bool remoteHadBetterAnswer;
                        if (!casEntries.Determinism.IsSinglePhaseNonDeterministic && !m_cache.RemoteCache.IsDisconnected)
                        {
                            var sfp = new StrongFingerprint(weak, inputList, hashOfInputListContents, m_cacheId);
                            var remoteOperation = await m_remoteROSession.GetCacheEntryAsync(sfp, urgencyHint, eventing.Id);
                            if ((!remoteOperation.Succeeded) && (remoteOperation.Failure.GetType() != typeof(NoMatchingFingerprintFailure)))
                            {
                                // We can drop this error and move on. This GetCacheEntry is a performance optimization to prevent trying to
                                // upload to a cache that's already got a better answer. If it fails, one of two things will happen:
                                // 1) It will disconnect if appropriate and save us additional failure costs below, and we'll treat it as disconnected.
                                // 2) We will try and upload the new CAS entries and those will work or not, and we'll try to commit the new fingerprint
                                //    and it won't stick if one already exists.
                                // So, while perf loss, no functional error in ignoring this failure.
                                var failure = new RemoteCacheFailure(m_cacheId, FailureConstants.FailureRemoteGet, remoteOperation.Failure);

                                // TODO: This failure needs to have keywords added so that it can be tracked as "Should be user facing"
                                eventing.Write(failure.ToETWFormat());
                            }

                            // Only accept a remotely-fetched value if that value is guaranteed by the remote cache to be usable (backed by content).
                            // AddOrGets must always overwrite an entry that isn't backed by content, so if the Remote Get returns a value that isn't
                            // guaranteed backed, AddOrGet must be called against the Remote (after uploading the *new* value's content) to ensure that the
                            // old value only wins if it is backed by content.
                            if (remoteOperation.Succeeded && (remoteOperation.Result.Determinism.IsDeterministicTool ||
                                                              remoteOperation.Result.Determinism.EffectiveGuid.Equals(m_cache.RemoteCache.CacheGuid)))
                            {
                                remoteHadBetterAnswer = true;
                                finalCasEntries = remoteOperation.Result;
                                counters.DeterminismRecovered();

                                if (casEntries.Determinism.IsDeterministicTool &&
                                   finalCasEntries.Determinism.IsDeterministicTool &&
                                   casEntries != finalCasEntries)
                                {
                                    // Trying to change Tool Deterministic files? No.
                                    counters.Failure();
                                    var failure = new NotDeterministicFailure(m_cache.CacheId, new FullCacheRecord(sfp, finalCasEntries), new FullCacheRecord(sfp, casEntries));
                                    return eventing.Returns(failure);
                                }

                                if (finalCasEntries.Determinism.IsSinglePhaseNonDeterministic)
                                {
                                    counters.Failure();
                                    var failure = new SinglePhaseMixingFailure(m_cacheId);
                                    return eventing.Returns(failure);
                                }
                            }
                            else
                            {
                                remoteHadBetterAnswer = false;
                                finalCasEntries = casEntries;
                            }
                        }
                        else
                        {
                            finalCasEntries = casEntries;
                            remoteHadBetterAnswer = false;
                        }

                        if (!m_remoteIsReadOnly && !remoteHadBetterAnswer && !m_cache.RemoteCache.IsDisconnected)
                        {
                            List<CasHash> filesToUpload = new List<CasHash>(casEntries);

                            if (!inputList.Equals(CasHash.NoItem))
                            {
                                filesToUpload.Add(inputList);
                            }

                            var uploadResult = new Possible<CasHash[], Failure>();
                            if (!m_remoteContentIsReadOnly)
                            {
                                uploadResult = await CopyCASFilesIfNeededAsync(m_localSession, m_remoteSession, urgencyHint, filesToUpload.ToArray(), false, eventing, counters.CopyStats);
                                if (!uploadResult.Succeeded)
                                {
                                    var failure = uploadResult.Failure;

                                    // If we failed because a file wasn't pinned in the source, report that error, else
                                    // report the wrapped error.
                                    if (failure.InnerFailure != null && failure.InnerFailure is UnpinnedCasEntryFailure)
                                    {
                                        counters.UploadDisregarded();
                                        counters.Failure();
                                        failure = failure.InnerFailure;
                                        return eventing.StopFailure(failure);
                                    }

                                    // Since we failed to upload the content, we are going to stop. If we can't upload content that was needed in the remote
                                    // there's not much point in trying to be a determinism recovering aggregator.
                                    // We'll still try and save to the local cache
                                    // TODO: This failure needs to have keywords added so that it can be tracked as "Should be user facing"
                                    eventing.Write(failure.ToETWFormat());
                                    counters.UploadDisregarded();
                                    counters.AddedLocal();
                                }
                            }

                            // If remote content is read-only, continue even without uploading
                            if (m_remoteContentIsReadOnly || uploadResult.Succeeded)
                            {
                                var remoteOperation = await m_remoteSession.AddOrGetAsync(weak, inputList, hashOfInputListContents, casEntries, urgencyHint, eventing.Id);
                                if (!remoteOperation.Succeeded)
                                {
                                    counters.Failure();

                                    if (remoteOperation.Failure is SinglePhaseMixingFailure)
                                    {
                                        return eventing.Returns(remoteOperation);
                                    }

                                    var remoteFailure = new RemoteCacheFailure(m_cacheId, FailureConstants.FailureRemoteAdd, remoteOperation.Failure);

                                    // TODO: This failure needs to have keywords added so that it can be tracked as "Should be user facing"
                                    eventing.Write(remoteFailure.ToETWFormat());
                                    finalCasEntries = casEntries;
                                }
                                else
                                {
                                    // If we got back content different than what we sent, and the remote is the source of determinism, honor that content.
                                    // Else, keep what we have and move on.
                                    if (remoteOperation.Result.Record != null)
                                    {
                                        counters.DeterminismRecovered();
                                        finalCasEntries = remoteOperation.Result.Record.CasEntries;
                                        remoteHadBetterAnswer = true;
                                    }
                                    else
                                    {
                                        counters.AddedRemote();
                                        finalCasEntries = new CasEntries(casEntries, remoteOperation.Result.Determinism);
                                        remoteHadBetterAnswer = false;
                                    }
                                }
                            }
                        }

                        // If we can't talk to the remote, add to only the local cache.
                        if ((m_remoteIsReadOnly && !remoteHadBetterAnswer) || m_cache.RemoteCache.IsDisconnected)
                        {
                            counters.AddedLocal();
                        }

                        Possible<FullCacheRecordWithDeterminism, Failure> localAddResult = await m_localSession.AddOrGetAsync(
                            weak,
                            inputList,
                            hashOfInputListContents,
                            finalCasEntries,
                            urgencyHint,
                            eventing.Id);

                        if (!localAddResult.Succeeded)
                        {
                            counters.Failure();
                            var failure = new LocalCacheFailure(m_cacheId, FailureConstants.FailureLocalAdd, localAddResult.Failure);
                            return eventing.Returns(failure);
                        }

                        // assert the authoritative consistency. If a remote cache returns an authoritative answer
                        // and adds it to the local cache, the local cache cannot be operating in an authoritative mode.
                        // and therefore must return one of two things:
                        // 1) The determinism of the remote cache
                        // 2) any other guid (if the remote cache returns a none/non-authoritative answer)
                        if (finalCasEntries.Determinism.IsDeterministic
                            && localAddResult.Result.Determinism.EffectiveGuid != finalCasEntries.Determinism.EffectiveGuid)
                        {
                            counters.Failure();
                            var failure = new InconsistentCacheStateFailure(
                                $"local record returned a determinism guid {localAddResult.Result.Determinism.EffectiveGuid} that doesn't match remote {finalCasEntries.Determinism.EffectiveGuid}. This may be because the local cache is running in an authoritative mode.");
                            return eventing.Returns(failure);
                        }

                        if (remoteHadBetterAnswer && finalCasEntries != casEntries)
                        {
                            var result = new FullCacheRecord(new StrongFingerprint(weak, inputList, hashOfInputListContents, m_remoteROSession.CacheId), finalCasEntries);
                            return eventing.Returns(new FullCacheRecordWithDeterminism(result));
                        }
                        else
                        {
                            return eventing.Returns(localAddResult);
                        }
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        counters.Failure();
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filestream != null);

            Contract.Assume(!IsReadOnly);

            using (var counters = m_sessionCounters.AddToCasCounter())
            {
                using (var eventing = new AddToCasStreamActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start(filestream, urgencyHint);
                    try
                    {
                        // TODO: Provide an option to lazily start to upload to Remote.
                        var result = await m_localSession.AddToCasAsync(filestream, hash, urgencyHint, eventing.Id);

                        if (!m_remoteIsReadOnly && m_cache.WriteThroughCasData && result.Succeeded && !m_cache.RemoteCache.IsDisconnected)
                        {
                            counters.WriteThrough();
                            result = await CopyCASFileIfNeededAsync(result.Result, m_localSession, m_remoteSession, urgencyHint, false, eventing, counters.CopyStats);
                        }

                        return eventing.Returns(result);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filename != null);

            Contract.Assume(!IsReadOnly);
            using (var counters = m_sessionCounters.AddToCasCounter())
            {
                using (var eventing = new AddToCasFilenameActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    try
                    {
                        eventing.Start(filename, fileState, urgencyHint);

                        var result = await m_localSession.AddToCasAsync(filename, fileState, hash, urgencyHint, eventing.Id);

                        if (!m_remoteIsReadOnly && m_cache.WriteThroughCasData && result.Succeeded && !m_cache.RemoteCache.IsDisconnected)
                        {
                            counters.WriteThrough();
                            var remotePin = await m_remoteROSession.PinToCasAsync(result.Result);
                            if (remotePin.Succeeded)
                            {
                                counters.CopyStats.FileSkipped();
                                eventing.Write(ETWConstants.ItemAlreadyExistsAtDestination);
                                return eventing.Returns(result);
                            }
                            else if (!(remotePin.Failure is NoCasEntryFailure))
                            {
                                counters.CopyStats.FileTransitFailed();

                                // Unknown failure trying to pin content. Fail.
                                return eventing.StopFailure(remotePin.Failure);
                            }

                            var addResult = await m_remoteSession.AddToCasAsync(filename, fileState, result.Result, urgencyHint, eventing.Id);
                            if (!addResult.Succeeded)
                            {
                                counters.CopyStats.FileTransitFailed();
                                var failure = new CASTransferFailure(
                                    m_cacheId,
                                    m_localSession.CacheId,
                                    m_remoteSession.CacheId,
                                    result.Result,
                                    FailureConstants.FailureCASUpload,
                                    addResult.Failure);
                                return eventing.StopFailure(failure);
                            }

                            if (!addResult.Result.Equals(result.Result))
                            {
                                var ret = new InconsistentCacheStateFailure(
                                    "CasHash ({0}) returned by Remote cache {1} for file {2} did not match CasHash ({3}) returned by Local cache {4}",
                                    addResult.Result,
                                    m_remoteSession.CacheId,
                                    filename,
                                    result.Result,
                                    m_localSession);
                                return eventing.StopFailure(ret);
                            }

                            counters.CopyStats.FileTransited(new FileInfo(filename).Length);
                        }

                        return eventing.Returns(result);
                    }
                    catch (Exception e)
                    {
                        return eventing.StopFailure(new AddToCasFailure(CacheId, hash, filename, e));
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            try
            {
                using (var eventing = new CloseActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start();

                    try
                    {
                        if (m_isClosed)
                        {
                            eventing.Write(ETWConstants.SessionClosed);
                            return eventing.Returns(m_sessionId);
                        }

                        m_isClosed = true;

                        var localCloseTask = await m_localSession.CloseAsync(activityId);

                        if (!localCloseTask.Succeeded)
                        {
                            var remoteCloseTask = await m_remoteROSession.CloseAsync(activityId);
                            if (!remoteCloseTask.Succeeded)
                            {
                                var result = new AggregateFailure(localCloseTask.Failure, remoteCloseTask.Failure);
                                return eventing.StopFailure(result);
                            }

                            var failure = new LocalCacheFailure(m_cacheId, FailureConstants.FailureLocal, localCloseTask.Failure);
                            return eventing.StopFailure(failure);
                        }

                        // Only if the remote if read-write and source of determinism
                        if (!m_remoteIsReadOnly && !m_cache.RemoteCache.IsDisconnected)
                        {
                            var fingerprints = m_localSession.EnumerateSessionFingerprints();
                            var records = await m_remoteSession.IncorporateRecordsAsync(fingerprints);
                            if (!records.Succeeded)
                            {
                                var remoteCloseTask = await m_remoteSession.CloseAsync(activityId);
                                if (!remoteCloseTask.Succeeded)
                                {
                                    var result = new AggregateFailure(records.Failure, remoteCloseTask.Failure);
                                    return eventing.StopFailure(result);
                                }

                                var failure = new RemoteCacheFailure(m_cacheId, FailureConstants.FailureRemote, records.Failure);
                                return eventing.StopFailure(failure);
                            }
                        }

                        var finalClose = await m_remoteROSession.CloseAsync(activityId);
                        if (!finalClose.Succeeded)
                        {
                            return eventing.StopFailure(finalClose.Failure);
                        }

                        // If everything is fine, return the local close task as the local may have been
                        // a named session, with a readonly remote.
                        return eventing.Returns(localCloseTask);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        throw;
                    }
                }
            }
            finally
            {
                // We need to do the collection of statistics and logging
                // at the last point after close has completed.
                using (var eventing = new CacheActivity(VerticalCacheAggregator.EventSource, CacheActivity.StatisticOptions, activityId, "SessionStatistics", CacheId))
                {
                    eventing.Start();
                    eventing.WriteStatistics(m_finalStats.Value);
                    eventing.Stop();
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var counters = m_sessionCounters.EnumerateStrongFingerprintsCounter())
            {
                using (var eventing = new EnumerateStrongFingerprintsActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start(weak, urgencyHint);

                    foreach (var oneEntry in m_localSession.EnumerateStrongFingerprints(weak, urgencyHint, eventing.Id))
                    {
                        counters.YieldReturnLocal();
                        yield return oneEntry;
                    }

                    eventing.Write("StrongFingerprintSentinel");

                    counters.YieldReturnSenintel();
                    yield return Task.FromResult(new Possible<StrongFingerprint, Failure>(StrongFingerprintSentinel.Instance));

                    if (!m_cache.RemoteCache.IsDisconnected)
                    {
                        foreach (var oneEntry in m_remoteROSession.EnumerateStrongFingerprints(weak, urgencyHint, eventing.Id))
                        {
                            counters.YieldReturnRemote();
                            yield return oneEntry;
                        }
                    }

                    eventing.Stop();
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var counters = m_sessionCounters.GetCacheEntryCounter())
            {
                using (var eventing = new GetCacheEntryActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    try
                    {
                        eventing.Start(strong, urgencyHint);

                        // First check the local cache.
                        var localResult = await m_localSession.GetCacheEntryAsync(strong, urgencyHint, eventing.Id);

                        // Ok, so if that worked, and the result is deterministic don't bother with the remote
                        // cache. Also, don't bother if we have a result, and we're not trying to enforce determinism into
                        // the system.
                        //
                        // The assumption here is that something external to build keeps the local and remote
                        // in some semblance of sync should the remote drop content.
                        // (i.e., the sessions in the local should be a sub-set of the sessions in the remote OR should have
                        // all their CAS content local)
                        if (localResult.Succeeded && (localResult.Result.Determinism.IsDeterministicTool ||
                                                      localResult.Result.Determinism.IsSinglePhaseNonDeterministic ||
                                                      m_cache.RemoteCache.IsDisconnected ||
                                                      localResult.Result.Determinism.EffectiveGuid.Equals(m_cache.RemoteCache.CacheGuid)))
                        {
                            counters.CacheHitLocal();
                            return eventing.Returns(localResult);
                        }

                        // If the local failed for a reason other than a cache miss, stop and bail.
                        if (!localResult.Succeeded && !(localResult.Failure is NoMatchingFingerprintFailure))
                        {
                            var failure = new LocalCacheFailure(m_cacheId, FailureConstants.FailureLocal, localResult.Failure);
                            counters.Failure();
                            return eventing.Returns(failure);
                        }

                        CasEntries finalEntries;

                        var remoteResult = await m_remoteROSession.GetCacheEntryAsync(strong, urgencyHint, eventing.Id);
                        if (remoteResult.Succeeded)
                        {
                            // If the local result also had been successful, we're querying the remote to attempt to recover determinism.
                            // So if both were successful, that means we're recovering bad determinism.
                            if (localResult.Succeeded)
                            {
                                counters.DeterminismRecovered();
                            }
                            else
                            {
                                counters.CacheHitRemote();
                            }

                            finalEntries = remoteResult.Result;
                        }
                        else if (!localResult.Succeeded)
                        {
                            // Local either worked, or had a cache miss. And the remote failed.
                            if (remoteResult.Failure is NoMatchingFingerprintFailure)
                            {
                                // If failed for a cache miss, return that.
                                return eventing.Returns(remoteResult);
                            }

                            eventing.Write(remoteResult.Failure.ToETWFormat());
                            counters.Failure();
                            return eventing.Returns(localResult);
                        }
                        else if (!m_remoteIsReadOnly && (remoteResult.Failure is NoMatchingFingerprintFailure))
                        {
                            // The remote has never heard of this entry, and is writable. So we need to add it and then stuff what comes back into the local.
                            // It would be tempting to skip the query above and just do the AddOrGet directly, but that requires adding all the CAS content to
                            // the remote, and it should be far cheaper to pay the price of a failed Get than the upload of a bunch of CAS data and then find out
                            // that the remote already had the content we need.
                            List<CasHash> filesToCopy = new List<CasHash>(localResult.Result.Count + 1);
                            filesToCopy.Add(strong.CasElement);
                            filesToCopy.AddRange(localResult.Result);

                            // Yes, the return formatting here is crazy. The compiler got confused on un-initialized variables when I had a bool set in
                            // each else clause and a single return point at the bottom. This made it happy.
                            if (!m_remoteContentIsReadOnly)
                            {
                                var copyResult = await CopyCASFilesIfNeededAsync(m_localSession, m_remoteSession, urgencyHint, filesToCopy.ToArray(), true, eventing, counters.CopyStats);
                                if (!copyResult.Succeeded)
                                {
                                    eventing.Write(copyResult.Failure.ToETWFormat());

                                    counters.UploadDisregarded();
                                    if (localResult.Succeeded)
                                    {
                                        counters.CacheHitLocal();
                                    }

                                    return eventing.Returns(localResult);
                                }
                            }

                            CasEntries localEntries = localResult.Result;

                            // Only propagate ToolDeterministic or None up. Do not send up a random cache ID for determinism.
                            var remoteAddResult = await m_remoteSession.AddOrGetAsync(
                                strong.WeakFingerprint,
                                strong.CasElement,
                                strong.HashElement,
                                localEntries.Determinism.IsDeterministicTool || !localEntries.Determinism.IsDeterministic ? localEntries : new CasEntries(localEntries),
                                urgencyHint, eventing.Id);
                            if (!remoteAddResult.Succeeded)
                            {
                                var failure = new RemoteCacheFailure(m_cacheId, FailureConstants.FailureRemoteAdd, remoteAddResult.Failure);
                                eventing.Write(failure.ToETWFormat());
                                counters.Failure();
                                return eventing.Returns(localResult);
                            }

                            if (remoteAddResult.Result.Record == null)
                            {
                                finalEntries = new CasEntries(localResult.Result, remoteAddResult.Result.Determinism);
                                counters.PromotedRemote();
                            }
                            else
                            {
                                finalEntries = remoteAddResult.Result.Record.CasEntries;
                                counters.UploadDisregarded();
                                counters.DeterminismRecovered();
                            }
                        }
                        else
                        {
                            // Remote is read only, and we had a local hit.
                            if (!(remoteResult.Failure is NoMatchingFingerprintFailure))
                            {
                                eventing.Write(remoteResult.Failure.ToETWFormat());
                            }

                            // Remote doesn't have a better entry, and we can't update it.
                            // So return the local entry we do have.
                            counters.CacheHitLocal();
                            return eventing.Returns(localResult);
                        }

                        CasEntries retCasEntries = finalEntries;

                        // If the remote returned anything, drop it in the local and return the remote info.
                        var localAdd = await m_localSession.AddOrGetAsync(
                            strong.WeakFingerprint,
                            strong.CasElement,
                            strong.HashElement,
                            retCasEntries,
                            urgencyHint,
                            eventing.Id);
                        if (!localAdd.Succeeded)
                        {
                            counters.Failure();
                            var failure = new LocalCacheFailure(m_cacheId, FailureConstants.FailureLocalAdd, localAdd.Failure);
                            return eventing.Returns(failure);
                        }

                        // assert the authoritative consistency. If a remote cache returns an authoritative answer
                        // and adds it to the local cache, the local cache cannot be operating in an authoritative mode.
                        // and therefore must return one of two things:
                        // 1) The determinism of the remote cache
                        // 2) any other guid (if the remote cache returns a none/non-authoritative answer)
                        if (retCasEntries.Determinism.IsDeterministic
                            && localAdd.Result.Determinism.EffectiveGuid != retCasEntries.Determinism.EffectiveGuid)
                        {
                            counters.Failure();
                            var failure = new LocalCacheFailure(
                                m_cacheId,
                                $"local record returned a determinism guid {localAdd.Result.Determinism.EffectiveGuid} that doesn't match remote {retCasEntries.Determinism.EffectiveGuid}. This may be because the local cache is running in an authoritative mode.",
                                null);
                            return eventing.Returns(failure);
                        }

                        // If a remote cache returns an authoritative answer, then the local cache must accept it.
                        if (retCasEntries.Determinism.IsDeterministic && localAdd.Result.Record != null)
                        {
                            counters.Failure();
                            var result = new InconsistentCacheStateFailure("Failed to update local record.");
                            return eventing.Returns(result);
                        }

                        return eventing.Returns(retCasEntries);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        counters.Failure();
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var counters = m_sessionCounters.GetStreamCounter())
            {
                using (var eventing = new GetStreamActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start(hash, urgencyHint);
                    try
                    {
                        var localStream = await m_localSession.GetStreamAsync(hash, urgencyHint, eventing.Id);
                        if (localStream.Succeeded)
                        {
                            counters.HitLocal();
                            return eventing.Returns(localStream);
                        }

                        // NOTE: Avoid checking for existence in local session since GetStream already failed.
                        var hashLocal = await CopyCASFileIfNeededAsync(hash, m_remoteROSession, m_localSession, urgencyHint, true, eventing, counters.CopyStats, false);

                        if (!hashLocal.Succeeded)
                        {
                            if (hashLocal.Failure is NoCasEntryFailure)
                            {
                                counters.Miss();
                            }
                            else
                            {
                                counters.Fail();
                            }

                            eventing.StopFailure(hashLocal.Failure);
                            return localStream;
                        }

                        var ret = await m_localSession.GetStreamAsync(hash, urgencyHint, eventing.Id);
                        if (ret.Succeeded)
                        {
                            counters.HitRemote();
                        }
                        else
                        {
                            // We just added the file. It better be there.
                            counters.Fail();
                        }

                        return eventing.Returns(ret);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        counters.Fail();
                        throw;
                    }
                }
            }
        }

        private async Task<Possible<CasHash, Failure>> CopyCASFileIfNeededAsync(
            CasHash hash,
            ICacheReadOnlySession sourceSession,
            ICacheSession targetSession,
            UrgencyHint urgencyHint,
            bool pinSource,
            CacheActivity parentActivity,
            SessionCounters.CasCopyStats counters,
            bool checkExistsInTarget = true)
        {
            using (CacheActivity eventing = new CacheActivity("CopyCASFileIfNeeded", parentActivity))
            {
                eventing.StartWithMethodArguments(new
                {
                    CasHash = hash,
                    SourceSession = sourceSession.CacheId,
                    TargetSession = targetSession.CacheId,
                    UrgencyHint = urgencyHint,
                    PinSource = pinSource,
                    CheckExistsInTarget = checkExistsInTarget,
                });

                try
                {
                    if (hash.Equals(CasHash.NoItem))
                    {
                        counters.FileSkipped();
                        eventing.Write(ETWConstants.NoItemCopy);
                        eventing.Stop();
                        return hash;
                    }

                    // This is for the potential inconssistent hash recovery operation
                    bool secondTry = false;
                    while (true)
                    {
                        if (checkExistsInTarget)
                        {
                            var targetPin = await targetSession.PinToCasAsync(hash);
                            if (targetPin.Succeeded)
                            {
                                counters.FileSkipped();
                                eventing.Write(ETWConstants.ItemAlreadyExistsAtDestination);
                                eventing.Stop();
                                return hash;
                            }
                            else if (!(targetPin.Failure is NoCasEntryFailure))
                            {
                                counters.FileTransitFailed();

                                // Unknown failure trying to pin content. Fail.
                                return eventing.StopFailure(targetPin.Failure);
                            }
                        }

                        if (pinSource)
                        {
                            // Pin the target on the source, so it knows we intend to copy it.
                            var sourcePin = sourceSession.PinToCasAsync(hash);
                            var sourceResult = await sourcePin;
                            if (!sourceResult.Succeeded)
                            {
                                counters.FileTransitFailed();
                                var failure = new CASTransferFailure(m_cacheId, sourceSession.CacheId, targetSession.CacheId, hash, FailureConstants.FailureSourcePin, sourceResult.Failure);
                                return eventing.StopFailure(failure);
                            }
                        }

                        // Copy it to the target.
                        var sourceStreamResult = await sourceSession.GetStreamAsync(hash, urgencyHint, eventing.Id);
                        if (!sourceStreamResult.Succeeded)
                        {
                            // An upload could fail at this point because the source wasn't pinned. Since we don't know if the file copy is an upload or a download, we'll
                            // just wrap the error either way, and the caller can unwrap if needed.
                            counters.FileTransitFailed();
                            var failure = new CASTransferFailure(m_cacheId, sourceSession.CacheId, targetSession.CacheId, hash, FailureConstants.FailureGetSourceStream, sourceStreamResult.Failure);
                            return eventing.StopFailure(failure);
                        }

                        Stream sourceStream = sourceStreamResult.Result;

                        var addResult = await targetSession.AddToCasAsync(sourceStream, hash, urgencyHint, eventing.Id);
                        if (!addResult.Succeeded)
                        {
                            counters.FileTransitFailed();
                            var failure = new CASTransferFailure(m_cacheId, sourceSession.CacheId, targetSession.CacheId, hash, FailureConstants.FailureCASUpload, addResult.Failure);
                            return eventing.StopFailure(failure);
                        }

                        try
                        {
                            if (sourceStream.CanSeek)
                            {
                                counters.FileTransited(sourceStream.Length);
                            }
                            else
                            {
                                counters.FileSizeUnknown();
                            }
                        }
                        catch (Exception e)
                        {
                            // Don't really care why we can't get the file size. Just report it as unknown and stuff the error in an event.
                            counters.FileSizeUnknown();
                            eventing.Write(new
                            {
                                FromCache = sourceSession.CacheId,
                                ToCache = targetSession.CacheId,
                                ErrorFindingStreamSize = e.ToString(),
                            });
                        }

                        // If all is good, return now.
                        if (addResult.Result.Equals(hash))
                        {
                            eventing.Stop();
                            return addResult.Result;
                        }

                        // Put a critical event into the eventing system as this id really
                        // important to have something take notice that we had this happened.
                        eventing.Write(CacheActivity.CriticalDataOptions, new
                        {
                            InconsistentCacheHash = hash,
                            FromCache = sourceSession.CacheId,
                            WrittenAs = addResult.Result,
                            ToCache = targetSession.CacheId,
                        });

                        // If this is our second time around the bend with hash mismatch, we are in big trouble
                        if (secondTry)
                        {
                            return eventing.StopFailure(new InconsistentCacheStateFailure(
                                "CashHash ({0}) returned by target cache {1} did not match CashHash ({2}) sent from source cache {3} - second try!",
                                addResult.Result, targetSession.CacheId, hash, sourceSession.CacheId));
                        }

                        // We have a hash mismatch - we need to ask the source cache to
                        // validate the content as it may be bad
                        var validateResult = await sourceSession.ValidateContentAsync(hash, urgencyHint, eventing.Id);
                        if (!validateResult.Succeeded)
                        {
                            eventing.Write(CacheActivity.VerboseOptions, new { RemediationFailed = hash, Reason = validateResult.Failure.Describe() });

                            return eventing.StopFailure(new InconsistentCacheStateFailure(
                                "CashHash ({0}) returned by target cache {1} did not match CashHash ({2}) sent from source cache {3} - cache ValidateContent failed with error: {4}",
                                addResult.Result, targetSession.CacheId, hash, sourceSession.CacheId, validateResult.Failure.Describe()));
                        }

                        if (validateResult.Result == ValidateContentStatus.Ok)
                        {
                            // The cache says things should be fine now, so lets try again (once)
                            secondTry = true;
                        }

                        if (validateResult.Result == ValidateContentStatus.Remediated)
                        {
                            // The cache says it remediated the issue.  This means we
                            // should try one more time but we also potentially need to
                            // do a fresh pin operation (as the prior pinned item was
                            // remediated)
                            secondTry = true;
                            pinSource = true;
                        }

                        // If we are not going to try again, lets return the error
                        if (!secondTry)
                        {
                            var ret = new InconsistentCacheStateFailure(
                                "CashHash ({0}) returned by target cache {1} did not match CashHash ({2}) sent from source cache {3} - cache ValidateContent returned {4}",
                                addResult.Result, targetSession.CacheId, hash, sourceSession.CacheId, validateResult.Result.ToString());

                            return eventing.StopFailure(ret);
                        }

                        // ETW about the retry
                        eventing.Write(CacheActivity.VerboseOptions, new { Retry = hash, Reason = validateResult.Result, FromCache = sourceSession.CacheId });
                    }
                }
                catch (Exception e)
                {
                    eventing.StopException(e);
                    throw;
                }
            }
        }

        /// <summary>
        /// Copies multiple files from one CAS to another if the files are not already in the destination.
        /// </summary>
        /// <param name="sourceSession">Session to copy files from</param>
        /// <param name="targetSession">session to copy to.</param>
        /// <param name="urgencyHint">Urgency hint.</param>
        /// <param name="casHashes">Files to copy.</param>
        /// <param name="pinSourceFiles">Source files must be pinned when downloaded, not when uploaded to remote. This is becasue the caller should have added the files to the session before invoking the cache.</param>
        /// <param name="parentActivity">The parent activity of this call.</param>
        /// <param name="counters">Statistic counters.</param>
        /// <returns>An array of the hashes moved.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall")]
        private async Task<Possible<CasHash[], Failure>> CopyCASFilesIfNeededAsync(
            ICacheReadOnlySession sourceSession,
            ICacheSession targetSession,
            UrgencyHint urgencyHint,
            CasHash[] casHashes,
            bool pinSourceFiles,
            CacheActivity parentActivity,
            SessionCounters.CasCopyStats counters)
        {
            using (CacheActivity eventing = new CacheActivity("CopyCASFilesIfNeeded", parentActivity))
            {
                eventing.StartWithMethodArguments(new
                {
                    CasHashes = casHashes,
                    SourceSession = sourceSession.CacheId,
                    TargetSession = targetSession.CacheId,
                    UrgencyHint = urgencyHint,
                    PinSource = pinSourceFiles,
                });

                try
                {
                    List<Task<Possible<CasHash, Failure>>> fileUploads = new List<Task<Possible<CasHash, Failure>>>(casHashes.Length + 1);
                    HashSet<CasHash> filesInMotion = new HashSet<CasHash>();

                    // First thing we do is pin/publish the CAS items to the remote cache.
                    Possible<string, Failure>[] pinResults = await targetSession.PinToCasAsync(casHashes, urgencyHint);
                    bool allPinsFailed = pinResults.Length == 1 && !pinResults[0].Succeeded;
                    for (int i = 0; i < casHashes.Length; i++)
                    {
                        if (allPinsFailed || !pinResults[i].Succeeded)
                        {
                            CasHash oneHash = casHashes[i];
                            if (!filesInMotion.Contains(oneHash))
                            {
                                fileUploads.Add(
                                    CopyCASFileIfNeededAsync(oneHash, sourceSession, targetSession, urgencyHint, pinSourceFiles, eventing, counters, checkExistsInTarget: false));
                                filesInMotion.Add(oneHash);
                            }
                        }
                    }

                    // We have filed to upload. Wait for them.
                    // And now make sure that there were no errors and no cosmic bit flips.
                    foreach (Task<Possible<CasHash, Failure>> completeFileTask in fileUploads.OutOfOrderResultsAsync())
                    {
                        Possible<CasHash, Failure> completeFile = await completeFileTask;

                        if (!completeFile.Succeeded)
                        {
                            return eventing.StopFailure(completeFile.Failure);
                        }
                    }

                    eventing.Stop();
                    return casHashes;
                }
                catch (Exception e)
                {
                    eventing.StopException(e);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries casEntries, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new PinToCasMultipleActivity(VerticalCacheAggregator.EventSource, activityId, this))
            {
                eventing.Start(casEntries, urgencyHint);
                try
                {
                    Possible<string, Failure>[] retValues = new Possible<string, Failure>[casEntries.Count];

                    // We can skip querying the actual cache sessions if nothing is requested. This noop is important because
                    // counters below won't set a status and will NRE if no casEntries are requested.
                    if (casEntries.Count == 0)
                    {
                        return eventing.Returns(retValues);
                    }

                    using (var counters = m_sessionCounters.PinToCasCounter())
                    {
                        try
                        {
                            Possible<string, Failure>[] localResultSet = await m_localSession.PinToCasAsync(casEntries, urgencyHint, eventing.Id);

                            int remoteCheckCount = 0;
                            for (int i = 0; i < localResultSet.Length; i++)
                            {
                                if (localResultSet[i].Succeeded)
                                {
                                    counters.PinHitLocal();
                                    retValues[i] = localResultSet[i];
                                }
                                else
                                {
                                    remoteCheckCount++;
                                }
                            }

                            if (remoteCheckCount > 0)
                            {
                                CasHash[] hashes = new CasHash[remoteCheckCount];
                                int[] localMap = new int[remoteCheckCount];
                                int hashIndex = 0;
                                for (int i = 0; i < localResultSet.Length; i++)
                                {
                                    if (!localResultSet[i].Succeeded)
                                    {
                                        hashes[hashIndex] = casEntries[i];
                                        localMap[hashIndex] = i;
                                        hashIndex++;
                                    }
                                }

                                CasEntries remotePinCheck = new CasEntries(hashes);
                                Possible<string, Failure>[] remotePins = await m_remoteROSession.PinToCasAsync(
                                    remotePinCheck,
                                    urgencyHint,
                                    eventing.Id);
                                for (int i = 0; i < remotePins.Length; i++)
                                {
                                    retValues[localMap[i]] = remotePins[i];
                                    if (remotePins[i].Succeeded)
                                    {
                                        counters.PinHitRemote();
                                    }
                                    else
                                    {
                                        counters.PinMiss();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            counters.Fail();
                            throw;
                        }
                    }

                    // Since this method calls PinToCasAsync which traces out all return values, there's no need to repeat the exercise here.
                    return eventing.Returns(retValues);
                }
                catch (Exception e)
                {
                    eventing.StopException(e);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var counters = m_sessionCounters.PinToCasCounter())
            {
                using (var eventing = new PinToCasActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start(hash, urgencyHint);

                    try
                    {
                        var localResult = await m_localSession.PinToCasAsync(hash, urgencyHint, eventing.Id);

                        if (localResult.Succeeded)
                        {
                            counters.PinHitLocal();
                            return eventing.Returns(localResult);
                        }

                        // TODO: We should filter the return code for a miss, but at this time, we don't get a consistent return back from all known caches.
                        // BasicFileSystem will return a UnPinnedCasEntryFailure if we try to get content that hasn't been pinned, even if the content is not in the cache.
                        // In fact, this is by design as it will prevent a remote access for items that are not pinned.  It does mean that we give you a different
                        // message which is to be clear about the cause but this is by design.  Note also that InMemory does the same thing to show that behavior.
                        var result = await m_remoteROSession.PinToCasAsync(hash, urgencyHint, eventing.Id);
                        if (!result.Succeeded)
                        {
                            counters.PinMiss();

                            // If both are NoCasEntry's, return that to preserve contract correctness.
                            if (localResult.Failure is NoCasEntryFailure && result.Failure is NoCasEntryFailure)
                            {
                                return eventing.Returns(result);
                            }

                            var failure = new RemoteCacheFailure(m_cacheId, FailureConstants.FailureRemotePin, result.Failure);
                            return eventing.Returns(failure);
                        }

                        counters.PinHitRemote();
                        return eventing.Returns(result);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        counters.Fail();
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<string, Failure>> ProduceFileAsync(
            CasHash hash,
            string filename,
            FileState fileState,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            using (var counters = m_sessionCounters.ProduceFileCounter())
            {
                using (var eventing = new ProduceFileActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    try
                    {
                        eventing.Start(hash, filename, fileState, urgencyHint);

                        // TODO: Remove this once the remote properly handles the directory's nonexistence
                        // NOTE: There are unit tests that validate this behavior but the VSTS cache does not
                        //       seem to run these tests.  Unclear why it does not run the normal ICache test suite
                        FileUtilities.CreateDirectory(Path.GetDirectoryName(filename));

                        // This is for the potential inconssistent hash recovery operation
                        bool secondTry = false;
                        while (true)
                        {
                            var localResult = await m_localSession.ProduceFileAsync(hash, filename, fileState, urgencyHint, eventing.Id);
                            if (localResult.Succeeded)
                            {
                                counters.HitLocal();
                                return eventing.Returns(localResult);
                            }

                            // Download to the final location.
                            var remoteProduceFileResult = await m_remoteROSession.ProduceFileAsync(hash, filename, fileState, urgencyHint, activityId);

                            if (!remoteProduceFileResult.Succeeded)
                            {
                                counters.CopyStats.FileTransitFailed();
                                counters.Fail();
                                var failure = new CASTransferFailure(
                                    m_cacheId,
                                    m_remoteROSession.CacheId,
                                    m_localSession.CacheId,
                                    hash,
                                    FailureConstants.FailureCASDownload,
                                    remoteProduceFileResult.Failure);
                                return eventing.StopFailure(failure);
                            }

                            // Add to the local session.
                            // IMPORTANT: Don't provide the hash so that the local cache must validate the hash on download.
                            // If we ever decide that we can always trust Remote.ProduceFile (not likely),
                            // then passing the hash here might be a nice optimization, allowing the local AddToCas to short-cut
                            // if it already has content with the passed hash.
                            var localAddResult = await m_localSession.AddToCasAsync(filename, fileState, null, urgencyHint, eventing.Id);
                            if (!localAddResult.Succeeded)
                            {
                                counters.CopyStats.FileTransitFailed();
                                counters.Fail();
                                var failure = new CASTransferFailure(
                                    m_cacheId,
                                    m_remoteROSession.CacheId,
                                    m_localSession.CacheId,
                                    hash,
                                    FailureConstants.FailureCASDownload,
                                    localAddResult.Failure);
                                return eventing.StopFailure(failure);
                            }

                            counters.CopyStats.FileTransited(new FileInfo(filename).Length);

                            if (localAddResult.Result.Equals(hash))
                            {
                                counters.HitRemote();
                                return eventing.Returns(remoteProduceFileResult);
                            }

                            // Put a critical event into the eventing system as this id really
                            // important to have something take notice that we had this happened.
                            eventing.Write(CacheActivity.CriticalDataOptions, new
                            {
                                InconsistentCacheHash = hash,
                                FromCache = m_remoteROSession.CacheId,
                                WrittenAs = localAddResult.Result,
                                ToCache = m_localSession.CacheId,
                            });

                            // The file we produces is wrong so we will try to delete
                            // it.  We don't care if it does not actually delete since
                            // we will be telling about the failure in the cache but
                            // we don't want to leave the wrong file in its target
                            // location if we don't have to.
                            try
                            {
                                File.Delete(filename);
                            }
                            catch (Exception e)
                            {
                                // Nothing to do but log a bit
                                eventing.Write(CacheActivity.VerboseOptions, new { FailedToDeleteCorruptFile = filename, Reason = e.ToString() });
                            }

                            if (secondTry)
                            {
                                counters.Fail();
                                var ret = new InconsistentCacheStateFailure(
                                    "CashHash ({0}) returned by target cache {1} for file {2} did not match CashHash ({3}) sent from source cache {4} - second try!",
                                    localAddResult.Result,
                                    m_localSession.CacheId,
                                    filename,
                                    hash,
                                    m_remoteROSession.CacheId);
                                return eventing.StopFailure(ret);
                            }

                            var validateResult = await m_remoteROSession.ValidateContentAsync(hash, urgencyHint, eventing.Id);

                            if (!validateResult.Succeeded)
                            {
                                eventing.Write(CacheActivity.VerboseOptions, new { RemediationFailed = hash, Reason = validateResult.Failure.Describe() });

                                counters.Fail();
                                return eventing.StopFailure(new InconsistentCacheStateFailure(
                                    "CashHash ({0}) returned by target cache {1} for file {2} did not match CashHash ({3}) sent from source cache {4} - cache ValidateContent failed with error: {5}",
                                    localAddResult.Result,
                                    m_localSession.CacheId,
                                    filename,
                                    hash,
                                    m_remoteROSession.CacheId,
                                    validateResult.Failure.Describe()));
                            }

                            if (validateResult.Result == ValidateContentStatus.Ok)
                            {
                                // The cache says things should be fine now, so lets try again (once)
                                secondTry = true;
                            }

                            if (validateResult.Result == ValidateContentStatus.Remediated)
                            {
                                // The cache says it remediated the issue.  This means we
                                // should try one more time but we also potentially need to
                                // do a fresh pin operation (as the prior pinned item was
                                // remediated)
                                secondTry = true;
                            }

                            // If we are not going to try again, lets return the error
                            if (!secondTry)
                            {
                                counters.Fail();
                                return eventing.StopFailure(new InconsistentCacheStateFailure(
                                    "CashHash ({0}) returned by target cache {1} for file {2} did not match CashHash ({3}) sent from source cache {4} - cache ValidateContent returned {5}",
                                    localAddResult.Result,
                                    m_localSession.CacheId,
                                    filename,
                                    hash,
                                    m_remoteROSession.CacheId,
                                    validateResult.Result.ToString()));
                            }

                            // ETW about the retry
                            eventing.Write(CacheActivity.VerboseOptions, new { Retry = hash, Reason = validateResult.Result, FromCache = m_remoteROSession.CacheId });
                        }
                    }
                    catch (Exception e)
                    {
                        counters.Fail();
                        return eventing.StopFailure(new ProduceFileFailure(CacheId, hash, filename, e));
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(strongFingerprints != null);

            using (var counter = m_sessionCounters.IncorporateRecordsCounter())
            {
                using (var eventing = new IncorporateRecordsActivity(VerticalCacheAggregator.EventSource, activityId, this))
                {
                    eventing.Start();
                    try
                    {
                        var ret = await m_localSession.IncorporateRecordsAsync(strongFingerprints);

                        return eventing.Returns(ret);
                    }
                    catch (Exception e)
                    {
                        eventing.StopException(e);
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            Contract.Requires(IsClosed);

            using (var eventing = new EnumerateSessionFingerprintsActivity(VerticalCacheAggregator.EventSource, activityId, this))
            {
                eventing.Start();
                try
                {
                    // The local always has a complete view of the fingerprints
                    // so just return the local's session fingerprints
                    var ret = m_localSession.EnumerateSessionFingerprints();

                    eventing.Stop();
                    return ret;
                }
                catch (Exception e)
                {
                    eventing.StopException(e);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId)
        {
            using (var eventing = new GetStatisticsActivity(VerticalCacheAggregator.EventSource, activityId, this))
            {
                eventing.Start();
                try
                {
                    List<CacheSessionStatistics> stats = new List<CacheSessionStatistics>();

                    // TODO:  We should have our own to report out too...
                    // Make sure to ETW them too (but only our own and only upon first generation)
                    // and add them to the list of stats
                    var maybeStats = await m_localSession.GetStatisticsAsync();
                    if (!maybeStats.Succeeded)
                    {
                        eventing.Write(FailureConstants.FailureLocal);
                        return eventing.StopFailure(maybeStats.Failure);
                    }

                    stats.AddRange(maybeStats.Result);

                    maybeStats = await m_remoteROSession.GetStatisticsAsync();
                    if (!maybeStats.Succeeded)
                    {
                        eventing.Write(FailureConstants.FailureRemote);
                        return eventing.StopFailure(maybeStats.Failure);
                    }

                    stats.AddRange(maybeStats.Result);

                    stats.Add(new CacheSessionStatistics(CacheId, m_cache.GetType().FullName, m_finalStats.Value));

                    eventing.Stop();
                    return stats.ToArray();
                }
                catch (Exception e)
                {
                    eventing.Stop(e);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            using (var eventing = new ValidateContentActivity(VerticalCacheAggregator.EventSource, activityId, this))
            {
                eventing.Start(hash, urgencyHint);

                // The Aggregator only passes this to the L1 as the outside caller never
                // gets content directly from the L2
                return eventing.Returns(await LocalSession.ValidateContentAsync(hash, urgencyHint, activityId));
            }
        }

        private Dictionary<string, double> ExportStats()
        {
            var result = new Dictionary<string, double>();
            m_sessionCounters.Export(result, prefix: null);
            return result;
        }
    }
}
