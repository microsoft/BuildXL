// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFilterLogAnalyzer()
        {
            string outputFilePath = null;
            List<ExecutionEventId> excludedEvents = new List<ExecutionEventId>();
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                {
                    excludedEvents.Add(ParseEnumOption<ExecutionEventId>(opt));
                }
                else
                {
                    throw Error("Unknown option for filter log analysis: {0}", opt.Name);
                }
            }

            return new FilterLogAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
                ExcludedEvents = excludedEvents,
            };
        }

        private static void WriteFilterLogHelp(HelpWriter writer)
        {
            writer.WriteBanner("Filter Log Analysis");
            writer.WriteOption("exclude", "Required. An execution event id name to exclude from the filtered execution log.");
            writer.WriteOption("outputFile", "Required. The file where to write the filtered execution log.", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to get stats on events (count and total size)
    /// </summary>
    internal sealed class FilterLogAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        /// <summary>
        /// The path to the output file
        /// </summary>
        public List<ExecutionEventId> ExcludedEvents = new List<ExecutionEventId>();

        private IExecutionLogTarget m_outputTarget;

        public FilterLogAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override void Prepare()
        {
            foreach (var eventId in ExcludedEvents)
            {
                DisableEvent(eventId);
            }

            m_outputTarget = new ExecutionLogFileTarget(
                new BinaryLogger(
                    File.Open(OutputFilePath, FileMode.CreateNew, FileAccess.ReadWrite),
                    CachedGraph.Context,
                    CachedGraph.PipGraph.GraphId,
                    CachedGraph.PipGraph.MaxAbsolutePathIndex));
        }

        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            data.Metadata.LogToTarget(data, m_outputTarget);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            return 0;
        }

        public override void Dispose()
        {
            m_outputTarget.Dispose();
        }
    }
}
