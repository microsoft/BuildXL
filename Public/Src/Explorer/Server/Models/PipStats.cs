// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Execution.Analyzer;

namespace BuildXL.Explorer.Server.Models
{
    public class PipStats
    {
        public long? ProcessPipCacheHits { get; set; }
        public long? ProcessPipCacheMisses { get; set; }
        public long? ProcessDelayedBySemaphore { get; set; }
        public long? ProcessPipsSkippedDueToFailedDependencies { get; set; }


        public StatsPerType Total { get; set; }

        public StatsPerType Process { get; set; }
        public StatsPerType WriteFile { get; set; }
        public StatsPerType CopyFile { get; set; }
        public StatsPerType SealDirectory { get; set; }
        public StatsPerType Ipc { get; set; }
        public StatsPerType Value { get; set; }
        public StatsPerType SpecFile { get; set; }
        public StatsPerType Module { get; set; }
        public StatsPerType HashSourceFile { get; set; }

        public PipStats(BuildXLStats stats)
        {
            ProcessPipCacheHits = stats.GetValue("ProcessPipCacheHits");
            ProcessPipCacheMisses = stats.GetValue("ProcessPipCacheMisses");
            ProcessDelayedBySemaphore = stats.GetValue("ProcessDelayedBySemaphore");
            ProcessPipsSkippedDueToFailedDependencies = stats.GetValue("ProcessPipsSkippedDueToFailedDependencies");
            Total = new StatsPerType
            {
                Total = stats.GetValue("TotalPips"),
                Done = stats.GetValue("PipsSucceeded"),
                Failed = stats.GetValue("PipsFailed"),
                Ignored = stats.GetValue("PipsIgnored"),
            };

            Process = new StatsPerType("Process", stats);
            CopyFile = new StatsPerType("CopyFile", stats);
            WriteFile = new StatsPerType("WriteFile", stats);
            SealDirectory = new StatsPerType("SealDirectory", stats);
            Ipc = new StatsPerType("Ipc", stats);
            Value = new StatsPerType("Value", stats);
            SpecFile = new StatsPerType("SpecFile", stats);
            Module = new StatsPerType("Module", stats);
            HashSourceFile = new StatsPerType("HashSourceFile", stats);
        }

        public class StatsPerType
        {
            public StatsPerType()
            {
            }

            public StatsPerType(string name, BuildXLStats stats)
            {
                Total = stats.GetValue($"PipStats.{name}_Total");
                Done = stats.GetValue($"PipStats.{name}_Done");
                Failed = stats.GetValue($"PipStats.{name}_Failed");
                Skipped = stats.GetValue($"PipStats.{name}_Skipped ");
                Ignored = stats.GetValue($"PipStats.{name}_Ignored");
            }

            public long? Total { get; set; }
            public long? Done { get; set; }
            public long? Failed { get; set; }
            public long? Skipped { get; set; }
            public long? Ignored { get; set; }
        }
    }
}

