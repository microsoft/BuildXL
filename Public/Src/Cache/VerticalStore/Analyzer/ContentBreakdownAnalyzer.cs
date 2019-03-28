// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// This class is used to dump content statistics per session.
    /// </summary>
    public sealed class ContentBreakdownAnalyzer
    {
        private readonly ICache m_cache;
        private readonly ICacheReadOnlySession m_session;
        private int m_numSessions = 0;
        private int m_numSessionsAnalyzed = 0;

        /// <summary>
        /// Constructs an object capable of performing statistical analysis on
        /// the specified cache
        /// </summary>
        /// <param name="cache">The cache to perform statistical analysis on</param>
        public ContentBreakdownAnalyzer(ICache cache)
        {
            m_cache = cache;
            m_session = m_cache.CreateReadOnlySessionAsync().Result.Result;
        }

        /// <summary>
        /// Analyzes a set of sessions, producing content information for each session.
        /// </summary>
        /// <param name="sessionNameRegex">Optional RegEx to filter sessions by name.</param>
        public IEnumerable<ContentBreakdownInfo> Analyze(Regex sessionNameRegex)
        {
            // TODO: This tool could be much improved by not retrieving content sizes
            //       multiple times, which would entail keeping a global-ish table
            //       of content sizes during the session enumeration.
            IEnumerable<Task<string>> sessionNames = m_cache.EnumerateCompletedSessions();
            IEnumerable<string> filteredSessionNames = sessionNames.Select(
                stringTask => stringTask.Result)
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
                });

            foreach (var sessionName in filteredSessionNames)
            {
                yield return AnalyzeSession(sessionName);
            }
        }

        private ContentBreakdownInfo AnalyzeSession(string name)
        {
            var casEntries = new HashSet<CasHash>();
            var casElements = new HashSet<CasHash>();
            var sfpSet = new HashSet<StrongFingerprint>();
            var casElementSizeTable = new Dictionary<CasHash, long>();
            var casEntrySizeTable = new Dictionary<CasHash, long>();
            int countSFP = 0;
            int contentErrors = 0;

            // Enumerate strong fingerprints for the session...
            IEnumerable<Task<StrongFingerprint>> strongFingerprints = m_cache.EnumerateSessionStrongFingerprints(name).Result;

            // ...and for each, accumulate the input lists as part of the info we'll report.
            foreach (Task<StrongFingerprint> strongFingerprintTask in strongFingerprints.OutOfOrderTasks())
            {
                ++countSFP;
                StrongFingerprint sfp = strongFingerprintTask.Result;

                // Grab the observed input list.
                CasHash casElement = sfp.CasElement;
                if (!casElement.Equals(CasHash.NoItem))
                {
                    casElements.Add(casElement);
                }

                // Remember the SFP for the content scan below, so we're not doing this enumeration twice.
                sfpSet.Add(sfp);
            }

            // Now query the content for the SFPs in the session
            foreach (var task in m_session.GetCacheEntries(sfpSet).OutOfOrderTasks())
            {
                var possibleCasEntries = task.Result;
                if (possibleCasEntries.Succeeded)
                {
                    casEntries.UnionWith(possibleCasEntries.Result);
                }
                else
                {
                    Console.Error.WriteLine("Unable to get CasEntries: {0}", possibleCasEntries.Failure.DescribeIncludingInnerFailures());
                    ++contentErrors;
                }
            }

            // With all the CAS entries in hand, get the sizes...
            foreach (var task in m_session.GetContentSizes(casEntries.Except(casEntrySizeTable.Keys)).OutOfOrderTasks())
            {
                var tuple = task.Result;
                casEntrySizeTable[tuple.Item1] = tuple.Item2;
            }

            // Get the sizes of the input lists.
            foreach (var task in m_session.GetContentSizes(casElements.Except(casElementSizeTable.Keys)).OutOfOrderTasks())
            {
                var tuple = task.Result;
                casElementSizeTable[tuple.Item1] = tuple.Item2;
            }

            return new ContentBreakdownInfo(name, casElementSizeTable, casEntrySizeTable);
        }
    }
}
