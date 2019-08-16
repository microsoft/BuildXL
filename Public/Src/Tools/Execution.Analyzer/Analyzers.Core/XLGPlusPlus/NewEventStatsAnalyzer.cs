// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeEventStatsXldbAnalyzer()
        {
            string inputDirPath = null;
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("inputDir", StringComparison.OrdinalIgnoreCase) ||
                     opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    inputDirPath = ParseSingletonPathOption(opt, inputDirPath);
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }


            if (string.IsNullOrEmpty(inputDirPath))
            {
                throw Error("/inputDir parameter is required");
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new EventStatsXldbAnalyzer(GetAnalysisInput())
            {
                InputDirPath = inputDirPath,
                OutputFilePath = outputFilePath
            };
        }

        /// <summary>
        /// Write the help message when the analyzer is invoked with the /help flag
        /// </summary>
        private static void WriteEventStatsXldbHelp(HelpWriter writer)
        {
            writer.WriteBanner("Event Stats Xldb Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.EventStatsXldb), "Generates stats on the aggregate size and count of execution log events, but uses the RocksDB database as the source of truth");
            writer.WriteOption("inputDir", "Required. The directory to read the RocksDB database from", shortName: "i");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to get stats on events (count and total size)
    /// </summary>
    internal sealed class EventStatsXldbAnalyzer : Analyzer
    {
        /// <summary>
        /// Input directory path that contains the RocksDB sst files
        /// </summary>
        public string InputDirPath;

        /// <summary>
        /// Output file path where the results will be written to.
        /// </summary>
        public string OutputFilePath;

        public EventStatsXldbAnalyzer(AnalysisInput input) : base(input) { }

        /// <inheritdoc/>
        public override int Analyze()
        {
            using (var dataStore = new XldbDataStore(storeDirectory: InputDirPath))
            using (var outputStream = File.OpenWrite(OutputFilePath))
            using (var writer = new StreamWriter(outputStream))
            {
                var workerToEventDict = new Dictionary<uint, Dictionary<Xldb.ExecutionEventId, (int, int)>>();
                foreach (Xldb.ExecutionEventId eventId in Enum.GetValues(typeof(Xldb.ExecutionEventId)))
                {
                    var eventCount = dataStore.GetCountByEvent(eventId);

                    if (eventCount != null)
                    {
                        foreach (var workerCount in eventCount.WorkerToCountMap)
                        {
                            if (workerToEventDict.TryGetValue(workerCount.Key, out var eventDict))
                            {
                                eventDict[eventId] = (workerCount.Value, 0);
                            }
                            else
                            {
                                var dict = new Dictionary<Xldb.ExecutionEventId, (int, int)>();
                                dict.Add(eventId, (workerCount.Value, 0));
                                workerToEventDict.Add(workerCount.Key, dict);
                            }
                        }
                        foreach (var payloadSize in eventCount.WorkerToPayloadMap)
                        {
                            workerToEventDict.TryGetValue(payloadSize.Key, out var eventDict);
                            eventDict.TryGetValue(eventId, out var tup);
                            eventDict[eventId] = (tup.Item1, payloadSize.Value);
                        }
                    }
                }

                foreach (var workerDict in workerToEventDict)
                {
                    writer.WriteLine("Worker {0}", workerDict.Key);
                    var maxLength = Enum.GetValues(typeof(Scheduler.Tracing.ExecutionEventId)).Cast<Scheduler.Tracing.ExecutionEventId>().Select(e => e.ToString().Length).Max();
                    foreach (var eventStats in workerDict.Value)
                    {
                        writer.WriteLine(
                        "{0}: {1} Count = {2}",
                        eventStats.Key.ToString().PadRight(maxLength, ' '),
                        eventStats.Value.Item2.ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '),
                        eventStats.Value.Item1.ToString(CultureInfo.InvariantCulture).PadLeft(12, ' '));
                    }
                    writer.WriteLine();
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        protected override bool ReadEvents()
        {
            // Do nothing. This analyzer does not read events.
            return true;
        }
    }
}
