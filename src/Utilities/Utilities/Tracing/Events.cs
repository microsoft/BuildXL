// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// The BuildXL event source
    /// </summary>
    [EventSource(Name = "BuildXL", LocalizationResources = "BuildXL.Utilities.Strings")]
    public sealed class Events : EventSource
    {
        // Here's the theory behind this class
        //
        // * There is one method per event type. Each method has an [Event] attribute
        //   that provides the metadata for the event.
        //
        // * Each event has a unique ID.
        //
        // * Each event has a bitmask of 'keywords'. Keywords correspond to large categories of
        //   events. It's somewhat cross-cutting through the product. ETW consumers can filter on these
        //   with support from the ETW subsystem.
        //
        // * Each event has got a level (informational, verbose, warning, error, etc). Users and ETW consumers can filter on these.
        //
        // * Each event has got a task. The task represents a major functional area within the product.
        //   The BuildXL event listeners allow enabling low-level diagnostic events on a per-task basis.
        //   Tasks are theoretically eligible for ETW-assisted filtering, though this would require a lot of work and a custom consumer
        //
        // * Each task has its own set of opcodes which represent minor functional areas within the major areas.
        //
        // So as an example:
        //
        // * Keywords are cross-cutting. Some of these are defined by the APEX analytics framework (Telemetry, Performance, Diagnostics)
        //   while others are specific to BuildXL.
        //
        // * Tasks represent a large bundle of functionality. In Windows for example, "File System" is a task. For
        //   BuildXL, we have a small number of well-known tasks (< 12).
        //
        // * Opcodes are task-relative. They represent a specific action the task is doing. For example, in Windows
        //   the "File System" task may have a CreateFile opcode, a DeleteFile opcode, RenameFile, etc. A given subsystem
        //   maybe have perhaps up to 25 distinct opcodes.
        //   BuildXL only uses Start/Stop, Info, and Recieve(used for RelatedActivityId) opcodes as they are well known by
        //   ETW trace viewing tools
        //
        // * Finally, for a given opcode you can have multiple distinct events. These represents the different messages
        //   logged while a given operation is under way.
        //
        // All the event methods below pump data into the ETW subsystem. We attach some in-proc listeners
        // such that we can extract these events as they are generated and display them on the console,
        // push them into a file, and count how many errors/warnings have occur.
        //
        // Note that logging an event with level EventLevel.Error and keyword Keywords.UserMessage automatically triggers the
        // build to fail. Basically, if any errors have been logged, BuildXL will exit with a failure code once it is done.
        //
        // Here are some general guidelines on how to extend this:
        //
        // * Events that fire less than 100 times a second on average generally don't need separate keywords, they should use the catch all 'Default' keyword.
        //
        // * Events that fire more than 1000 times a second on average need keywords to turn them off when they are not needed.
        //
        // * Events in between 100 and 1000 are a judgment call.
        //
        // * Define keywords from a user’s (or scenario) point of view. While most users will
        //   simply use the default (turn everything on) keywords are the preferred mechanism
        //   for filtering events.
        //
        // * Use EventLevels less than Informational for relatively rare warnings or errors. When in doubt stick with the default of
        //   Informational, and use Verbose for events that can happen more than 1000 times / second. Typically users would use
        //   keywords more often than EventLevel for filtering, so don't worry about it too much.
        //
        // * The RelatedAcitivityId field is used for SemiStablePipId for easily coorelating all events relating to a pip
        //   when looking at traces.

        /// <summary>
        /// Prefix used to indicate the provenance of an error for the user's benefit.
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string ProvenancePrefix = "{0}({1},{2}): ";

        /// <summary>
        /// Prefix used to indicate a pip.
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string PipPrefix = "[{1}] ";

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip.
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipDependencyAnalysisPrefix = "Detected dependency violation: [{1}] ";

        /// <summary>
        /// Prefix used to indicate a pip, the spec file that generated it, and the working directory
        /// </summary>
        /// <remarks>
        /// Please realize that changing this affects a lot of methods in this class. If you have to, please validate all consumers
        /// in this class.
        /// </remarks>
        public const string PipSpecPrefix = "[{1}, {2}, {3}]";

        /// <summary>
        /// Prefix used to indicate dependency analysis results specific to a pip, the spec file that generated it, and the working directory
        /// </summary>
        /// <remarks>
        /// Why this extra prefix? Text filtering. There's a corresponding ETW keyword, but today people lean mostly on the text logs.
        /// </remarks>
        public const string PipSpecDependencyAnalysisPrefix = "Detected dependency violation: [{1}, {2}, {3}] ";

        /// <summary>
        /// Prefix used to indicate phases.
        /// </summary>
        public const string PhasePrefix = "-- ";

        /// <summary>
        /// Prefix used to indicate that artifacts (files or directories) or pips have changed (or have become dirty).
        /// </summary>
        /// <remarks>
        /// This prefix is used mostly for incremental scheduling logs.
        /// </remarks>
        public const string ArtifactOrPipChangePrefix = ">>> ";

        /// <summary>
        /// Suffix added to the PipProcessError log when the process finished successfully but did not produced all required outputs.
        /// </summary>
        public const string PipProcessErrorMissingOutputsSuffix = "; required output is missing";

        // disable warning regarding 'missing XML comments on public API'. We don't need docs for these methods
#pragma warning disable 1591

        /// <summary>
        /// Major groupings of events used for filtering.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible", Justification = "Needed by event infrastructure.")]
        public static class Keywords
        {
            /// <summary>
            /// Added to events that log performance data (GC stats, parsing stats, object sizes)
            /// </summary>
            public const EventKeywords Performance = (EventKeywords)(1 << 29);

            /// <summary>
            /// Added to Level=Verbose events that may need to be optionally enabled for additional diagnostics but are
            /// generally disabled. The BuildXL host application has command lines to optionally enable events with this
            /// keyword on a per task basis
            /// </summary>
            public const EventKeywords Diagnostics = (EventKeywords)(1 << 28);

            /// <summary>
            /// Events that are sent to CloudBuild listener
            /// </summary>
            public const EventKeywords CloudBuild = (EventKeywords)(1 << 27);

            /// <summary>
            /// Indicates an event that will be interpreted by the BuildXL listeners.
            /// </summary>
            public const EventKeywords UserMessage = (EventKeywords)(1 << 0);

            /// <summary>
            /// This the events relevant to progress indication.
            /// </summary>
            public const EventKeywords Progress = (EventKeywords)(1 << 1);

            /// <summary>
            /// Events related to analysis of file monitoring violations.
            /// </summary>
            public const EventKeywords DependencyAnalysis = (EventKeywords)(1 << 2);

            /// <summary>
            /// Events that are only shown as temporary status on the console. They may be overwritten by future events
            /// if supported by the console
            /// </summary>
            public const EventKeywords Overwritable = (EventKeywords)(1 << 3);

            /// <summary>
            /// Events that are only shown as temporary status on the console and are printed if the console supports overwritting.
            /// They will be overwritten by future events.
            /// <remarks>Events flagged with this keyword will never go to the text log (as opposed to events flagged with 'Overwritable')</remarks>
            /// </summary>
            public const EventKeywords OverwritableOnly = (EventKeywords)(1 << 4);

            /// <summary>
            /// Events sent to external ETW listeners only
            /// </summary>
            public const EventKeywords ExternalEtwOnly = (EventKeywords)(1 << 5);

            /// <summary>
            /// Error events that are flagged as infrastructure issues
            /// </summary>
            public const EventKeywords InfrastructureError = (EventKeywords)(1 << 6);

            /// <summary>
            /// Error events that are flagged as User Errors
            /// </summary>
            public const EventKeywords UserError = (EventKeywords)(1 << 7);

            /// <summary>
            /// Events that should not be forwarded to the master
            /// </summary>
            public const EventKeywords NotForwardedToMaster = (EventKeywords)(1 << 8);

            /// <summary>
            /// Events that should be in included only custom logs which selectively enable the event
            /// </summary>
            public const EventKeywords SelectivelyEnabled = (EventKeywords)(1 << 9);
        }

        /// <summary>
        /// DO NOT USE!! DO NOT CONSUME IN NEW LOG MESSAGES!!!!
        /// Only for use while transitioning our logging. Telemetry will cry if it sees any of these
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2211:NonConstantFieldsShouldNotBeVisible", Justification = "Needed until we convert everything to pass logging contexts.")]
        public static LoggingContext StaticContext = new LoggingContext("DummyStatic");

        /// <summary>
        /// Major functional areas of the entire product.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
        [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible", Justification = "Needed by event infrastructure.")]
        public static class Tasks
        {
            // Do not use Task == 0. This results in EventSource auto-assigning a task number (to ensure inner despair).
            public const EventTask Scheduler = (EventTask)1;

            public const EventTask Parser = (EventTask)2;

            public const EventTask Storage = (EventTask)3;

            public const EventTask UnitTest = (EventTask)4;

            public const EventTask Transformers = (EventTask)5;

            public const EventTask Engine = (EventTask)6;

            public const EventTask Viewer = (EventTask)7;

            public const EventTask UnitTest2 = (EventTask)8;

            public const EventTask PipExecutor = (EventTask)9;

            public const EventTask ChangeJournalService = (EventTask)10;

            public const EventTask HostApplication = (EventTask)11;

            public const EventTask CommonInfrastructure = (EventTask)12;

            public const EventTask CacheInteraction = (EventTask)13;

            public const EventTask Debugger = (EventTask)14;

            public const EventTask Analyzers = (EventTask)15;

            public const EventTask PipInputAssertions = (EventTask)16;

            public const EventTask Distribution = (EventTask)17;

            public const EventTask CriticalPaths = (EventTask)18;

            public const EventTask ChangeDetection = (EventTask)19;

            public const EventTask Unclassified = (EventTask)20;

            public const EventTask LanguageServer = (EventTask)21;

            public const EventTask ExecutionAnalyzers = (EventTask)22;

            /// <summary>
            /// Highest-ordinal task.
            /// </summary>
            /// <remarks>
            /// This must be updated when a task is added.
            /// </remarks>
            public const EventTask Max = ExecutionAnalyzers;
        }

        private static readonly Events s_log = new Events();

        [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        static Events()
        {
            // we must declare a static ctor in order to trigger beforefieldinit semantics. Otherwise the log
            // gets created before the listener exists and that leads to failures cause the listener doesn't
            // recognize keywords for some reason
        }

        private Events()
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
        }

        /// <summary>
        /// Gets the primary BuildXL event source instance.
        /// </summary>
        public static Events Log
        {
            get { return s_log; }
        }

        /// <summary>
        /// Whether user passed any diagnostics argument via /diag:
        /// </summary>
        public bool HasDiagnosticsArgument { get; set; }

        private object m_syncLock = new object();

        private HashSet<BaseEventListener> m_listeners = new HashSet<BaseEventListener>();

        private HashSet<EventSource> m_mergedEventSources = new HashSet<EventSource>();

        /// <summary>
        /// Gets the current set of merged event sources
        /// </summary>
        public IReadOnlyCollection<EventSource> MergedEventSources
        {
            get
            {
                lock (m_syncLock)
                {
                    return new List<EventSource>(m_mergedEventSources);
                }
            }
        }

        /// <summary>
        /// Registers a listener to ensure the listener listens to events for all merged event sources
        /// </summary>
        [NonEvent]
        public void RegisterEventListener(BaseEventListener listener)
        {
            lock (m_syncLock)
            {
                if (m_listeners.Add(listener))
                {
                    foreach (var mergedEventSource in m_mergedEventSources)
                    {
                        listener.RegisterEventSource(mergedEventSource);
                    }
                }
            }
        }

        /// <summary>
        /// Unregisters a listener and ensures all events for merged event sources are disabled
        /// </summary>
        [NonEvent]
        public void UnregisterEventListener(BaseEventListener listener, bool disableEvents = false)
        {
            lock (m_syncLock)
            {
                if (m_listeners.Remove(listener))
                {
                    if (disableEvents)
                    {
                        foreach (var mergedEventSource in m_mergedEventSources)
                        {
                            listener.DisableEvents(mergedEventSource);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Registers an event source and registers with all registed listeners
        /// </summary>
        [NonEvent]
        public void RegisterMergedEventSource(EventSource mergedEventSource)
        {
            if (mergedEventSource.ConstructionException != null)
            {
                throw mergedEventSource.ConstructionException;
            }

            lock (m_syncLock)
            {
                if (m_mergedEventSources.Add(mergedEventSource))
                {
                    // TODO: Remove the code below. When we register the GeneratedEventSources (ETW loggers), there is no listeners created.
                    foreach (var listener in m_listeners)
                    {
                        listener.RegisterEventSource(mergedEventSource);
                    }
                }
            }
        }

        private bool m_eventSourceWarningsEncountered;

        /// <summary>
        /// Flag indicating if any event-writing errors have been encountered by this event source, e.g. due to buffers too small for an ETW session.
        /// </summary>
        public bool HasEventWriteFailures
        {
            get { return Volatile.Read(ref m_eventSourceWarningsEncountered); }
        }

        [NonEvent]
        internal void OnEventWriteFailure()
        {
            Volatile.Write(ref m_eventSourceWarningsEncountered, true);
        }

        #region Log helpers

        /// <summary>
        /// Logs an event with provenance information
        /// </summary>
        public static void LogWithProvenance(LoggingContext loggingContext, Action<LoggingContext, string, int, int> eventAction, PathTable pathTable, LocationData token)
        {
            Contract.Requires(eventAction != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(token != null);
            eventAction(loggingContext, token.Path.ToString(pathTable), token.Line, token.Position);
        }

        /// <summary>
        /// Logs an event with provenance information
        /// </summary>
        public static void LogWithProvenance<T0>(LoggingContext loggingContext, Action<LoggingContext, string, int, int, T0> eventAction, PathTable pathTable, LocationData token, T0 arg0)
        {
            Contract.Requires(eventAction != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(token != null);
            eventAction(loggingContext, token.Path.ToString(pathTable), token.Line, token.Position, arg0);
        }

        /// <summary>
        /// Logs an event with provenance information
        /// </summary>
        public static void LogWithProvenance<T0, T1>(LoggingContext loggingContext, Action<LoggingContext, string, int, int, T0, T1> eventAction, PathTable pathTable, LocationData token, T0 arg0, T1 arg1)
        {
            Contract.Requires(eventAction != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(token != null);
            eventAction(loggingContext, token.Path.ToString(pathTable), token.Line, token.Position, arg0, arg1);
        }

        /// <summary>
        /// Logs an event with provenance information
        /// </summary>
        public static void LogWithProvenance<T0, T1, T2>(LoggingContext loggingContext, Action<LoggingContext, string, int, int, T0, T1, T2> eventAction, PathTable pathTable, LocationData token, T0 arg0, T1 arg1, T2 arg2)
        {
            Contract.Requires(eventAction != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(token != null);
            eventAction(loggingContext, token.Path.ToString(pathTable), token.Line, token.Position, arg0, arg1, arg2);
        }

        public static void LogRelatedLocation(
            LoggingContext loggingContext,
            Action<LoggingContext, string, int, int, string, int, int> eventAction,
            PathTable pathTable,
            LocationData relatedLocation,
            LocationData alreadyReportedLocation)
        {
            Contract.Requires(eventAction != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(relatedLocation != null);
            Contract.Requires(alreadyReportedLocation != null);
            eventAction(loggingContext, relatedLocation.Path.ToString(pathTable), relatedLocation.Line, relatedLocation.Position, alreadyReportedLocation.Path.ToString(pathTable), alreadyReportedLocation.Line, alreadyReportedLocation.Position);
        }

        #endregion

        // this method is internal to allow access from unit tests
        [NonEvent]
        internal static string GetReflectionExceptionMessage(Exception exception)
        {
            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                StringBuilder builder = wrap.Instance;
                builder.AppendLine();

                builder.AppendLine(exception.Message);

                var reflectionTypeLoadException = exception as ReflectionTypeLoadException;
                if (reflectionTypeLoadException != null)
                {
                    builder.AppendLine("LoaderExceptions:");
                    foreach (Exception loaderException in reflectionTypeLoadException.LoaderExceptions)
                    {
                        builder.AppendLine(loaderException.ToStringDemystified());
                    }
                }

                return builder.ToString();
            }
        }

        #region Testing

        /////////////////////////
        //
        // The remaining events are for testing purposes, they are produced and consumed by the unit tests.
        //
        /////////////////////////

        [Event(
            (int)EventId.VerboseEvent,
            Level = EventLevel.Verbose,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void VerboseEvent(string message)
        {
            WriteEvent((int)EventId.VerboseEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEvent,
            Level = EventLevel.Verbose,
            Task = Tasks.UnitTest,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEvent(string message)
        {
            WriteEvent((int)EventId.DiagnosticEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEventInOtherTask,
            Level = EventLevel.Verbose,
            Task = Tasks.UnitTest2,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEventInOtherTask(string message)
        {
            WriteEvent((int)EventId.DiagnosticEventInOtherTask, message);
        }

        [Event(
            (int)EventId.InfoEvent,
            Level = EventLevel.Informational,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void InfoEvent(string message)
        {
            WriteEvent((int)EventId.InfoEvent, message);
        }

        [Event(
            (int)EventId.WarningEvent,
            Level = EventLevel.Warning,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void WarningEvent(string message)
        {
            WriteEvent((int)EventId.WarningEvent, message);
        }

        [Event(
            (int)EventId.ErrorEvent,
            Level = EventLevel.Error,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void ErrorEvent(string message)
        {
            WriteEvent((int)EventId.ErrorEvent, message);
        }

        [Event(
            (int)EventId.InfrastructureErrorEvent,
            Level = EventLevel.Error,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.InfrastructureError,
            Message = "{0}")]
        public void InfrastructureErrorEvent(string message)
        {
            WriteEvent((int)EventId.InfrastructureErrorEvent, message);
        }

        [Event(
            (int)EventId.UserErrorEvent,
            Level = EventLevel.Error,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.UserError,
            Message = "{0}")]
        public void UserErrorEvent(string message)
        {
            WriteEvent((int)EventId.UserErrorEvent, message);
        }

        [Event(
            (int)EventId.CriticalEvent,
            Level = EventLevel.Critical,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void CriticalEvent(string message)
        {
            WriteEvent((int)EventId.CriticalEvent, message);
        }

        [Event(
            (int)EventId.AlwaysEvent,
            Level = EventLevel.LogAlways,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void AlwaysEvent(string message)
        {
            WriteEvent((int)EventId.AlwaysEvent, message);
        }

        [Event(
            (int)EventId.VerboseEventWithProvenance,
            Level = EventLevel.Verbose,
            Task = Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = ProvenancePrefix + "{3}")]
        public void VerboseEventWithProvenance(string file, int line, int column, string message)
        {
            WriteEvent((int)EventId.VerboseEventWithProvenance, file, line, column, message);
        }

        #endregion

        [Event(
            (int)EventId.StartViewer,
            Level = EventLevel.Informational,
            Keywords = Keywords.Performance | Keywords.UserMessage,
            Task = Tasks.Viewer,
            Message = PhasePrefix + "Starting viewer @ {0} (async)")]
        public void StartViewer(string address)
        {
            WriteEvent(
                (int)EventId.StartViewer,
                address);
        }

        [Event(
            (int)EventId.UnableToStartViewer,
            Level = EventLevel.Warning,
            Keywords = Keywords.UserMessage,
            Task = Tasks.Viewer,
            Message = "Unable to start viewer: {0}")]
        public void UnableToStartViewer(string message)
        {
            WriteEvent(
                (int)EventId.UnableToStartViewer,
                message);
        }

        [Event(
            (int)EventId.UnableToLaunchViewer,
            Level = EventLevel.Warning,
            Keywords = Keywords.UserMessage,
            Task = Tasks.Viewer,
            Message = "Unable to launch viewer: {0}")]
        public void UnableToLaunchViewer(string message)
        {
            WriteEvent(
                (int)EventId.UnableToLaunchViewer,
                message);
        }
    }
}
