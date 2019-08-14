// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.ExceptionServices;
using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Ide.JsonRpc;

namespace BuildXL.Ide.LanguageServer.Tracing
{
    /// <summary>
    /// High level tracing facility reponsible for constructing and managing the logger and the logging context.
    /// </summary>
    /// <remarks>
    /// This class also glues together an ETW listener and sends the events to the file system and to the language server.
    /// </remarks>
    internal sealed class Tracer : IDisposable
    {
        private readonly string m_logFilePath;
        private readonly IDisposable m_loggingListeners;

        private readonly Logger m_logger = Logger.CreateLogger();

        // Use custom environment to separate BuildXL's events from the language server's events.
        private readonly LoggingContext m_loggingContext = new LoggingContext("DsLanguageServer", environment: "DsLanguageServer");

        public Tracer(StreamJsonRpc.JsonRpc pushRpc, string logFilePath, EventLevel logFileVerbosity, EventLevel outputPaneVerbosity)
        {
            Contract.Requires(pushRpc != null);
            Contract.Requires(!string.IsNullOrEmpty(logFilePath));
            Contract.Requires(logFileVerbosity > outputPaneVerbosity, $"Log file verbosity ('{logFileVerbosity}') should be more detailed than output pane verbosity '{outputPaneVerbosity}'.");

            m_logFilePath = logFilePath;
            var outputEventWriter = new OutputWindowEventWriter(
                new OutputWindowReporter(pushRpc),
                File.CreateText(logFilePath),
                outputPaneVerbosity);

            m_loggingListeners = SetupLogging(logFileVerbosity, outputEventWriter);
        }

        public Tracer()
        {
        }

        /// <summary>
        /// Path to the current log file.
        /// </summary>
        public string LogFilePath => m_logFilePath;

        /// <summary>
        /// Instance of the current ETW logger.
        /// </summary>
        public Logger Logger => m_logger;

        /// <summary>
        /// Instance of the current logging context.
        /// </summary>
        public LoggingContext LoggingContext => m_loggingContext;

        /// <inheritdoc />
        public void Dispose()
        {
            m_loggingListeners.Dispose();
            AriaV2StaticState.TryShutDown(out _);
        }

        /// <summary>
        /// Set up event listeners for BuildXL build.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static IDisposable SetupLogging(EventLevel level, IEventWriter writer)
        {
            var eventListener = new TextWriterEventListener(eventSource: Events.Log, writer: writer, baseTime: DateTime.UtcNow, level: level);

            var primarySource = bxl.ETWLogger.Log;
            if (primarySource.ConstructionException != null)
            {
                // Rethrow an exception preserving the original stack trace.
                var edi = ExceptionDispatchInfo.Capture(primarySource.ConstructionException);
                edi.Throw();

                // This code is unreachable, but compiler doesn't know about it.
                throw null;
            }

            eventListener.RegisterEventSource(primarySource);
            eventListener.EnableTaskDiagnostics(BuildXL.Tracing.ETWLogger.Tasks.CommonInfrastructure);
            AriaV2StaticState.Enable(AriaTenantToken.Key);

            var eventSources = new EventSource[]
            {
                bxl.ETWLogger.Log,
                BuildXL.Engine.Cache.ETWLogger.Log,
                BuildXL.Engine.ETWLogger.Log,
                BuildXL.Scheduler.ETWLogger.Log,
                BuildXL.Tracing.ETWLogger.Log,
                bxlScriptAnalyzer.ETWLogger.Log,
                BuildXL.Ide.LanguageServer.ETWLogger.Log,
                BuildXL.FrontEnd.Core.ETWLogger.Log,
                BuildXL.FrontEnd.Script.ETWLogger.Log,
                BuildXL.FrontEnd.Nuget.ETWLogger.Log,
            };

            using (var listener = new TrackingEventListener(Events.Log))
            {
                foreach (var eventSource in eventSources)
                {
                    Events.Log.RegisterMergedEventSource(eventSource);
                }
            }

            return eventListener;
        }
    }
}
