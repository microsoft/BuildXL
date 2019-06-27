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
    // Using has to be inside namespace due to BuildXL.Utilities.Tasks;
    using Tasks = BuildXL.Utilities.Instrumentation.Common.Tasks;

    /// <summary>
    /// The BuildXL event source
    /// </summary>
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    [EventSource(Name = "Domino", LocalizationResources = "BuildXL.Utilities.Strings")]
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
        /// DO NOT USE!! DO NOT CONSUME IN NEW LOG MESSAGES!!!!
        /// Only for use while transitioning our logging. Telemetry will cry if it sees any of these
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2211:NonConstantFieldsShouldNotBeVisible", Justification = "Needed until we convert everything to pass logging contexts.")]
        public static LoggingContext StaticContext = new LoggingContext("DummyStatic");

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

        private readonly object m_syncLock = new object();

        private readonly HashSet<BaseEventListener> m_listeners = new HashSet<BaseEventListener>();

        private readonly HashSet<EventSource> m_mergedEventSources = new HashSet<EventSource>();

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

// disable warning regarding 'missing XML comments on public API'. We don't need docs for these methods
#pragma warning disable 1591

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
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void VerboseEvent(string message)
        {
            WriteEvent((int)EventId.VerboseEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEvent,
            Level = EventLevel.Verbose,
            Task= Tasks.UnitTest,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEvent(string message)
        {
            WriteEvent((int)EventId.DiagnosticEvent, message);
        }

        [Event(
            (int)EventId.DiagnosticEventInOtherTask,
            Level = EventLevel.Verbose,
            Task= Tasks.UnitTest2,
            Keywords = Keywords.Diagnostics,
            Message = "{0}")]
        public void DiagnosticEventInOtherTask(string message)
        {
            WriteEvent((int)EventId.DiagnosticEventInOtherTask, message);
        }

        [Event(
            (int)EventId.InfoEvent,
            Level = EventLevel.Informational,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void InfoEvent(string message)
        {
            WriteEvent((int)EventId.InfoEvent, message);
        }

        [Event(
            (int)EventId.WarningEvent,
            Level = EventLevel.Warning,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void WarningEvent(string message)
        {
            WriteEvent((int)EventId.WarningEvent, message);
        }

        [Event(
            (int)EventId.ErrorEvent,
            Level = EventLevel.Error,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void ErrorEvent(string message)
        {
            WriteEvent((int)EventId.ErrorEvent, message);
        }

        [Event(
            (int)EventId.InfrastructureErrorEvent,
            Level = EventLevel.Error,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.InfrastructureError,
            Message = "{0}")]
        public void InfrastructureErrorEvent(string message)
        {
            WriteEvent((int)EventId.InfrastructureErrorEvent, message);
        }

        [Event(
            (int)EventId.UserErrorEvent,
            Level = EventLevel.Error,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage | Keywords.UserError,
            Message = "{0}")]
        public void UserErrorEvent(string message)
        {
            WriteEvent((int)EventId.UserErrorEvent, message);
        }

        [Event(
            (int)EventId.CriticalEvent,
            Level = EventLevel.Critical,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void CriticalEvent(string message)
        {
            WriteEvent((int)EventId.CriticalEvent, message);
        }

        [Event(
            (int)EventId.AlwaysEvent,
            Level = EventLevel.LogAlways,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = "{0}")]
        public void AlwaysEvent(string message)
        {
            WriteEvent((int)EventId.AlwaysEvent, message);
        }

        [Event(
            (int)EventId.VerboseEventWithProvenance,
            Level = EventLevel.Verbose,
            Task= Tasks.UnitTest,
            Keywords = Keywords.UserMessage,
            Message = EventConstants.ProvenancePrefix + "{3}")]
        public void VerboseEventWithProvenance(string file, int line, int column, string message)
        {
            WriteEvent((int)EventId.VerboseEventWithProvenance, file, line, column, message);
        }

        #endregion

        [Event(
            (int)EventId.StartViewer,
            Level = EventLevel.Informational,
            Keywords = Keywords.Performance | Keywords.UserMessage,
            Task= Tasks.Viewer,
            Message = EventConstants.PhasePrefix + "Starting viewer @ {0} (async)")]
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
