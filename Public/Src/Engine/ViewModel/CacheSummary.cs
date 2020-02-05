// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace BuildXL.ViewModel
{
    /// <nodoc />
    public class CacheSummary
    {
        /// <nodoc />
        public long TotalProcessPips { get; set; }

        /// <nodoc />
        public long ProcessPipCacheHit { get; set; }

        /// <nodoc />
        public List<CacheMissSummaryEntry> Entries { get; } = new List<CacheMissSummaryEntry>();

        /// <nodoc />
        public List<string> BatchEntries { get; } = new List<string>();

        /// <nodoc />
        internal void RenderMarkdown(MarkDownWriter writer)
        {
            int cacheHitRate = 0;
            if (TotalProcessPips > 0)
            {
                cacheHitRate = (int)(100.0 * ProcessPipCacheHit / TotalProcessPips);
            }

            var caseRateMessage = $"Process pip cache hits: {cacheHitRate}% ({ProcessPipCacheHit}/{TotalProcessPips})";

            if (Entries.Count == 0 && BatchEntries == null) 
            {
                writer.WriteDetailedTableEntry("Cache rates", caseRateMessage);
            }
            else
            {
                writer.StartDetailedTableSummary(
                    "Cache rates & Misses",
                    caseRateMessage
                    );

                foreach (var entry in Entries)
                {
                    writer.WritePreSection(
                        entry.PipDescription + (entry.FromCacheLookup ? " (From Cachelookup)" : null), 
                        entry.Reason,
                        25);
                }

                foreach (var entry in BatchEntries)
                {
                    writer.WriteLineRaw(entry);
                }
                writer.EndDetailedTableSummary();
            }
            
        }
    }
}
