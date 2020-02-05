// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json.Linq;
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
        PathSetHashMismatch,

        /// <nodoc/>
        StrongFingerprintMismatch,

        /// <nodoc/>
        UncacheablePip,

        /// <nodoc/>
        DataMiss,

        /// <nodoc/>
        OutputMiss,

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
        public static CacheMissAnalysisDetailAndResult AnalyzeCacheMiss(
            PipCacheMissInfo missInfo,
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc,
            CacheMissDiffFormat diffFormat)
        {
            Contract.Requires(oldSessionFunc != null);
            Contract.Requires(newSessionFunc != null);

            var cacheMissType = missInfo.CacheMissType.ToString();
            var cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType);

            switch (missInfo.CacheMissType)
            {
                // Fingerprint miss.
                case PipCacheMissType.MissForDescriptorsDueToAugmentedWeakFingerprints:
                case PipCacheMissType.MissForDescriptorsDueToWeakFingerprints:
                case PipCacheMissType.MissForDescriptorsDueToStrongFingerprints:
                    // Compute the pip unique output hash to use as the primary lookup key for fingerprint store entries
                    cacheMissAnalysisDetailAndResult = AnalyzeFingerprints(oldSessionFunc, newSessionFunc, diffFormat, cacheMissType);
                    break;

                // We had a weak and strong fingerprint match, but couldn't retrieve correct data from the cache
                case PipCacheMissType.MissForCacheEntry:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.DataMiss, "Cache entry missing from the cache.");
                    break;

                case PipCacheMissType.MissForProcessMetadata:
                case PipCacheMissType.MissForProcessMetadataFromHistoricMetadata:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.DataMiss, "MetaData missing from the cache.");
                    break;

                case PipCacheMissType.MissForProcessOutputContent:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.OutputMiss, "Outputs missing from the cache.", new JObject(new JProperty("MissingOutputs", missInfo.MissedOutputs)));
                    break;

                case PipCacheMissType.MissDueToInvalidDescriptors:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.InvalidDescriptors, "Cache returned invalid data.");
                    break;

                case PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.ArtificialMiss, "Cache miss artificially forced by user.");
                    break;

                case PipCacheMissType.Hit:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.NoMiss, "Pip was a cache hit.");
                    break;

                case PipCacheMissType.Invalid:
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.Invalid, "No valid changes or cache issues were detected to cause process execution, but a process still executed.");
                    break;

                default:
                    break;
            }

            return cacheMissAnalysisDetailAndResult;
        }

        /// <summary>
        /// A struct used to store the detail and result of a cache miss analysis
        /// </summary>
        public class CacheMissAnalysisDetailAndResult
        {
            /// <summary>
            /// Pip Cache Miss Analysis Result
            /// </summary>
            public CacheMissAnalysisResult Result;

            /// <summary>
            /// Pip Cache Miss Analysis Detail
            /// </summary>
            public CacheMissAnalysisDetail Detail;

            /// <summary>
            /// Constructor.
            /// </summary>
            public CacheMissAnalysisDetailAndResult(string cacheMissType, CacheMissAnalysisResult cacheMissAnalysisResult = CacheMissAnalysisResult.Invalid, string cacheMissReason = "Unhandled cache miss type.", JObject cacheMissInfo = null)
            {
                Result = cacheMissAnalysisResult;
                Detail = new CacheMissAnalysisDetail(cacheMissType, cacheMissReason, cacheMissInfo);                  
            }

        }

        /// <summary>
        /// A struct used to store the detail of a cache miss analysis detail
        /// </summary>
        /// <remarks>
        /// Cache miss analysis result in the log looks like:
        /// {
        ///     PipSemiStableHash: {
        ///         Description:
        ///         FromCacheLookUp:
        ///         Detail: {
        ///            Type: ...
        ///            Reason: ...
        ///            Info: ...
        ///         }
        ///     }
        /// }
        /// This struct would be put in the log Json as the value of "Detail".
        /// </remarks>
        public struct CacheMissAnalysisDetail
        {
            /// <summary>
            /// Type property in the Json
            /// </summary>
            public string Type;

            /// <summary>
            /// Reason property in the Json
            /// </summary>
            public string Reason;

            /// <summary>
            /// Info property in the Json
            /// </summary>
            /// <remark>
            /// This property can be absent in the Json.
            /// </remark>
            public JObject Info;

            /// <summary>
            /// Constructor
            /// </summary>
            public CacheMissAnalysisDetail(string cacheMissType, string cacheMissReason, JObject cacheMissInfo)
            {
                Type = cacheMissType;
                Reason = cacheMissReason;
                Info = cacheMissInfo;
            }

            /// <summary> 
            /// Add cache miss info property
            /// </summary>
            public void AddPropertyToCacheMissInfo(string propertyName, object propertyValue)
            {
                if (Info == null)
                {
                    Info = new JObject();
                }

                Info.Add(new JProperty(propertyName, propertyValue));
            }

            /// <summary>
            /// Construct the JPropety in the below format with this cache miss analysis detail 
            ///     PipSemiStableHash: {
            ///         Description:
            ///         FromCacheLookUp:
            ///         Detail: {
            ///            Type: ...
            ///            Reason: ...
            ///            Info: ...
            ///         }
            ///     }
            /// </summary>
            public JProperty ToJObjectWithPipInfo(string pipFormattedSemiStableHash, string pipDescription, bool fromCacheLookup)
            {
                var result = new JObject();
                result.Add(new JProperty("Description", pipDescription));
                result.Add(new JProperty("FromCacheLookup", fromCacheLookup));

                var detail = new JObject(
                        new JProperty(nameof(Type), Type),
                        new JProperty(nameof(Reason), Reason));

                if (Info != null)
                {
                    detail.Add(new JProperty(nameof(Info), Info));
                }

                result.Add(new JProperty("Detail", detail));

                return new JProperty(pipFormattedSemiStableHash, result);
            }
        }

        private static CacheMissAnalysisDetailAndResult AnalyzeFingerprints(
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc,
            CacheMissDiffFormat diffFormat,
            string cacheMissType)
        {
            var cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType);

            // While a PipRecordingSession is in scope, any pip information retrieved from the fingerprint store is
            // automatically written out to per-pip files.
            using (var oldPipSession = oldSessionFunc())
            using (var newPipSession = newSessionFunc())
            {
                if (!oldPipSession.EntryExists)
                {
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.MissingFromOldBuild, $"No fingerprint computation data found from old build. This may be the first execution where pip outputs were stored to the cache. {RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching}");

                    // Write to just the old pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, oldPipSession.PipWriter);
                    WriteLine(string.Empty, oldPipSession.PipWriter);

                    return cacheMissAnalysisDetailAndResult;
                }

                if (!newPipSession.EntryExists)
                {
                    // Cases:
                    // 1. ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations
                    // 2. ScheduleProcessNotStoredDueToMissingOutputs
                    // 3. ScheduleProcessNotStoredToWarningsUnderWarnAsError
                    // 4. ScheduleProcessNotStoredToCacheDueToInherentUncacheability
                    cacheMissAnalysisDetailAndResult = new CacheMissAnalysisDetailAndResult(cacheMissType, CacheMissAnalysisResult.MissingFromNewBuild, $"No fingerprint computation data found from new build. {RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching}");
                    // Write to just the new pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, newPipSession.PipWriter);
                    WriteLine(string.Empty, newPipSession.PipWriter);

                    return cacheMissAnalysisDetailAndResult;
                }

                if (oldPipSession.FormattedSemiStableHash != newPipSession.FormattedSemiStableHash)
                {
                    cacheMissAnalysisDetailAndResult.Detail.Reason = "SemiStableHashs of the builds are different.";
                    // Make trivial json so the print looks like the rest of the diff
                    if (diffFormat == CacheMissDiffFormat.CustomJsonDiff)
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("SemiStableHash",
                            new JObject(
                                new JProperty("Old", oldPipSession.FormattedSemiStableHash),
                                new JProperty("New", newPipSession.FormattedSemiStableHash)));
                    }
                    else
                    {
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
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("SemiStableHash", JsonTree.PrintTreeDiff(oldNode, newNode));
                    }
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
                    cacheMissAnalysisDetailAndResult.Detail.Reason = "WeakFingerprints of the builds are different.";
                    if (diffFormat == CacheMissDiffFormat.CustomJsonDiff)
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("WeakFingerprintMismatchResult", oldPipSession.DiffWeakFingerprint(newPipSession));
                    }
                    else
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("WeakFingerprintMismatchResult", PrintTreeDiff(oldPipSession.GetWeakFingerprintTree(), newPipSession.GetWeakFingerprintTree(), oldPipSession));
                    }

                    cacheMissAnalysisDetailAndResult.Result = CacheMissAnalysisResult.WeakFingerprintMismatch;
                }
                else if (oldPipSession.PathSetHash != newPipSession.PathSetHash)
                {
                    cacheMissAnalysisDetailAndResult.Detail.Reason = "PathSets of the builds are different.";
                    if (diffFormat == CacheMissDiffFormat.CustomJsonDiff)
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("PathSetMismatchResult", oldPipSession.DiffPathSet(newPipSession));
                    }
                    else
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("PathSetMismatchResult", PrintTreeDiff(oldPipSession.GetStrongFingerprintTree(), newPipSession.GetStrongFingerprintTree(), oldPipSession));
                    }

                    cacheMissAnalysisDetailAndResult.Result = CacheMissAnalysisResult.PathSetHashMismatch;
                }
                else if (oldPipSession.StrongFingerprint != newPipSession.StrongFingerprint)
                {
                    cacheMissAnalysisDetailAndResult.Detail.Reason = "StrongFingerprints of the builds are different.";
                    if (diffFormat == CacheMissDiffFormat.CustomJsonDiff)
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("StrongFingerprintMismatchResult", oldPipSession.DiffStrongFingerprint(newPipSession));
                    }
                    else
                    {
                        cacheMissAnalysisDetailAndResult.Detail.AddPropertyToCacheMissInfo("StrongFingerprintMismatchResult", PrintTreeDiff(oldPipSession.GetStrongFingerprintTree(), newPipSession.GetStrongFingerprintTree(), oldPipSession));
                    }

                    cacheMissAnalysisDetailAndResult.Result = CacheMissAnalysisResult.StrongFingerprintMismatch;
                }
                else
                {
                    cacheMissAnalysisDetailAndResult.Detail.Reason = $"The fingerprints from both builds matched and no cache retrieval errors occurred. {RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching}";
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, oldPipSession.PipWriter, newPipSession.PipWriter);
                    cacheMissAnalysisDetailAndResult.Result = CacheMissAnalysisResult.UncacheablePip;
                }
            }

            return cacheMissAnalysisDetailAndResult;
        }

        private static string PrintTreeDiff(JsonNode oldNode, JsonNode newNode, PipRecordingSession oldPipSession)
        {
            return JsonTree.PrintTreeDiff(oldNode, newNode) + oldPipSession.GetOldProvenance().ToString();
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
                = "SemiStableHash";

            /// <summary>
            /// Marker indicating that a value is not specified.
            /// </summary>
            public const string UnspecifiedValue = "[Unspecified value]";

            /// <summary>
            /// Marker indicating that a value is specified.
            /// </summary>
            public const string ExistentValue = "[Value exists]";

            /// <summary>
            /// Marker indicating that an expected value is missing, which indicates a bug in the cache miss analysis.
            /// </summary>
            public const string MissingValue = "Missing value";
        }
    }
}
