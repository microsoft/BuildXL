// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Sdk.Tracing;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field

namespace BuildXL.FrontEnd.Script.Analyzer.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    public abstract partial class Logger : LoggerBase
    {
        private const int DefaultKeywords = (int)(Keywords.UserMessage | Keywords.Diagnostics);

        private bool m_preserveLogEvents;
        private int m_errorCount;

        private readonly ConcurrentQueue<Diagnostic> m_capturedDiagnostics = new ConcurrentQueue<Diagnostic>();

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }

        /// <summary>
        /// True when at least one error occurred.
        /// </summary>
        public bool HasErrors => ErrorCount != 0;

        /// <summary>
        /// Returns number of errors.
        /// </summary>
        public int ErrorCount => m_errorCount;

        /// <summary>
        /// Provides diagnostics captured by the logger.
        /// Would be non-empty only when preserveLogEvents flag was specified in the <see cref="Logger.CreateLogger"/> factory method.
        /// </summary>
        public IReadOnlyList<Diagnostic> CapturedDiagnostics => m_capturedDiagnostics.ToList();

        /// <summary>
        /// Factory method that creates instances of the logger.
        /// </summary>
        public static Logger CreateLogger(bool preserveLogEvents = false)
        {
            return new LoggerImpl
            {
                m_preserveLogEvents = preserveLogEvents,
            };
        }

        /// <summary>
        /// Set up console event listener for BuildXL's ETW event sources.
        /// </summary>
        /// <param name="level">The level of data to be sent to the listener.</param>
        /// <returns>An <see cref="EventListener"/> with the appropriate event sources registered.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static IDisposable SetupEventListener(EventLevel level)
        {
            var eventListener = new ConsoleEventListener(Events.Log, DateTime.UtcNow, true, true, true, false, level: level);

            var primarySource = bxlScriptAnalyzer.ETWLogger.Log;
            if (primarySource.ConstructionException != null)
            {
                throw primarySource.ConstructionException;
            }

            eventListener.RegisterEventSource(primarySource);

            eventListener.EnableTaskDiagnostics(BuildXL.Tracing.ETWLogger.Tasks.CommonInfrastructure);

            var eventSources = new EventSource[]
                               {
                                   bxl.ETWLogger.Log,
                                   bxlScriptAnalyzer.ETWLogger.Log,
                                   BuildXL.Engine.Cache.ETWLogger.Log,
                                   BuildXL.Engine.ETWLogger.Log,
                                   BuildXL.Scheduler.ETWLogger.Log,
                                   BuildXL.Tracing.ETWLogger.Log,
                                   BuildXL.Storage.ETWLogger.Log,
                                   BuildXL.FrontEnd.Core.ETWLogger.Log,
                                   BuildXL.FrontEnd.Script.ETWLogger.Log,
                                   BuildXL.FrontEnd.Nuget.ETWLogger.Log,
                                   BuildXL.FrontEnd.Download.ETWLogger.Log,
                               };

            using (var dummy = new TrackingEventListener(Events.Log))
            {
                foreach (var eventSource in eventSources)
                {
                    Events.Log.RegisterMergedEventSource(eventSource);
                }
            }

            return eventListener;
        }

        /// <nodoc />
        public override bool InspectMessageEnabled => true;

        /// <nodoc />
        protected override void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            if (level.IsError())
            {
                Interlocked.Increment(ref m_errorCount);
            }

            if (m_preserveLogEvents)
            {
                var diagnostic = new Diagnostic(logEventId, level, message, location);
                m_capturedDiagnostics.Enqueue(diagnostic);
            }
        }

        [GeneratedEvent(
            (ushort)LogEventId.ErrorParsingFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Error loading configuration.",
            Keywords = DefaultKeywords)]
        public abstract void ErrorParsingFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorParsingFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error at position {position} of command line pip filter {filter}. {message} {positionMarker}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void ErrorParsingFilter(LoggingContext context, string filter, int position, string message, string positionMarker);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorFilterHasNoMatchingSpecs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Filter: {filter} resulted in no specs being selected to be fixed.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void ErrorFilterHasNoMatchingSpecs(LoggingContext context, string filter);

        [GeneratedEvent(
            (ushort)LogEventId.FixRequiresPrettyPrint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "When you pass '/Fix' you should add the PrettyPrint analyzer using '/a:PrettyPrint' as the last argument to ensure the fixes are written to disk.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void FixRequiresPrettyPrint(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.AnalysisErrorSummary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Encountered {nrOfErrors} errors using {nrOfAnalyzers}. Pass '/Fix' to automatically apply the fixes.",
            Keywords = DefaultKeywords)]
        public abstract void AnalysisErrorSummary(LoggingContext context, int nrOfErrors, int nrOfAnalyzers);

        #region PrettyPrint

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintErrorWritingSpecFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to write updates back to the file: {message}.",
            Keywords = DefaultKeywords)]
        public abstract void PrettyPrintErrorWritingSpecFile(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintUnexpectedChar,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered: '{encounteredToken}' expected: '{expectedToken}' in line:\r\n{encounteredLine}\r\n{positionMarker}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintUnexpectedChar(LoggingContext context, Location location, string expectedToken, string encounteredToken, string encounteredLine, string positionMarker);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintExtraTargetLines,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered a missing line. Expected line:\r\n{expectedLine}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintExtraTargetLines(LoggingContext context, Location location, string expectedLine);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintExtraSourceLines,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered an extra line:\r\n{encountered}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintExtraSourceLines(LoggingContext context, Location location, string encountered);

        #endregion

        #region LegacyLiteralCreation

        [GeneratedEvent(
            (ushort)LogEventId.LegacyLiteralFix,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use {fixExpression} rather than {existingExpression}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void LegacyLiteralFix(LoggingContext context, Location location, string fixExpression, string existingExpression);

        #endregion

        #region PathFixer

        [GeneratedEvent(
            (ushort)LogEventId.PathFixerIllegalPathSeparator,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use path separator '{expectedSeparator}' rather than '{illegalSeparator}' in '{pathFragment}'",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PathFixerIllegalPathSeparator(LoggingContext context, Location location, string pathFragment, char expectedSeparator, char illegalSeparator);

        [GeneratedEvent(
            (ushort)LogEventId.PathFixerIllegalCasing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use lowercase for all directory parts. Use '{expectedLoweredFragment}' rather than '{encounteredFragment}' in '{pathFragment}'.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PathFixerIllegalCasing(LoggingContext context, Location location, string pathFragment, string encounteredFragment, string expectedLoweredFragment);

        #endregion

        #region Documentation

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationMissingOutputFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "The Documentation Analyzer requires the parameter '{parameter}'. None was given.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void DocumentationMissingOutputFolder(LoggingContext context, string parameter);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationErrorCleaningFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error cleaning output folder '{outputFolder}': {message}",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationErrorCleaningFolder(LoggingContext context, string outputFolder, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationErrorCreatingOutputFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error creating output folder '{outputFolder}': {message}",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationErrorCreatingOutputFolder(LoggingContext context, string outputFolder, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationSkippingV1Module,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Skipping module '{moduleName}' because it is not a v2 module.",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationSkippingV1Module(LoggingContext context, string moduleName);

        #endregion
    }
}

#pragma warning restore CA1823 // Unused field
