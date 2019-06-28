// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING

#endif

#pragma warning disable 1591

namespace BuildXL.Execution.Analyzer.Tracing
{
    /// <summary>
    /// Logging for bxlanalyzer.exe.
    /// The count of local events will be captured in the final <see cref="ExecutionAnalyzerEventCount(LoggingContext, IDictionary{string,int})"/> event which will be sent to telemetry.
    /// There are no log files for execution analyzers, so messages for events with <see cref="EventGenerators.LocalOnly"/> will be lost. 
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger : LoggerBase
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (int)LogEventId.ExecutionAnalyzerInvoked,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.ExecutionAnalyzers,
            Message = "Telemetry only")]
        public abstract void ExecutionAnalyzerInvoked(LoggingContext context, string analysisMode, long runtimeMs, string commandLine);

        [GeneratedEvent(
            (int)LogEventId.ExecutionAnalyzerCatastrophicFailure,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.ExecutionAnalyzers,
            Message = "Telemetry only")]
        public abstract void ExecutionAnalyzerCatastrophicFailure(LoggingContext context, string analysisMode, string exception);

        [GeneratedEvent(
            (ushort)LogEventId.ExecutionAnalyzerEventCount,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Message = "Telemetry only")]
        public abstract void ExecutionAnalyzerEventCount(LoggingContext context, IDictionary<string, int> entryMatches);

        [GeneratedEvent(
            (ushort)LogEventId.FingerprintStorePipMissingFromOldBuild,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "N/A")]
        public abstract void FingerprintStorePipMissingFromOldBuild(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FingerprintStorePipMissingFromNewBuild,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "N/A")]
        public abstract void FingerprintStorePipMissingFromNewBuild(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.FingerprintStoreUncacheablePip,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Message = "N/A")]
        public abstract void FingerprintStoreUncacheablePipAnalyzed(LoggingContext context);
    }
}
