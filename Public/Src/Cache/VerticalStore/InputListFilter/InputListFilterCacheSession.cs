// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;

namespace BuildXL.Cache.InputListFilter
{
    internal sealed class InputListFilterCacheSession : InputListFilterReadOnlyCacheSession, ICacheSession
    {
        private readonly ICacheSession m_session;

        internal InputListFilterCacheSession(ICacheSession session, InputListFilterCache cache)
            : base(session, cache)
        {
            m_session = session;
        }

        /// <summary>
        /// Check the input list against the regex
        /// </summary>
        /// <param name="weak">The weak fingerprint (for logging on failure)</param>
        /// <param name="casElement">The CasElement of the strong fingerprint</param>
        /// <param name="hashElement">The hashElement of the strong fingerprint (for logging on failure)</param>
        /// <param name="urgencyHint">Pass-through</param>
        /// <param name="activityId">Pass-through activityId</param>
        /// <returns>false if the check was not performed, true if the checks were performed, failure if the regex checks failed</returns>
        /// <remarks>
        /// This will attempt to validate the CAS stored input list against the regex rules
        /// </remarks>
        private async Task<Possible<bool, Failure>> CheckInputList(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, UrgencyHint urgencyHint, Guid activityId)
        {
            // If we either have no CasHash item or we have no regex to check, just return false
            // (that we did nothing)
            if (casElement.Equals(CasHash.NoItem) || ((Cache.MustIncludeRegex == null) && (Cache.MustNotIncludeRegex == null)))
            {
                return false;
            }

            // mustInclude start out false if we need to check for mustInclude
            // Once we get a mustInclude match we no longer need to check.
            // If we have no mustInclude regex, we set it to true such that
            // we don't bother checking it
            bool mustInclude = Cache.MustIncludeRegex == null;

            // This is just to make a faster check for the MustNotinclude
            // case.  If we have the regex then we must check each entry
            // but in many cases we don't have the regex so let this be a quick out.
            bool checkMustNot = Cache.MustNotIncludeRegex != null;

            // Try to get the observed inputs from the CasHash given
            var possibleStream = await GetStreamAsync(casElement, urgencyHint, activityId);
            if (!possibleStream.Succeeded)
            {
                // If we could not get a stream to the CasEntery in the fingerprint.
                return new InputListFilterFailure(Cache.CacheId, weak, casElement, hashElement, "Failed to get stream of CasElement");
            }

            // Deserialize the contents of the path set.
            using (possibleStream.Result)
            {
                PathTable pathTable = new PathTable();
                BuildXLReader reader = new BuildXLReader(false, possibleStream.Result, true);
                var maybePathSet = ObservedPathSet.TryDeserialize(pathTable, reader);
                if (maybePathSet.Succeeded)
                {
                    // Deserialization was successful
                    foreach (ObservedPathEntry entry in maybePathSet.Result.Paths)
                    {
                        string filepath = entry.Path.ToString(pathTable);

                        // Have we seen a must-have entry yet?  If not check if this is one
                        // that way once we found one we want we stop checking this regex
                        if (!mustInclude)
                        {
                            mustInclude = Cache.MustIncludeRegex.IsMatch(filepath);
                        }

                        // Now, if we are looking for a must not include, we just check for that
                        // and if it matches we fail
                        if (checkMustNot)
                        {
                            if (Cache.MustNotIncludeRegex.IsMatch(filepath))
                            {
                                return new InputListFilterFailure(Cache.CacheId, weak, casElement, hashElement, string.Format(CultureInfo.InvariantCulture, "Failed due to a MustNotInclude file: {0}", filepath));
                            }
                        }
                    }
                }
                else
                {
                    return new InputListFilterFailure(Cache.CacheId, weak, casElement, hashElement, "Failed to deserialize observed inputs");
                }
            }

            if (!mustInclude)
            {
                return new InputListFilterFailure(Cache.CacheId, weak, casElement, hashElement, "Failed due to not including at least one MustInclude file");
            }

            return true;
        }

        public async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddOrGetAsync(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId)
        {
            var check = await CheckInputList(weak, casElement, hashElement, urgencyHint, activityId);
            if (!check.Succeeded)
            {
                return check.Failure;
            }

            return await m_session.AddOrGetAsync(weak, casElement, hashElement, hashes, urgencyHint, activityId);
        }

        public Task<Possible<CasHash, Failure>> AddToCasAsync(Stream filestream, CasHash? hash, UrgencyHint urgencyHint, Guid activityId)
        {
            return m_session.AddToCasAsync(filestream, hash, urgencyHint, activityId);
        }

        public Task<Possible<CasHash, Failure>> AddToCasAsync(
            string filename,
            FileState fileState,
            CasHash? hash,
            UrgencyHint urgencyHint,
            Guid activityId)
        {
            return m_session.AddToCasAsync(filename, fileState, hash, urgencyHint, activityId);
        }

        public IEnumerable<Task<StrongFingerprint>> EnumerateSessionFingerprints(Guid activityId)
        {
            return m_session.EnumerateSessionFingerprints(activityId);
        }

        public Task<Possible<int, Failure>> IncorporateRecordsAsync(IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId)
        {
            return m_session.IncorporateRecordsAsync(strongFingerprints, activityId);
        }
    }
}
