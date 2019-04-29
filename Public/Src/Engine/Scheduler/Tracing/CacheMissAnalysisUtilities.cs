// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Engine.Cache.Serialization;
using static BuildXL.Scheduler.Tracing.FingerprintStoreReader;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Cache miss analysis result
    /// </summary>
    public enum CacheMissAnalysisResult
    {
        /// <nodoc/>
        Invalid,

        /// <nodoc/>
        MissingFromOldBuild,

        /// <nodoc/>
        MissingFromNewBuild,

        /// <nodoc/>
        WeakFingerprintMismatch,

        /// <nodoc/>
        StrongFingerprintMismatch,

        /// <nodoc/>
        UncacheablePip,

        /// <nodoc/>
        DataMiss,

        /// <nodoc/>
        InvalidDescriptors,

        /// <nodoc/>
        ArtificialMiss,

        /// <nodoc/>
        NoMiss
    }

    /// <summary>
    /// Cache miss analysis methods used by both on-the-fly and execution log analyzer
    /// </summary>
    public static class CacheMissAnalysisUtilities
    {
        /// <summary>
        /// Analyzes the cache miss for a specific pip.
        /// </summary>
        public static CacheMissAnalysisResult AnalyzeCacheMiss(
            TextWriter writer,
            PipCacheMissInfo missInfo,
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc)
        {
            Contract.Requires(oldSessionFunc != null);
            Contract.Requires(newSessionFunc != null);

            WriteLine($"Cache miss type: {missInfo.CacheMissType}", writer);
            WriteLine(string.Empty, writer);

            switch (missInfo.CacheMissType)
            {
                // Fingerprint miss
                case PipCacheMissType.MissForDescriptorsDueToWeakFingerprints:
                case PipCacheMissType.MissForDescriptorsDueToStrongFingerprints:
                    // Compute the pip unique output hash to use as the primary lookup key for fingerprint store entries
                    return AnalyzeFingerprints(oldSessionFunc, newSessionFunc, writer);

                // We had a weak and strong fingerprint match, but couldn't retrieve correct data from the cache
                case PipCacheMissType.MissForCacheEntry:
                case PipCacheMissType.MissForProcessMetadata:
                case PipCacheMissType.MissForProcessMetadataFromHistoricMetadata:
                case PipCacheMissType.MissForProcessOutputContent:
                    WriteLine($"Data missing from the cache.", writer);
                    return CacheMissAnalysisResult.DataMiss;

                case PipCacheMissType.MissDueToInvalidDescriptors:
                    WriteLine($"Cache returned invalid data.", writer);
                    return CacheMissAnalysisResult.InvalidDescriptors;

                case PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions:
                    WriteLine($"Cache miss artificially forced by user.", writer);
                    return CacheMissAnalysisResult.ArtificialMiss;

                case PipCacheMissType.Invalid:
                    WriteLine($"Unexpected condition! No valid changes or cache issues were detected to cause process execution, but a process still executed.", writer);
                    return CacheMissAnalysisResult.Invalid;

                case PipCacheMissType.Hit:
                    WriteLine($"Pip was a cache hit.", writer);
                    return CacheMissAnalysisResult.NoMiss;

                default:
                    WriteLine($"Unexpected condition! Unknown cache miss type.", writer);
                    return CacheMissAnalysisResult.Invalid;
            }
        }

        private static CacheMissAnalysisResult AnalyzeFingerprints(
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc,
            TextWriter writer)
        {
            var result = CacheMissAnalysisResult.Invalid;

            // While a PipRecordingSession is in scope, any pip information retrieved from the fingerprint store is
            // automatically written out to per-pip files.
            using (var oldPipSession = oldSessionFunc())
            using (var newPipSession = newSessionFunc())
            {
                bool missingPipEntry = false;
                if (!oldPipSession.EntryExists)
                {
                    WriteLine("No fingerprint computation data found from old build.", writer, oldPipSession.PipWriter);
                    WriteLine("This may be the first execution where pip outputs were stored to the cache.", writer, oldPipSession.PipWriter);

                    // Write to just the old pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, oldPipSession.PipWriter);
                    missingPipEntry = true;
                    result = CacheMissAnalysisResult.MissingFromOldBuild;

                    WriteLine(string.Empty, writer, oldPipSession.PipWriter);
                }

                if (!newPipSession.EntryExists)
                {
                    // Cases:
                    // 1. ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations
                    // 2. ScheduleProcessNotStoredDueToMissingOutputs
                    // 3. ScheduleProcessNotStoredToWarningsUnderWarnAsError
                    // 4. ScheduleProcessNotStoredToCacheDueToInherentUncacheability
                    WriteLine("No fingerprint computation data found from new build.", writer, newPipSession.PipWriter);

                    // Write to just the new pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, newPipSession.PipWriter);
                    missingPipEntry = true;
                    result = CacheMissAnalysisResult.MissingFromNewBuild;

                    WriteLine(string.Empty, writer, newPipSession.PipWriter);
                }

                if (missingPipEntry)
                {
                    // Only write once to the analysis file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, writer);

                    // Nothing to compare if an entry is missing
                    return result;
                }

                if (oldPipSession.FormattedSemiStableHash != newPipSession.FormattedSemiStableHash)
                {
                    // Make trivial json so the print looks like the rest of the diff
                    var oldNode = new JsonNode
                    {
                        Name = RepeatedStrings.FormattedSemiStableHashChanged
                    };
                    oldNode.Values.Add(oldPipSession.FormattedSemiStableHash);

                    var newNode = new JsonNode
                    {
                        Name = RepeatedStrings.FormattedSemiStableHashChanged
                    };
                    newNode.Values.Add(newPipSession.FormattedSemiStableHash);

                    WriteLine(JsonTree.PrintTreeDiff(oldNode, newNode), writer);
                }

                // Diff based off the actual fingerprints instead of the PipCacheMissType
                // to avoid shared cache diff confusion.
                //
                // In the following shared cache scenario:
                // Local cache: WeakFingerprint miss
                // Remote cache: WeakFingerprint hit, StrongFingerprint miss
                //
                // The pip cache miss type will be a strong fingerprint miss,
                // but the data in the fingerprint store will not match the 
                // remote cache's, so we diff based off what we have in the fingerprint store.
                if (oldPipSession.WeakFingerprint != newPipSession.WeakFingerprint)
                {
                    WriteLine("WeakFingerprint", writer);
                    WriteLine(JsonTree.PrintTreeDiff(oldPipSession.GetWeakFingerprintTree(), newPipSession.GetWeakFingerprintTree()), writer);
                    result = CacheMissAnalysisResult.WeakFingerprintMismatch;
                }
                else if (oldPipSession.StrongFingerprint != newPipSession.StrongFingerprint)
                {
                    WriteLine("StrongFingerprint", writer);
                    WriteLine(JsonTree.PrintTreeDiff(oldPipSession.GetStrongFingerprintTree(), newPipSession.GetStrongFingerprintTree()), writer);
                    result = CacheMissAnalysisResult.StrongFingerprintMismatch;
                }
                else
                {
                    WriteLine("The fingerprints from both builds matched and no cache retrieval errors occurred.", writer);
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, writer, oldPipSession.PipWriter, newPipSession.PipWriter);
                    result = CacheMissAnalysisResult.UncacheablePip;
                }
            }

            return result;
        }

        private static void WriteLine(string message, TextWriter writer, params TextWriter[] additionalWriters)
        {
            writer?.WriteLine(message);
            foreach (var w in additionalWriters)
            {
                w?.WriteLine(message);
            }
        }


        /// <summary>
        /// Any strings that need to be repeated.
        /// </summary>
        public readonly struct RepeatedStrings
        {
            /// <summary>
            /// Disallowed file accesses prevent caching.
            /// </summary>
            public const string DisallowedFileAccessesOrPipFailuresPreventCaching
                = "Settings that permit disallowed file accesses or pip failure can prevent pip outputs from being stored in the cache.";

            /// <summary>
            /// Missing directory membership fingerprint.
            /// </summary>
            public const string MissingDirectoryMembershipFingerprint
                = "Directory membership fingerprint entry missing from store.";

            /// <summary>
            /// Formatted semi stable hash changed.
            /// </summary>
            public const string FormattedSemiStableHashChanged
                = "FormattedSemiStableHash";
        }
    }
}
