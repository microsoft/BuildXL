// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using System;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        // Required flags
        private const string OutputDirectoryOption = "OutputDirectory";

        // Optional flags
        public Analyzer InitializePackedExecutionExporter()
        {
            string outputDirectoryPath = null;

            foreach (Option opt in AnalyzerOptions)
            {
                if (opt.Name.Equals(OutputDirectoryOption, StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectoryPath = ParseSingletonPathOption(opt, outputDirectoryPath);
                }
                else
                {
                    throw Error("Unknown option for PackedExecutionExporter: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputDirectoryPath))
            {
                throw Error("/outputDirectory parameter is required");
            }

            return new PackedExecutionExporterAnalyzer(GetAnalysisInput(), outputDirectoryPath);
        }

        private static void WritePackedExecutionExporterHelp(HelpWriter writer)
        {
            writer.WriteBanner("Packed Execution Exporter");
            writer.WriteModeOption(nameof(AnalysisMode.PackedExecutionExporter), "Exports the pip graph and execution data in PackedExecution format");
            writer.WriteLine("Required");
            writer.WriteOption(OutputDirectoryOption, "The location of the output directory.");
        }
    }

    /// <summary>
    /// Exports the build graph and execution data in PackedExecution format, as an Analyzer.
    /// </summary>
    /// <remarks>
    /// The base implementation is in BuildXL.Scheduler.Tracing.PackedExecutionExporter in order to
    /// allow the exporter to run concurrently with the actual build's execution. This is a wrapper
    /// type which allows the pre-existing instance to be wrapped as an Analyzer as well.
    /// </remarks>
    public sealed class PackedExecutionExporterAnalyzer : Analyzer
    {
        private readonly PackedExecutionExporter m_exporter;

        /// <summary>
        /// Construct a PackedExecutionExporter.
        /// </summary>
        public PackedExecutionExporterAnalyzer(AnalysisInput input, string outputDirectoryPath)
            : base(input)
        {
            m_exporter = new PackedExecutionExporter(input.CachedGraph.PipGraph, outputDirectoryPath);
        }

        /// <inheritdoc />
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
            // Delegate all events to underlying exporter
            => data.Metadata.LogToTarget(data, m_exporter);

        /// <inheritdoc />
        public override bool CanHandleWorkerEvents => m_exporter.CanHandleWorkerEvents;

        /// <inheritdoc />
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
            => m_exporter.CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);

        /// <inheritdoc />
        public override void Prepare() => m_exporter.Prepare();

        /// <inheritdoc />
        public override int Analyze() => m_exporter.Analyze();
    }
}
