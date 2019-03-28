// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// This class is used to analyze the variation and uniqueness of the metadata of a cache over time
    /// </summary>
    public sealed class StatisticalAnalyzer
    {
        /// <summary>
        /// The cache being statistically analyzed
        /// </summary>
        private readonly ICache m_cache;

        private readonly ICacheReadOnlySession m_session;

        private int m_numSessions = 0;

        private int m_numSessionsAnalyzed = 0;

        private static bool ValidContentSize(long size)
        {
            return size >= 0;
        }

        /// <summary>
        /// The total number of sessions in the cache
        /// </summary>
        internal int NumSessions => m_numSessions;

        /// <summary>
        /// Number of sessions that were actually analyzed
        /// </summary>
        internal int NumSessionsAnalyzed => m_numSessionsAnalyzed;

        /// <summary>
        /// Constructs an object capable of performing statistical analysis on
        /// the specified cache
        /// </summary>
        /// <param name="cache">The cache to perform statistical analysis on</param>
        public StatisticalAnalyzer(ICache cache)
        {
            m_cache = cache;
            m_session = m_cache.CreateReadOnlySessionAsync().Result.Result;
        }

        /// <summary>
        /// Analyzes counts and churn of fingerprints, input lists, and optionally
        /// content sizes of sessions in the cache.
        /// </summary>
        /// <param name="sessionNameRegex">Acts as a filter for which sessions to include in the analysis.</param>
        /// <param name="analyzeContent">When true, analysis will include content sizing for each session.</param>
        /// <returns>SessionChurnInfo object for every session analyzed</returns>
        public IEnumerable<SessionChurnInfo> Analyze(Regex sessionNameRegex, bool analyzeContent)
        {
            Contract.Assume(sessionNameRegex != null);

            m_numSessions = 0;
            m_numSessionsAnalyzed = 0;

            // Used to store every unique strong fingerprint for all sessions
            HashSet<StrongFingerprint> allStrongFingerprints = new HashSet<StrongFingerprint>();

            // Used to store strong fingerprints for a given session
            HashSet<StrongFingerprint> sessionStrongFingerprints = new HashSet<StrongFingerprint>();

            HashSet<WeakFingerprintHash> allWeakFingerprints = new HashSet<WeakFingerprintHash>();

            // Used to store every unique cas element for all sessions
            HashSet<CasHash> allCasHashes = new HashSet<CasHash>();

            // Used to store every unique cas entry for all sessions
            HashSet<CasHash> allCasEntries = new HashSet<CasHash>();

            IEnumerable<Task<string>> unorderedSessionNames = m_cache.EnumerateCompletedSessions();

            IOrderedEnumerable<string> orderedSessionNames = unorderedSessionNames.Select(stringTask => stringTask.Result)
                                                                                  .Where((sessionName) =>
                                                                                  {
                                                                                      m_numSessions++;
                                                                                      if (sessionNameRegex.IsMatch(sessionName))
                                                                                      {
                                                                                          m_numSessionsAnalyzed++;
                                                                                          return true;
                                                                                      }
                                                                                      else
                                                                                      {
                                                                                          return false;
                                                                                      }
                                                                                  })
                                                                                  .OrderBy(sessionName => sessionName);

            // Dictionary of CAS entry sizes
            Dictionary<CasHash, long> contentSizeTable = new Dictionary<CasHash, long>();

            // Analyze each session in order
            foreach (string sessionName in orderedSessionNames)
            {
                Console.Error.WriteLine("Analyzing session {0}", sessionName);

                // Initialize counters for the current session
                int totalNumberStrongFingerprints = 0;
                int numberUniqueWeakFingerprints = 0;
                int numberUniqueStrongFingerprints = 0;
                int numberUniqueCasHashesOverTime = 0;
                int numberCasHashNoItemsForSession = 0;
                int contentErrors = 0;

                // Clear the set of strong fingerprints in this next session.
                sessionStrongFingerprints.Clear();

                // Contains every unique cas hash for the current session
                HashSet<CasHash> sessionCasHashes = new HashSet<CasHash>();

                IEnumerable<Task<StrongFingerprint>> strongFingerprints = m_cache.EnumerateSessionStrongFingerprints(sessionName).Result;

                // Analyze each strong fingerprint
                foreach (Task<StrongFingerprint> strongFingerprintTask in strongFingerprints.OutOfOrderTasks())
                {
                    totalNumberStrongFingerprints++;

                    StrongFingerprint strongFingerprint = strongFingerprintTask.Result;

                    sessionStrongFingerprints.Add(strongFingerprint);

                    // Check if strong fingerprint has never been seen before
                    if (allStrongFingerprints.Add(strongFingerprint))
                    {
                        // New strong fingerprint so increment counter and add to collection
                        numberUniqueStrongFingerprints++;
                    }

                    if (allWeakFingerprints.Add(strongFingerprint.WeakFingerprint))
                    {
                        numberUniqueWeakFingerprints++;
                    }

                    CasHash casElement = strongFingerprint.CasElement;

                    // Check if the cas hash is the special no item value
                    if (casElement.Equals(CasHash.NoItem))
                    {
                        numberCasHashNoItemsForSession++;
                    }

                    // Collect unique CAS elements in the session
                    sessionCasHashes.Add(casElement);

                    // Check if cas hash has never been seen before for the whole cache
                    if (allCasHashes.Add(casElement))
                    {
                        numberUniqueCasHashesOverTime++;
                    }
                }

                SessionContentInfo sessionContentInfo = null;

                if (analyzeContent)
                {
                    // Contains every unique cash entry hash for the current session
                    var sessionCasEntries = new HashSet<CasHash>();

                    // Accumulate all the CasEntries for the session
                    foreach (var task in m_session.GetCacheEntries(sessionStrongFingerprints).OutOfOrderTasks(32))
                    {
                        var possibleCasEntries = task.Result;
                        if (possibleCasEntries.Succeeded)
                        {
                            var casEntries = possibleCasEntries.Result;
                            sessionCasEntries.UnionWith(casEntries);
                        }
                        else
                        {
                            Console.Error.WriteLine("Unable to get CasEntries: {0}", possibleCasEntries.Failure.DescribeIncludingInnerFailures());
                            ++contentErrors;
                        }
                    }

                    // Retrieve the content size for each new CasEntry.
                    foreach (var task in m_session.GetContentSizes(sessionCasEntries.Except(allCasEntries)).OutOfOrderTasks(32))
                    {
                        var tuple = task.Result;
                        contentSizeTable[tuple.Item1] = tuple.Item2;
                    }

                    // Accumulate the size of content we've already seen.
                    long totalContentSize = 0;
                    long newContentSize = 0;
                    var newContentCount = 0;

                    foreach (var e in sessionCasEntries)
                    {
                        long length;

                        if (contentSizeTable.TryGetValue(e, out length))
                        {
                            if (ValidContentSize(length))
                            {
                                totalContentSize += length;

                                if (!allCasEntries.Contains(e))
                                {
                                    newContentSize += length;
                                    newContentCount += 1;
                                    allCasEntries.Add(e);
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine("Unable to find content length ({0}) for {1}", (ContentError)length, e);
                                ++contentErrors;
                            }
                        }
                        else
                        {
#pragma warning disable CA2201 // Do not raise reserved exception types
                            throw new ApplicationException($"No content length for {e}");
#pragma warning restore CA2201 // Do not raise reserved exception types
                        }
                    }

                    sessionContentInfo = new SessionContentInfo(
                        sessionCasEntries.Count,
                        totalContentSize,
                        newContentCount,
                        newContentSize,
                        contentErrors);
                }

                // Aggregate the counters and return the session's data
                SessionStrongFingerprintChurnInfo sessionStrongFingerprintChurnInfo =
                    new SessionStrongFingerprintChurnInfo(totalNumberStrongFingerprints, numberUniqueStrongFingerprints, numberUniqueWeakFingerprints);

                SessionInputListChurnInfo sessionInputListChurnInfo =
                    new SessionInputListChurnInfo(totalNumberStrongFingerprints, sessionCasHashes.Count, numberUniqueCasHashesOverTime, numberCasHashNoItemsForSession);

                yield return new SessionChurnInfo(sessionName, sessionStrongFingerprintChurnInfo, sessionInputListChurnInfo, sessionContentInfo);
            }
        }
    }
}
