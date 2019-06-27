// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.BasicFilesystem
{
    internal sealed class BasicFilesystemCacheSession : ICacheSession, IDisposable
    {
        /// <summary>
        /// This is the actual cache underlying this session
        /// </summary>
        private readonly BasicFilesystemCache m_cache;

        // The set of session based counters for statistics
        private readonly SessionCounters m_counters = new SessionCounters();

        // Cas entries that have been pinned
        private readonly ConcurrentDictionary<CasHash, int> m_pinnedToCas = new ConcurrentDictionary<CasHash, int>();

        private readonly Lazy<Dictionary<string, double>> m_finalStats;

        private string m_sessionId;

        private readonly bool m_readOnly;

        private string m_completedFilename;

        private ConcurrentDictionary<StrongFingerprint, int> m_fingerprints;

        // Set to true when the session is closed
        private bool m_closed = false;

        // This is the file we hold onto as a lock on the session name
        // It will be null if there is no session lock (and thus no tracking)
        // We will not write to this file but only keep it open for the
        // duration of a named session.
        private FileStream m_sessionLockFile;

        // The number of cache disconnects that already happened before the session has started
        private readonly int m_cacheDisconnectCountAtSessionStart;

        internal static Possible<BasicFilesystemCacheSession, BasicFilesystemCacheSessionFailure> TryCreateBasicFilesystemCacheSession(BasicFilesystemCache cache, bool readOnly, string sessionRoot = null, string sessionId = null)
        {
            // Sessions with an ID are never read-only
            if (!string.IsNullOrEmpty(sessionId))
            {
                Contract.Requires(!readOnly);
            }

            var bfcs = new BasicFilesystemCacheSession(cache, readOnly);

            if (!string.IsNullOrEmpty(sessionRoot) && !string.IsNullOrEmpty(sessionId))
            {
                var setupResult = bfcs.SetUpSession(sessionRoot, sessionId);
                if (setupResult.Succeeded)
                {
                    return bfcs;
                }
                else
                {
                    return new BasicFilesystemCacheSessionFailure(setupResult.ErrorMessage);
                }
            }

            return bfcs;
        }

        private BasicFilesystemCacheSession(BasicFilesystemCache cache, bool readOnly)
        {
            Contract.Requires(cache != null);

            m_cache = cache;
            m_readOnly = readOnly;

            // No-item is always already here (no special pinning required)
            m_pinnedToCas.TryAdd(CasHash.NoItem, 0);

            m_finalStats = Lazy.Create(ExportStats);

            // Save the number of cache disconnects that already happened before the session has started
            m_cacheDisconnectCountAtSessionStart = m_cache.DisconnectCount;
        }

        /// <summary>
        /// Initialize resources for a named session.
        /// </summary>
        private BoolResult SetUpSession(string sessionRoot, string sessionId)
        {
            Contract.Requires(!string.IsNullOrEmpty(sessionRoot));
            Contract.Requires(!string.IsNullOrEmpty(sessionId));

            m_sessionId = sessionId;
            m_fingerprints = new ConcurrentDictionary<StrongFingerprint, int>();

            m_completedFilename = Path.Combine(sessionRoot, BasicFilesystemCache.CompletedSessionPrefix + sessionId);
            if (File.Exists(m_completedFilename))
            {
                return new BoolResult($"Duplicate Session ID: {sessionId} ({m_completedFilename} exists)");
            }

            // This will fail if the session is currently active
            // It gets automatically deleted on close as this acts as a lock.
            // UNIX: FileOptions.DeleteOnClose only enforces deletion when the stream is closed, NOT when the handle is lost. On a crash,
            //       this file will remain open. Therefore, an existence check does not help here.
            // UNIX: FileShare.None is the only value which acquires an exclusive lock.
            string inProgressFilename = Path.Combine(sessionRoot, BasicFilesystemCache.InProgressSessionPrefix + sessionId);
            try
            {
                m_sessionLockFile = new FileStream(inProgressFilename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
            }
            catch (IOException e)
            {
                return new BoolResult(e, $"Duplicate Session ID: {sessionId} ({inProgressFilename} is locked)");
            }

            m_sessionLockFile.WriteByte(1);

            // Put us into the open sessions list
            m_cache.OpenSessions.TryAdd(m_sessionId, 1);

            return BoolResult.Success;
        }

        private Dictionary<string, double> ExportStats()
        {
            var result = new Dictionary<string, double>();
            m_counters.Export(result, null);
            return result;
        }

        internal static IEnumerable<string> EnumerateCompletedSessions(string sessionRoot)
        {
            foreach (var file in Directory.EnumerateFiles(sessionRoot, BasicFilesystemCache.CompletedSessionPrefix + "*"))
            {
                // Strip out the CompletedSessionPrefix
                yield return Path.GetFileName(file).Substring(BasicFilesystemCache.CompletedSessionPrefix.Length);
            }
        }

        private void Close()
        {
            if (m_sessionLockFile != null)
            {
                int junk;
                m_cache.OpenSessions.TryRemove(m_sessionId, out junk);

                try
                {
                    m_sessionLockFile.Close();
                    m_sessionLockFile = null;
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // We don't care about the potential close errors
                    // that could happen here.  The session lock file
                    // is only used as a lock and gets deleted on close
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }

        /// <summary>
        /// Determines if the current session contains a given StrongFingerprint
        /// </summary>
        /// <param name="record">StrongFingerprint to search for.</param>
        /// <returns>True if it exists</returns>
        private bool Contains(StrongFingerprint record)
        {
            if (m_fingerprints == null)
            {
                // We don't track records so we claim they
                // are all already here
                return true;
            }

            return m_fingerprints.ContainsKey(record);
        }

        /// <summary>
        /// Indicates if the session is ReadOnly
        /// </summary>
        public bool IsReadOnly => m_readOnly;

        /// <summary>
        /// Add a record to the session if we are recording a session
        /// </summary>
        /// <param name="strong">Strong Fingerprint to add</param>
        private void AddSessionRecord(StrongFingerprint strong)
        {
            if (m_sessionLockFile != null)
            {
                // We really just need to add unique strong fingerprints
                // so if it already exists, we don't care
                Analysis.IgnoreResult(m_fingerprints.TryAdd(strong, 1));
            }
        }

        // Return true if any of the CasHashes passed in are missing
        private bool HasMissingContent(IEnumerable<CasHash> casHashes)
        {
            foreach (CasHash casHash in casHashes)
            {
                // We optimize through the pinned list just so we don't spend
                // time for an on disk check if we have it pinned
                if (!m_pinnedToCas.ContainsKey(casHash))
                {
                    if (!m_cache.CasExists(casHash))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:outputCanBeDoubleDisposed", Justification = "Tool is confused - No way to write this code without it complaining")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<CasHash> HashStreamToFileAsync(Stream filestream, string filename)
        {
            using (var output = await m_cache.ContendedOpenStreamAsync(filename, FileMode.Create, FileAccess.Write, FileShare.None, handlePendingDelete: false))
            {
                using (var hasher = ContentHashingUtilities.HashInfo.CreateContentHasher())
                {
                    using (var hashingStream = hasher.CreateReadHashingStream(filestream))
                    {
                        hashingStream.CopyTo(output);
                        return new CasHash(hashingStream.GetContentHash());
                    }
                }
            }
        }

        #region ICacheReadOnlySession methods

        public string CacheId => m_cache.CacheId;

        public string CacheSessionId => m_sessionId;

        public bool IsClosed => m_closed;

        public bool StrictMetadataCasCoupling => m_cache.StrictMetadataCasCoupling;

        public async Task<Possible<string, Failure>> CloseAsync(Guid activityId)
        {
            try
            {
                using (var counter = m_counters.CloseCounter())
                {
                    using (var eventing = new CloseActivity(BasicFilesystemCache.EventSource, activityId, this))
                    {
                        eventing.Start();

                        m_closed = true;

                        if (m_sessionLockFile != null)
                        {
                            try
                            {
                                // Write out our session data (strong fingerprints)
                                if (m_fingerprints.Count > 0)
                                {
                                    using (FileStream sessionFile = await m_cache.ContendedOpenStreamAsync(m_completedFilename, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        await BasicFilesystemCache.WriteSessionFingerprints(sessionFile, m_fingerprints.Keys);
                                        counter.SessionFingerprints(m_fingerprints.Count);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                return eventing.StopFailure(new WriteCompletedSessionFailure(m_cache.CacheId, m_completedFilename, e));
                            }
                            finally
                            {
                                Close();
                            }
                        }

                        return eventing.Returns(m_sessionId);
                    }
                }
            }
            finally
            {
                // Update the cache disconnect count - only count the disconnects that happened after the session has started
                this.m_counters.SetCacheDisconnectedCounter(m_cache.DisconnectCount - m_cacheDisconnectCountAtSessionStart);

                // We need to do the collection of statistics and logging
                // at the last point after close has completed.
                using (var eventing = new CacheActivity(BasicFilesystemCache.EventSource, CacheActivity.StatisticOptions,  activityId, "SessionStatistics", CacheId))
                {
                    eventing.Start();
                    eventing.WriteStatistics(m_finalStats.Value);
                    eventing.Stop();
                }
            }
        }

        public IEnumerable<Task<Possible<StrongFingerprint, Failure>>> EnumerateStrongFingerprints(WeakFingerprintHash weak, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);

            using (var counter = m_counters.EnumerateStrongFingerprintsCounter())
            {
                using (var eventing = new EnumerateStrongFingerprintsActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(weak, urgencyHint);

                    // It's possible the cache could encounter an IO error when attempting to enumerate the fingerprints.
                    // This shouldn't be a fatal error. we just want to catch and record it, then continue on.
                    // Due to the use of yield a foreach loop can't be used here while still handling the error,
                    // so the enumation must be done manually.
                    using (var enumerator = m_cache.EnumerateStrongFingerprints(weak).GetEnumerator())
                    {
                        while (true)
                        {
                            StrongFingerprint strong = null;
                            try
                            {
                                if (!enumerator.MoveNext())
                                {
                                    break;
                                }

                                strong = enumerator.Current;
                            }
                            catch (IOException ex)
                            {
                                eventing.StopFailure(new StrongFingerprintEnumerationFailure(m_cache.CacheId, weak, ex));
                                yield break;
                            }
                            catch (UnauthorizedAccessException ex)
                            {
                                eventing.StopFailure(new StrongFingerprintEnumerationFailure(m_cache.CacheId, weak, ex));
                                yield break;
                            }

                            counter.YieldReturn();
                            yield return Task.FromResult(new Possible<StrongFingerprint, Failure>(strong));
                        }
                    }

                    eventing.Stop();
                }
            }
        }

        public async Task<Possible<CasEntries, Failure>> GetCacheEntryAsync(StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(strong != null);

            using (var counter = m_counters.GetCacheEntryCounter())
            {
                using (var eventing = new GetCacheEntryActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(strong, urgencyHint);

                    var result = await m_cache.ReadCacheEntryAsync(strong);
                    if (result.Succeeded)
                    {
                        AddSessionRecord(strong);
                        counter.CacheHit();
                        result = result.Result.GetModifiedCasEntriesWithDeterminism(m_cache.IsAuthoritative, m_cache.CacheGuid, DateTime.UtcNow.Add(m_cache.TimeToLive));
                    }

                    return eventing.Returns(result);
                }
            }
        }

        public Task<Possible<string, Failure>> PinToCasAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);

            return Task.Run<Possible<string, Failure>>(() =>
            {
                using (var counter = m_counters.PinToCasCounter())
                {
                    using (var eventing = new PinToCasActivity(BasicFilesystemCache.EventSource, activityId, this))
                    {
                        eventing.Start(hash, urgencyHint);

                        if (!m_pinnedToCas.ContainsKey(hash))
                        {
                            if (!m_cache.CasExists(hash))
                            {
                                counter.PinMiss();
                                var result = new NoCasEntryFailure(m_cache.CacheId, hash);
                                return eventing.Returns(result);
                            }

                            if (!m_pinnedToCas.TryAdd(hash, 0))
                            {
                                counter.PinRaced();
                            }
                        }
                        else
                        {
                            counter.PinDup();
                        }

                        return eventing.Returns(m_cache.CacheId);
                    }
                }
            });
        }

        public async Task<Possible<string, Failure>[]> PinToCasAsync(CasEntries casEntries, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(casEntries.IsValid);

            using (var eventing = new PinToCasMultipleActivity(BasicFilesystemCache.EventSource, activityId, this))
            {
                eventing.Start(casEntries, urgencyHint);

                // First, initiate all of the operations
                var taskValues = new Task<Possible<string, Failure>>[casEntries.Count];
                for (int i = 0; i < casEntries.Count; i++)
                {
                    taskValues[i] = PinToCasAsync(casEntries[i], urgencyHint, activityId);
                }

                // Now await them all (since they can run in parallel
                var results = new Possible<string, Failure>[casEntries.Count];
                for (int i = 0; i < casEntries.Count; i++)
                {
                    results[i] = await taskValues[i];
                }

                // All return results are actually traced via the per-hash call of PinToCas
                return eventing.Returns(results);
            }
        }

        public async Task<Possible<string, Failure>> ProduceFileAsync(
            CasHash hash,
            string filename,
            FileState fileState,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filename != null);

            using (var counter = m_counters.ProduceFileCounter())
            {
                using (var eventing = new ProduceFileActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(hash, filename, fileState, urgencyHint);

                    if (!m_pinnedToCas.ContainsKey(hash))
                    {
                        counter.Miss();
                        return eventing.StopFailure(new UnpinnedCasEntryFailure(CacheId, hash));
                    }

                    try
                    {
                        FileUtilities.CreateDirectory(Path.GetDirectoryName(filename));
                        await m_cache.CopyFromCasAsync(hash, filename);
                        counter.FileSize(new FileInfo(filename).Length);
                    }
                    catch (Exception e)
                    {
                        counter.Fail();
                        return eventing.StopFailure(new ProduceFileFailure(CacheId, hash, filename, e));
                    }

                    return eventing.Returns(filename);
                }
            }
        }

        public async Task<Possible<Stream, Failure>> GetStreamAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);

            using (var counter = m_counters.GetStreamCounter())
            {
                using (var eventing = new GetStreamActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(hash, urgencyHint);

                    if (!m_pinnedToCas.ContainsKey(hash))
                    {
                        counter.Miss();
                        return eventing.StopFailure(new UnpinnedCasEntryFailure(CacheId, hash));
                    }

                    try
                    {
                        Stream result =
                            CasHash.NoItem.Equals(hash) ?
                            Stream.Null :
                            await m_cache.ContendedOpenStreamAsync(m_cache.ToPath(hash), FileMode.Open, FileAccess.Read, FileShare.Read, useAsync: true, handlePendingDelete: true);

                        counter.FileSize(result.Length);
                        return eventing.Returns(result);
                    }
                    catch (Exception e)
                    {
                        counter.Fail();
                        return eventing.StopFailure(new ProduceStreamFailure(CacheId, hash, e));
                    }
                }
            }
        }

        public Task<Possible<CacheSessionStatistics[], Failure>> GetStatisticsAsync(Guid activityId)
        {
            using (var eventing = new GetStatisticsActivity(BasicFilesystemCache.EventSource, activityId, this))
            {
                eventing.Start();

                CacheSessionStatistics[] result = new CacheSessionStatistics[1];
                result[0] = new CacheSessionStatistics(CacheId, m_cache.GetType().FullName, m_finalStats.Value);

                eventing.Stop();
                return Task.FromResult(new Possible<CacheSessionStatistics[], Failure>(result));
            }
        }

        public async Task<Possible<ValidateContentStatus, Failure>> ValidateContentAsync(CasHash hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);

            using (var counter = m_counters.ValidateSessionCounter())
            {
                using (var eventing = new ValidateContentActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(hash, urgencyHint);

                    if (CasHash.NoItem.Equals(hash))
                    {
                        return eventing.Returns(counter.Ok());
                    }

                    string path = m_cache.ToPath(hash);

                    try
                    {
                        // We don't use ProduceStream as this operation does not pin or cause pinning
                        // and we want to have FileShare.Delete in case we need to delete this entry
                        // due to it being corrupt.  This way there is no race as to which file is
                        // being deleted - it will be the one that was just determined to be corrupt.
                        using (Stream fileData = await m_cache.ContendedOpenStreamAsync(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, useAsync: true, handlePendingDelete: true))
                        {
                            // Size of the file
                            counter.FileSize(fileData.Length);

                            CasHash contentHash = new CasHash(await ContentHashingUtilities.HashContentStreamAsync(fileData));
                            if (contentHash.Equals(hash))
                            {
                                return eventing.Returns(counter.Ok());
                            }

                            // Remove it from pinned as it is being removed
                            int junk;
                            m_pinnedToCas.TryRemove(hash, out junk);

                            // Now, we try to remediate - This is a simple delete attempt with any error
                            // saying that we could not delete it
                            try
                            {
                                File.Delete(path);
                                eventing.Write(CacheActivity.CriticalDataOptions, new { RemovedCorruptedEntry = path });

                                return eventing.Returns(counter.Remediated());
                            }
                            catch (Exception e)
                            {
                                // Could not delete it (for what ever reason)
                                eventing.Write(CacheActivity.CriticalDataOptions, new { FailedToRemovedCorruptedEntry = path, Reason = e.Message });

                                // The file failed to be deleted, so we need to say that it is still there
                                return eventing.Returns(counter.Invalid());
                            }
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // Not found (either type) is the same as Remediated
                        return eventing.Returns(counter.Remediated());
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Not found (either type) is the same as Remediated
                        return eventing.Returns(counter.Remediated());
                    }
                    catch (Exception e)
                    {
                        // Other errors are reported as a failure to produce a stream of the data
                        return eventing.Returns(new ProduceStreamFailure(CacheId, hash, e));
                    }
                }
            }
        }

        #endregion ICacheReadOnlySession methods

        #region ICacheSession methods

        public async Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filestream != null);

            Contract.Assert(!IsReadOnly);

            // We have this interesting issue - we are not sure if the stream is rewindable
            // and the target CAS may not be local so we will end up streaming this to
            // a temporary file just to pass it up.  (Implementation detail)
            using (var counter = m_counters.AddToCasCounterStream())
            {
                using (var eventing = new AddToCasStreamActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(filestream, urgencyHint);

                    Possible<CasHash, Failure> result;

                    string tmpFile = await m_cache.CreateTempFile();

                    // Since these are longer operations that are synchronous, we wrap them
                    // into a task for potential parallelism
                    try
                    {
                        CasHash casHash = await HashStreamToFileAsync(filestream, tmpFile);

                        try
                        {
                            counter.ContentSize(new FileInfo(tmpFile).Length);

                            if (!await m_cache.AddToCasAsync(tmpFile, casHash))
                            {
                                counter.DuplicatedContent();
                            }

                            // Pin it and return the hash
                            m_pinnedToCas.TryAdd(casHash, 0);

                            result = casHash;
                        }
                        catch (Exception e)
                        {
                            counter.Failed();
                            result = new AddToCasFailure(CacheId, casHash, "<stream>", e);
                        }
                    }
                    catch (Exception e)
                    {
                        counter.Failed();
                        result = new HashFailure(CacheId, "<stream>", e);
                    }
                    finally
                    {
                        try
                        {
                            File.Delete(tmpFile);
                        }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                        catch
                        {
                            // Ignore the failure - it is likely caused by
                            // a semantic breaking tool such as most virus scanners
                            // or disk indexers.  The file was a local teporary file
                            // in the temp directory
                        }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                    }

                    return eventing.Returns(result);
                }
            }
        }

        public async Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(filename != null);
            Contract.Assert(!IsReadOnly);

            using (var counter = m_counters.AddToCasCounterFile())
            {
                using (var eventing = new AddToCasFilenameActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(filename, fileState, urgencyHint);

                    Possible<CasHash, Failure> result;

                    // First we need to do the hash of the file
                    // We do this "in place" since the CAS may be
                    // on "slow" storage and this is local
                    try
                    {
                        // We keep the file open during this such that others can't modify it
                        // until the add-to-cas has completed.  It also happens to be needed
                        // in order to compute the hash.
                        using (var fileData = await m_cache.ContendedOpenStreamAsync(filename, FileMode.Open, FileAccess.Read, FileShare.Read, handlePendingDelete: false))
                        {
                            counter.ContentSize(fileData.Length);

                            CasHash casHash = new CasHash(await ContentHashingUtilities.HashContentStreamAsync(fileData));

                            try
                            {
                                if (!await m_cache.AddToCasAsync(filename, casHash))
                                {
                                    counter.DuplicatedContent();
                                }

                                // Pin it and return the hash
                                m_pinnedToCas.TryAdd(casHash, 0);
                                result = casHash;
                            }
                            catch (Exception e)
                            {
                                counter.Failed();
                                result = new AddToCasFailure(CacheId, casHash, filename, e);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        counter.Failed();
                        result = new HashFailure(CacheId, filename, e);
                    }

                    return eventing.Returns(result);
                }
            }
        }

        public async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(hashes.IsValid);
            Contract.Assert(!IsReadOnly);

            using (var counter = m_counters.AddOrGetCounter())
            {
                using (var eventing = new AddOrGetActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start(weak, casElement, hashElement, hashes, urgencyHint);

                    counter.SetEntriesCount(hashes.Count);  // The size of what we are adding (effectively)

                    // We check the Cas entries if we are strict
                    if (StrictMetadataCasCoupling)
                    {
                        // Check that the content is valid.
                        if (!m_pinnedToCas.ContainsKey(casElement))
                        {
                            counter.Failed();
                            return eventing.StopFailure(new UnpinnedCasEntryFailure(CacheId, casElement));
                        }

                        foreach (CasHash hash in hashes)
                        {
                            if (!m_pinnedToCas.ContainsKey(hash))
                            {
                                counter.Failed();
                                return eventing.StopFailure(new UnpinnedCasEntryFailure(CacheId, hash));
                            }
                        }
                    }

                    StrongFingerprint strong = new StrongFingerprint(weak, casElement, hashElement, CacheId);

                    // Assume we accepted the Add and there is nothing to return
                    FullCacheRecord result = null;

                    string strongFingerprintName = m_cache.GetStrongFingerprintFilename(strong);

                    try
                    {
                        using (FileStream file = await m_cache.ContendedOpenStreamAsync(strongFingerprintName, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            // The compiler thinks that it is not assigned down below at the end
                            // even though it would be set by the writeEntries being true in that case
                            CasEntries oldCasEntries = hashes;

                            // Assume we will write our new enties to the file.
                            bool writeEntries = true;

                            // If there is some data in the file already, we need to try to read it.
                            if (file.Length > 0)
                            {
                                var possibleOldCasEntries = await m_cache.ReadCacheEntryAsync(file);

                                // Only if it was formatted correctly do we continue to check if
                                // we should replace it.
                                if (possibleOldCasEntries.Succeeded)
                                {
                                    oldCasEntries = possibleOldCasEntries.Result;
                                    writeEntries = false;

                                    // We can only replace if both or neither is SinglePhaseNonDeterministic
                                    if (oldCasEntries.Determinism.IsSinglePhaseNonDeterministic != hashes.Determinism.IsSinglePhaseNonDeterministic)
                                    {
                                        counter.Failed();
                                        return eventing.StopFailure(new SinglePhaseMixingFailure(CacheId));
                                    }

                                    // Should we replace?
                                    if (hashes.Determinism.IsSinglePhaseNonDeterministic ||
                                        (!oldCasEntries.Determinism.IsDeterministicTool &&
                                         (hashes.Determinism.IsDeterministic && !oldCasEntries.Determinism.Equals(hashes.Determinism))))
                                    {
                                        // We are replacing due to determinism
                                        counter.Det();
                                        writeEntries = true;
                                    }
                                    else if (HasMissingContent(oldCasEntries))
                                    {
                                        counter.Repair();
                                        writeEntries = true;
                                    }
                                    else if (oldCasEntries.Determinism.IsDeterministicTool && hashes.Determinism.IsDeterministicTool && !oldCasEntries.Equals(hashes))
                                    {
                                        // We have a non-deterministic tool!
                                        counter.Failed();
                                        return eventing.StopFailure(new NotDeterministicFailure(CacheId, new FullCacheRecord(strong, oldCasEntries), new FullCacheRecord(strong, hashes)));
                                    }
                                }
                            }

                            // Are we going to write the entries?
                            if (writeEntries)
                            {
                                // We are writing so the old entries don't count
                                oldCasEntries = hashes;

                                // Write from the front
                                file.SetLength(0);
                                await m_cache.WriteCacheEntryAsync(file, hashes);
                            }
                            else
                            {
                                counter.Dup();
                            }

                            // If what is in the cache is different than what we are
                            // asking to add, build a FullCacheRecord to return what
                            // is in the cache.
                            if (!oldCasEntries.Equals(hashes))
                            {
                                counter.Get();
                                result = new FullCacheRecord(strong, oldCasEntries);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        counter.Failed();
                        return eventing.StopFailure(new StrongFingerprintAccessFailure(m_cache.CacheId, strong, e));
                    }

                    AddSessionRecord(strong);

                    if (result != null)
                    {
                        return
                            eventing.Returns(
                                new FullCacheRecordWithDeterminism(
                                    new FullCacheRecord(
                                        result.StrongFingerprint,
                                        result.CasEntries.GetModifiedCasEntriesWithDeterminism(
                                            m_cache.IsAuthoritative,
                                            m_cache.CacheGuid,
                                            CacheDeterminism.NeverExpires))));
                    }
                    else
                    {
                        return eventing.Returns(new FullCacheRecordWithDeterminism(hashes.GetFinalDeterminism(m_cache.IsAuthoritative, m_cache.CacheGuid, DateTime.UtcNow.Add(m_cache.TimeToLive))));
                    }
                }
            }
        }

        public async Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            Contract.Requires(!IsClosed);
            Contract.Requires(strongFingerprints != null);

            Contract.Assert(!IsReadOnly);

            using (var counter = m_counters.IncorporateRecordsCounter())
            {
                using (var eventing = new IncorporateRecordsActivity(BasicFilesystemCache.EventSource, activityId, this))
                {
                    eventing.Start();
                    int count = 0;

                    if (m_sessionId != null)
                    {
                        foreach (var strongTask in strongFingerprints)
                        {
                            StrongFingerprint strong = await strongTask;

                            if (!Contains(strong))
                            {
                                // Validate that we have the strong fingerprint
                                if (!m_cache.StrongFingerprintExists(strong))
                                {
                                    // Strong fingerprint not found
                                    return eventing.StopFailure(new NoMatchingFingerprintFailure(strong));
                                }

                                // Add it to our session since we don't have it already
                                AddSessionRecord(strong);
                                counter.NewRecord();
                                count++;
                            }
                            else
                            {
                                counter.DupRecord();
                            }
                        }
                    }

                    return eventing.Returns(count);
                }
            }
        }

        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            Contract.Requires(IsClosed);
            using (var eventing = new EnumerateSessionFingerprintsActivity(BasicFilesystemCache.EventSource, activityId, this))
            {
                eventing.Start();

                if (m_fingerprints != null)
                {
                    foreach (StrongFingerprint strong in m_fingerprints.Keys)
                    {
                        yield return Task.FromResult(strong);
                    }
                }

                eventing.Stop();
            }
        }

        #endregion ICacheSession methods

        #region IDisposable Methods

        /// <summary>
        /// This code added to correctly implement the disposable pattern.
        /// </summary>
        public void Dispose()
        {
            // We are disposing rather than closing
            // so we will blow away any fingerprints
            // we had (if any)  This is an unclean shutdown
            if (m_fingerprints != null)
            {
                m_fingerprints.Clear();
            }

            Close();
        }

        #endregion IDisposable Methods
    }
}
