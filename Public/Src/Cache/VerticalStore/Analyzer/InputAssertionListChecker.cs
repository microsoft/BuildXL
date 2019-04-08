// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Checks for significant changes in the size of input assertion lists for
    /// the same weak fingerprint.
    /// </summary>
    /// <remarks>
    /// This class was not designed with the intention of having multiple
    /// checks performed at the same time with the same instance. If this is
    /// desired, multiple instances of this class should be created and each
    /// instance perform a single check.
    /// </remarks>
    public sealed class InputAssertionListChecker
    {
        /// <summary>
        /// The cache being checked
        /// </summary>
        private readonly ICache m_cache;

        /// <summary>
        /// A read only session to the cache being checked
        /// </summary>
        private readonly ICacheReadOnlySession m_readOnlySession;

        /// <summary>
        /// Default value for the min input assertion list size disparity
        /// factor
        /// </summary>
        public const double DefaultDisparityFactor = 2;

        /// <summary>
        /// The minumum factor of difference between the lengths of input
        /// assertion lists that consitutes an anomaly
        /// </summary>
        private readonly double m_minDisparityFactor = DefaultDisparityFactor;

        private int m_numSessions = 0;

        private int m_numSessionsChecked = 0;

        private int m_numInputListsChecked = 0;

        private int m_numWeakFingerprintsChecked = 0;

        private int m_numWeakFingerprintsWithTwoOrMoreSFPs = 0;

        /// <summary>
        /// Total numbers of session in cache being checked. This value is
        /// only valid if the check was done through the sessions.
        /// </summary>
        internal int NumSessions => m_numSessions;

        /// <summary>
        /// Number of input lists checked
        /// </summary>
        internal int NumInputListsChecked => m_numInputListsChecked;

        /// <summary>
        /// This is the number of sessions that were enumerated for their
        /// strong fingerprints.
        /// </summary>
        internal int NumSessionsChecked => m_numSessionsChecked;

        /// <summary>
        /// This is the total number of weak fingerprints checked.
        /// </summary>
        /// <remarks>
        /// If the user provided a list of weak fingerprints, this number will
        /// match the size of that list. If the check was done through the
        /// sessions, this number will reflect the number of unique weak
        /// fingerprints found within the sessions that matched the specified
        /// session filter.
        /// </remarks>
        internal int NumWeakFingerprintsChecked => m_numWeakFingerprintsChecked;

        /// <summary>
        /// This number reflects how many of the weak fingerprints checked have
        /// two or more strong fingerprints associated with them.
        /// </summary>
        internal int NumWeakFingerprintsWithTwoOrMoreSFPs => m_numWeakFingerprintsWithTwoOrMoreSFPs;

        /// <summary>
        /// A dictionary of the hashes for all input assertion lists pointing
        /// to their respective file lengths
        /// </summary>
        private ConcurrentDictionary<CasHash, long> m_inputAssertionListLengths =
            new ConcurrentDictionary<CasHash, long>();

        /// <summary>
        /// A dictionary of sessions to strong fingerprints
        /// </summary>
        private readonly ConcurrentDictionary<string, HashSet<StrongFingerprint>> m_sessionMap =
            new ConcurrentDictionary<string, HashSet<StrongFingerprint>>();

        /// <summary>
        /// Creates a read only session for the cache to check
        /// </summary>
        /// <param name="cache">The cache to check for anomalies</param>
        /// <param name="minDisparityFactor">This is the minimum factor of
        /// disparity in the size of input assertion list files that consitutes
        /// an anonaly</param>
        public InputAssertionListChecker(ICache cache, double minDisparityFactor = DefaultDisparityFactor)
        {
            Contract.Requires(cache != null);
            Contract.Requires(minDisparityFactor > 1.0);

            m_cache = cache;
            m_readOnlySession = m_cache.CreateReadOnlySessionAsync().Result.Result;
            m_minDisparityFactor = minDisparityFactor;
        }

        // Our pathtable for doing input lists
        private static readonly PathTable s_pathTable = new PathTable();

        /// <summary>
        /// Takes a strong fingerprint and returns the deserialized contents of
        /// the input assertion list file that corresponds to it
        /// </summary>
        /// <param name="sfp">The strong fingerprint to get the input assertion
        /// list file contents for</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <returns>Deserialized contents of the input assertion list file
        /// corresponding to the specified strong fingerprint</returns>
        private async Task<string> GetInputAssertionListFileContentsAsync(StrongFingerprint sfp, ConcurrentDictionary<CacheError, int> cacheErrors)
        {
            // Check for the NoItem
            if (sfp.CasElement.Equals(CasHash.NoItem))
            {
                return string.Empty;
            }

            // Pin the input assertion list file
            Possible<string, Failure> possibleString = await m_readOnlySession.PinToCasAsync(sfp.CasElement).ConfigureAwait(false);
            if (!possibleString.Succeeded)
            {
                return string.Empty;
            }

            // Get the stream for the input assertion list file
            Possible<Stream, Failure> possibleStream = await m_readOnlySession.GetStreamAsync(sfp.CasElement).ConfigureAwait(false);
            if (!possibleStream.Succeeded)
            {
                cacheErrors.TryAdd(
                    new CacheError(
                    CacheErrorType.CasHashError,
                    "The input assertion list for SFP " + sfp.ToString() + " was not found in CAS"), 0);
                return string.Empty;
            }

            // Read the stream contents while hashing
            return await Task.Run(() =>
            {
                using (var hasher = ContentHashingUtilities.HashInfo.CreateContentHasher())
                {
                    using (var hashingStream = hasher.CreateReadHashingStream(possibleStream.Result))
                    {
                        using (var reader = new BuildXLReader(false, hashingStream, false))
                        {
                            var maybePathSet = ObservedPathSet.TryDeserialize(s_pathTable, reader);

                            // Check if deserialization was successful
                            if (!maybePathSet.Succeeded)
                            {
                                // Deserialization failed
                                cacheErrors.TryAdd(
                                    new CacheError(
                                    CacheErrorType.CasHashError,
                                    "The input assertion list for SFP " + sfp.ToString() + " could not be deserialized"), 0);
                                return string.Empty;
                            }

                            CasHash newCasHash = new CasHash(hashingStream.GetContentHash());

                            // Check if the hashes match
                            if (!sfp.CasElement.Equals(newCasHash))
                            {
                                cacheErrors.TryAdd(
                                    new CacheError(
                                    CacheErrorType.CasHashError,
                                    "The input assertion list for SFP " + sfp.ToString() + " has been altered in the CAS"), 0);
                                return string.Empty;
                            }

                            // Deserialization was successful and file was unaltered
                            StringBuilder fileContents = new StringBuilder();
                            foreach (ObservedPathEntry entry in maybePathSet.Result.Paths)
                            {
                                fileContents.Append(entry.Path.ToString(s_pathTable)).Append(Environment.NewLine);
                            }

                            return fileContents.ToString();
                        }
                    }
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Iterates over a collection of strong fingerprints, finds the length
        /// of the input assertion list file for each, and stores each length.
        /// </summary>
        /// <param name="strongFingerprints">The strong fingerprints to get
        /// input assertion list file lengths for</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        private async Task StoreInputAssertionListFileLengths(ICollection<StrongFingerprint> strongFingerprints, ConcurrentDictionary<CacheError, int> cacheErrors)
        {
            foreach (var strongFingerprint in strongFingerprints)
            {
                if (!m_inputAssertionListLengths.ContainsKey(strongFingerprint.CasElement))
                {
                    Possible<string, Failure> possibleString = await m_readOnlySession.PinToCasAsync(strongFingerprint.CasElement).ConfigureAwait(false);
                    if (possibleString.Succeeded)
                    {
                        Possible<Stream, Failure> possibleStream = await m_readOnlySession.GetStreamAsync(strongFingerprint.CasElement).ConfigureAwait(false);
                        if (possibleStream.Succeeded)
                        {
                            using (Stream stream = possibleStream.Result)
                            {
                                // Get the file length
                                long fileLength = (await new StreamReader(stream).ReadToEndAsync()).Length;
                                m_inputAssertionListLengths.TryAdd(strongFingerprint.CasElement, fileLength);
                            }
                        }
                        else
                        {
                            // This is an error because the pin operation succeeded but getting the stream failed
                            cacheErrors.TryAdd(
                                new CacheError(
                                CacheErrorType.CasHashError,
                                "The input assertion list for SFP " + strongFingerprint.ToString() + " was not found in CAS"), 0);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks a group of strong fingerprints for an input assertion list
        /// anomaly where each strong fingerprint has the same weak
        /// fingerprint as all the rest.
        /// </summary>
        /// <param name="strongFingerprints">The strong fingerprints to check</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <returns>An InputAssertionListAnomaly object if an anomaly was found else null</returns>
        private async Task<InputAssertionListAnomaly> CheckWeakFingerprintForAnomalyAsync(ICollection<StrongFingerprint> strongFingerprints, ConcurrentDictionary<CacheError, int> cacheErrors)
        {
            Interlocked.Increment(ref m_numWeakFingerprintsChecked);

            // There must be 2 or more strong fingerprints to make a meaningful comparison
            if (strongFingerprints.Count < 2)
            {
                // There is no comparison to make so we skip this weak fingerprint
                return null;
            }
            else
            {
                Interlocked.Increment(ref m_numWeakFingerprintsWithTwoOrMoreSFPs);
            }

            // Determine and store input assertion list length for each strong fingerprint
            await StoreInputAssertionListFileLengths(strongFingerprints, cacheErrors).ConfigureAwait(false);

            // Find the strong fingerprint with the smallest and largest input assertion list file sizes
            StrongFingerprint smallestInputListFileStrongFingerprint = null;
            long smallestInputListFileLength = long.MaxValue;
            StrongFingerprint largestInputListFileStrongFingerprint = null;
            long largestInputListFileLength = long.MinValue;
            foreach (var strongFingerprint in strongFingerprints)
            {
                long inputAssertionListFileLength;
                if (m_inputAssertionListLengths.TryGetValue(strongFingerprint.CasElement, out inputAssertionListFileLength))
                {
                    if (inputAssertionListFileLength < smallestInputListFileLength)
                    {
                        smallestInputListFileLength = inputAssertionListFileLength;
                        smallestInputListFileStrongFingerprint = strongFingerprint;
                    }

                    if (inputAssertionListFileLength > largestInputListFileLength)
                    {
                        largestInputListFileLength = inputAssertionListFileLength;
                        largestInputListFileStrongFingerprint = strongFingerprint;
                    }
                }
            }

            // Check for an anomaly in the file sizes
            if ((smallestInputListFileLength * m_minDisparityFactor) < largestInputListFileLength)
            {
                // Anomaly found so get contents of files
                string smallestFileContents = await GetInputAssertionListFileContentsAsync(smallestInputListFileStrongFingerprint, cacheErrors).ConfigureAwait(false);
                string largestFileContents = await GetInputAssertionListFileContentsAsync(largestInputListFileStrongFingerprint, cacheErrors).ConfigureAwait(false);

                return new InputAssertionListAnomaly(smallestInputListFileStrongFingerprint, smallestFileContents, largestInputListFileStrongFingerprint, largestFileContents);
            }

            return null;
        }

        /// <summary>
        /// Checks weak fingerprints for input assertion list anomalies
        /// </summary>
        /// <param name="weakFingerprints">The weak fingerprints to check</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <returns>All anomalies found. NOTE Many results will be null, MUST check for nulls in return values</returns>
        private IEnumerable<Task<InputAssertionListAnomaly>> CheckWeakFingerprints(IEnumerable<WeakFingerprintHash> weakFingerprints, ConcurrentDictionary<CacheError, int> cacheErrors)
        {
            foreach (WeakFingerprintHash weakFingerprint in weakFingerprints)
            {
                yield return Task.Run(() =>
                {
                    // Enumerate strong fingerprints from weak fingerprint
                    IEnumerable<Task<Possible<StrongFingerprint, Failure>>> strongFingerprints = m_readOnlySession.EnumerateStrongFingerprints(weakFingerprint);

                    ConcurrentDictionary<StrongFingerprint, int> strongFingerprintSet = new ConcurrentDictionary<StrongFingerprint, int>();
                    foreach (Task<Possible<StrongFingerprint, Failure>> possibleStrongFingerprintTask in strongFingerprints.OutOfOrderTasks())
                    {
                        Possible<StrongFingerprint, Failure> possibleStrongFingerprint = possibleStrongFingerprintTask.Result;
                        if (!possibleStrongFingerprint.Succeeded)
                        {
                            continue;
                        }
                        StrongFingerprint strongFingerprint = possibleStrongFingerprint.Result;
                        strongFingerprintSet.TryAdd(strongFingerprint, 0);
                    }

                    // Check for anomaly
                    return CheckWeakFingerprintForAnomalyAsync(strongFingerprintSet.Keys, cacheErrors);
                });
            }
        }

        private async Task<HashSet<StrongFingerprint>> GetSessionStrongFingerprintsAsync(string sessionId)
        {
            try
            {
                HashSet<StrongFingerprint> strongFingerprints = new HashSet<StrongFingerprint>();
                if (m_sessionMap.TryAdd(sessionId, strongFingerprints))
                {
                    var possibleStrongFingerprintTasks = m_cache.EnumerateSessionStrongFingerprints(sessionId);
                    if (possibleStrongFingerprintTasks.Succeeded)
                    {
                        foreach (Task<StrongFingerprint> strongFingerprintTask in possibleStrongFingerprintTasks.Result)
                        {
                            strongFingerprints.Add(await strongFingerprintTask);
                        }

                        return strongFingerprints;
                    }
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // We ignore sessions we can't read - nothing to do here
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            return null;
        }

        /// <summary>
        /// Get an enumeration of HashSets of StrongFingerprints from the enumeration of session IDs
        /// </summary>
        /// <param name="enumeratedCompletedSessions">The sessions to load strong fingerprints from</param>
        /// <param name="sessionRegex">Filters which sessions to load strong fingerprints from</param>
        /// <returns>Strong fingerprints enumerated from given sessions</returns>
        private IEnumerable<Task<HashSet<StrongFingerprint>>> GetSessionStrongFingerprints(IEnumerable<Task<string>> enumeratedCompletedSessions, Regex sessionRegex)
        {
            foreach (var sessionIdTask in enumeratedCompletedSessions.OutOfOrderTasks())
            {
                if (!sessionIdTask.IsFaulted)
                {
                    Interlocked.Increment(ref m_numSessions);
                    string sessionId = sessionIdTask.Result;
                    if (sessionRegex.IsMatch(sessionId))
                    {
                        yield return GetSessionStrongFingerprintsAsync(sessionIdTask.Result);
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates strong fingerprints from sessions
        /// </summary>
        /// <param name="enumeratedCompletedSessions">Sessions to enumerate
        /// strong fingerprints from</param>
        /// <param name="sessionRegex">Filters which sessions are used to
        /// enumerate</param>
        /// <param name="weakFingerprintsFound">If not null, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>Strong fingerprints enumerated from given sessions</returns>
        private IEnumerable<StrongFingerprint> ProduceStrongFingerprints(
            IEnumerable<Task<string>> enumeratedCompletedSessions,
            Regex sessionRegex,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound)
        {
            foreach (var fingerprintsTask in GetSessionStrongFingerprints(enumeratedCompletedSessions, sessionRegex).OutOfOrderTasks())
            {
                if (!fingerprintsTask.IsFaulted && (fingerprintsTask.Result != null))
                {
                    Interlocked.Increment(ref m_numSessionsChecked);
                    foreach (var fingerprint in fingerprintsTask.Result)
                    {
                        if (weakFingerprintsFound != null)
                        {
                            weakFingerprintsFound.TryAdd(fingerprint.WeakFingerprint, 0);
                        }

                        yield return fingerprint;
                    }
                }
            }
        }

        /// <summary>
        /// Checks for anomalies via a collection of weak fingerprints.
        /// </summary>
        /// <param name="weakFingerprints">Weak fingerprints to check with</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>All anomalies found</returns>
        public IEnumerable<InputAssertionListAnomaly> PerformAnomalyCheck(IEnumerable<WeakFingerprintHash> weakFingerprints, ConcurrentDictionary<CacheError, int> cacheErrors, ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Requires(cacheErrors != null);
            m_numWeakFingerprintsChecked = 0;
            m_numWeakFingerprintsWithTwoOrMoreSFPs = 0;

            if (weakFingerprintsFound != null)
            {
                weakFingerprints = weakFingerprints.AddWeakFingerprintsToSet(weakFingerprintsFound);
            }

            IEnumerable<Task<InputAssertionListAnomaly>> inputAssertionAnomalies = CheckWeakFingerprints(weakFingerprints, cacheErrors);

            foreach (var anomaly in inputAssertionAnomalies.OutOfOrderTasks())
            {
                if (anomaly.Result != null)
                {
                    yield return anomaly.Result;
                }
            }
        }

        /// <summary>
        /// Checks for anomalies via the sessions. This method only works on
        /// caches that suport enumerating sessions.
        /// </summary>
        /// <param name="sessionRegex">Filters which sessions are checked</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>All anomalies found</returns>
        public IEnumerable<InputAssertionListAnomaly> PerformAnomalyCheck(
            Regex sessionRegex,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Requires(sessionRegex != null);
            Contract.Requires(cacheErrors != null);

            IEnumerable<Task<string>> enumeratedCompletedSessions = m_cache.EnumerateCompletedSessions();

            IEnumerable<StrongFingerprint> strongFingerprints = ProduceStrongFingerprints(enumeratedCompletedSessions, sessionRegex, weakFingerprintsFound);

            IEnumerable<WeakFingerprintHash> weakFingerprints = strongFingerprints.ProduceWeakFingerprints();

            return PerformAnomalyCheck(weakFingerprints, cacheErrors, weakFingerprintsFound);
        }

        // Returns null if there it does not match, otherwise returns the InputAssertionList object
        private async Task<InputAssertionList> GetInputMatchingInputAssertions(
            StrongFingerprint strongFingerprint,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            Func<string, bool> inputListFilter)
        {
            string inputAssertionListFileContents = await GetInputAssertionListFileContentsAsync(strongFingerprint, cacheErrors).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(inputAssertionListFileContents))
            {
                Interlocked.Increment(ref m_numInputListsChecked);
                if (inputListFilter(inputAssertionListFileContents))
                {
                    return new InputAssertionList(strongFingerprint, inputAssertionListFileContents);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the input assertion list of every input strong fingerprint
        /// </summary>
        /// <param name="strongFingerprints">Strong fingerprints to find input
        /// assertion lists for</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="inputListFilter">The filter function that will select if
        /// the given input list is interesting.  The function is passed a
        /// string that contains the paths in the input list and returns true
        /// if the input list is interesting.</param>
        /// <param name="weakFingerprintsFound">If not null, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>The input assertion list of every input strong fingerprint</returns>
        private IEnumerable<Task<InputAssertionList>> GenerateInputAssertionLists(
            IEnumerable<StrongFingerprint> strongFingerprints,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            Func<string, bool> inputListFilter,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound)
        {
            HashSet<CasHash> alreadyCompleted = new HashSet<CasHash>();
            foreach (StrongFingerprint strongFingerprint in strongFingerprints)
            {
                if (weakFingerprintsFound != null)
                {
                    weakFingerprintsFound.TryAdd(strongFingerprint.WeakFingerprint, 0);
                }

                if (alreadyCompleted.Add(strongFingerprint.CasElement))
                {
                    yield return GetInputMatchingInputAssertions(strongFingerprint, cacheErrors, inputListFilter);
                }
            }
        }

        /// <summary>
        /// Returns the input assertion list of every input strong fingerprint
        /// </summary>
        /// <param name="strongFingerprints">Strong fingerprints to find input
        /// assertion lists for</param>
        /// <param name="inputListFilter">The filter function that will select if
        /// the given input list is interesting.  The function is passed a
        /// string that contains the paths in the input list and returns true
        /// if the input list is interesting.</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>The input assertion list of every input strong fingerprint</returns>
        public IEnumerable<InputAssertionList> GetInputAssertionLists(
            IEnumerable<StrongFingerprint> strongFingerprints,
            Func<string, bool> inputListFilter,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Requires(cacheErrors != null);
            Contract.Requires(inputListFilter != null);

            IEnumerable<Task<InputAssertionList>> inputAssertionListTasks = GenerateInputAssertionLists(strongFingerprints, cacheErrors, inputListFilter, weakFingerprintsFound);

            foreach (Task<InputAssertionList> inputAssertionListTask in inputAssertionListTasks.OutOfOrderTasks(64))
            {
                // Only turn those that are non-null.
                if (inputAssertionListTask.Result != null)
                {
                    yield return inputAssertionListTask.Result;
                }
            }
        }

        /// <summary>
        /// Returns the input assertion list of every strong fingerprint that
        /// can be enumerated from the input weak fingerprints
        /// </summary>
        /// <param name="weakFingerprints">Weak fingerprints to enunerate
        /// strong fingerprints from</param>
        /// <param name="inputListFilter">The filter function that will select if
        /// the given input list is interesting.  The function is passed a
        /// string that contains the paths in the input list and returns true
        /// if the input list is interesting.</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>The input assertion lists that the filter function liked
        /// of every strong fingerprint that can be enumerated from the input
        /// weak fingerprints</returns>
        public IEnumerable<InputAssertionList> GetSuspectInputAssertionLists(
            IEnumerable<WeakFingerprintHash> weakFingerprints,
            Func<string, bool> inputListFilter,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Requires(cacheErrors != null);
            Contract.Requires(inputListFilter != null);

            return GetInputAssertionLists(weakFingerprints.ProduceStrongFingerprints(m_readOnlySession), inputListFilter, cacheErrors, weakFingerprintsFound);
        }

        /// <summary>
        /// Returns the input assertion list of every strong fingerprint that
        /// can be enumerated from the filtered sessions
        /// </summary>
        /// <param name="sessionRegex">Filters which sessions are used</param>
        /// <param name="inputListFilter">The filter function that will select if
        /// the given input list is interesting.  The function is passed a
        /// string that contains the paths in the input list and returns true
        /// if the input list is interesting.</param>
        /// <param name="cacheErrors">Any cache errors that are found will be
        /// added to this collection</param>
        /// <param name="weakFingerprintsFound">If specified, all weak
        /// fingerprints found will be added to this collection</param>
        /// <returns>The input assertion list of every strong fingerprint that
        /// can be enumerated from the input weak fingerprints</returns>
        public IEnumerable<InputAssertionList> GetSuspectInputAssertionLists(
            Regex sessionRegex,
            Func<string, bool> inputListFilter,
            ConcurrentDictionary<CacheError, int> cacheErrors,
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFound = null)
        {
            Contract.Requires(sessionRegex != null);
            Contract.Requires(cacheErrors != null);
            Contract.Requires(inputListFilter != null);

            m_numSessions = 0;
            m_numSessionsChecked = 0;

            IEnumerable<StrongFingerprint> strongFingerprints = ProduceStrongFingerprints(m_cache.EnumerateCompletedSessions(), sessionRegex, weakFingerprintsFound);

            return GetInputAssertionLists(strongFingerprints, inputListFilter, cacheErrors);
        }

        /// <summary>
        /// Return the set of sessions that contain the given strong fingerprint
        /// </summary>
        /// <param name="strongFingerprint">The strong fingerprint in question</param>
        /// <returns>
        /// An enumeration of the sessions that contain the strong fingerprint
        /// </returns>
        public IEnumerable<string> GetSessionsWithFingerprint(StrongFingerprint strongFingerprint)
        {
            foreach (var kv in m_sessionMap)
            {
                if (kv.Value.Contains(strongFingerprint))
                {
                    yield return kv.Key;
                }
            }
        }
    }
}
