// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeEventStatsAnalyzer()
        {
            string outputFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }

            return new EventStatsAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteEventStatsHelp(HelpWriter writer)
        {
            writer.WriteBanner("Event Stats Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.EventStats), "Generates stats on the aggregate size and count of execution log events");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to get stats on events (count and total size)
    /// </summary>
    internal sealed class EventStatsAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        private WorkerEventStats[] m_workerStats = new WorkerEventStats[byte.MaxValue];

        public EventStatsAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    for (int workerId = 0; workerId < m_workerStats.Length; workerId++)
                    {
                        var workerStats = m_workerStats[workerId];
                        if (workerStats == null)
                        {
                            continue;
                        }

                        writer.WriteLine("Worker {0}", workerId);

                        var maxLength = Enum.GetValues(typeof(ExecutionEventId)).Cast<ExecutionEventId>().Select(e => e.ToString().Length).Max();
                        foreach (ExecutionEventId eventId in Enum.GetValues(typeof(ExecutionEventId)))
                        {
                            writer.WriteLine(
                                "{0}: {1} (Count = {2})",
                                eventId.ToString().PadRight(maxLength, ' '),
                                workerStats.Sizes[(int)eventId].ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '),
                                workerStats.Counts[(int)eventId].ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '));
                        }

                        writer.WriteLine();
                    }
                }
            }

            return 0;
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            var workerStats = m_workerStats[workerId];
            if (workerStats == null)
            {
                workerStats = new WorkerEventStats();
                m_workerStats[workerId] = workerStats;
            }

            workerStats.Sizes[(int)eventId] += eventPayloadSize;
            workerStats.Counts[(int)eventId] += 1;

            // Return false to keep the event from being parsed
            return false;
        }

        private class WorkerEventStats
        {
            public long[] Counts = new long[byte.MaxValue];
            public long[] Sizes = new long[byte.MaxValue];
        }
    }
}
