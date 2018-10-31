// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Context passed to generated logging methods
    /// </summary>
    public class LoggingContext
    {
        /// <summary>
        /// Session information. This part of the context is the same for the whole session.
        /// Used for AriaV2 reporting and PipCacheDescriptor provenance.
        /// </summary>
        public sealed class SessionInfo
        {
            /// <summary>
            /// Unique ID which will be assigned to a given session.
            /// </summary>
            public readonly string Id;

            /// <summary>
            /// Identifies the environment the code is being run in, e.g., dev, test, prod
            /// It is best to limit the variability of these identifiers if bucketing by them in SkypeRV's heuristics API
            /// </summary>
            public readonly string Environment;

            /// <summary>
            /// Gets the related activity id for the session
            /// </summary>
            public readonly Guid RelatedActivityId;

            /// <summary>
            /// Gets the related session id for the session
            /// </summary>
            public readonly string RelatedId;

            /// <summary>
            /// Create a new session info
            /// </summary>
            public SessionInfo(string id, string environment, Guid relatedActivityId)
            {
                // Currently we use Guids as session identifiers.
                // Verify that the passed string is actually a Guid.
                Contract.Requires(StringIsGuid(id));

                Id = id;
                Environment = environment;
                RelatedActivityId = relatedActivityId;
                RelatedId = relatedActivityId.ToString();
            }

            /// <summary>
            /// Check if a string is a Guid
            /// </summary>
            [Pure]
            public static bool StringIsGuid(string s)
            {
                Guid result;
                return Guid.TryParse(s, out result);
            }
        }

        private const string DefaultEnvironment = "Unset";

        /// <summary>
        /// Session information
        /// </summary>
        public readonly SessionInfo Session;

        /// <summary>
        /// A back pointer to the parent logging context.
        /// </summary>
        public readonly LoggingContext Parent;

        /// <summary>
        /// The maximum level of events to log
        /// </summary>
        /// <remarks>
        /// Events higher than the specified here won't be logged
        /// </remarks>
        public LogEventLevel MaximumLevelToLog { get; }

        /// <summary>
        /// Unique ID to trace the activity in a session
        /// </summary>
        public readonly Guid ActivityId;

        /// <summary>
        /// The component that calls the log, e.g. Class.Method
        /// </summary>
        public readonly string LoggerComponentInfo;

        /// <summary>
        /// Return the activity id of the parent context.
        /// </summary>
        public Guid ParentActivityId => Parent != null ? Parent.ActivityId : Guid.Empty;

        /// <summary>
        /// Count of errors that were logged. This is only set for the highest parent context.
        /// </summary>
        private int m_errorsLogged = 0;

        /// <summary>
        /// Count of warnings that were logged. This is only set for the highest parent context.
        /// </summary>
        private int m_warningsLogged = 0;

        /// <summary>
        /// Count of verbose events that were logged. This is only set for the highest parent context.
        /// </summary>
        private int m_verboseLogged = 0;

        /// <summary>
        /// Events logged by event ID. 
        /// This will only be populated for the root context and should be exclusively accessed through <see cref="EventsLoggedById"/>.
        /// Lazy since most of the time there will be no errors.
        /// </summary>
        private Lazy<ConcurrentDictionary<int, long>> m_eventsLoggedById = new Lazy<ConcurrentDictionary<int, long>>(() => new ConcurrentDictionary<int, long>());

        /// <summary>
        /// Errors logged by event ID
        /// </summary>
        public ConcurrentDictionary<int, long> EventsLoggedById
        {
            get
            {
                return GetRootContext().m_eventsLoggedById.Value;
            }
        }

        /// <summary>
        /// Creates an instance of Context
        /// </summary>
        public LoggingContext(Guid activityId, string loggerComponentInfo, SessionInfo session, LoggingContext parent = null, LogEventLevel maximumLevelToLog = LogEventLevel.Warning)
        {
            // TODO: we want to always have a component info for debugging purposes.
            // However right noe PerformanceMeasurement and TimedBlock allow nulls and their behavior depends on whether this vaslue is null.
            // Fix these classes and enable this contract check.
            // Contract.Requires(loggerComponentInfo != null);
            Contract.Requires(session != null);

            ActivityId = activityId;
            LoggerComponentInfo = loggerComponentInfo;
            Session = session;
            Parent = parent;
            MaximumLevelToLog = maximumLevelToLog;
        }

        /// <summary>
        /// Creates a new Context for use as the root Context of the application
        /// </summary>
        /// <param name="loggerComponentInfo">The component that calls the log, e.g. Class.Method.</param>
        /// <param name="environment">Identifies the environment the code is being run in, e.g., dev, test, prod.
        /// <param name="maximumLevelToLog">The maximum level of the event to log. e.g. LogEventLevel.Warning will only log warnings and errors</param>
        /// It is best to limit the variability of these identifiers if bucketing by them in SkypeRV's heuristics API</param>
        public LoggingContext(string loggerComponentInfo, string environment = null, LogEventLevel maximumLevelToLog = LogEventLevel.Warning)
            : this(Guid.NewGuid(), loggerComponentInfo, new SessionInfo(Guid.NewGuid().ToString(), environment ?? DefaultEnvironment, Guid.Empty), maximumLevelToLog: maximumLevelToLog)
        {
        }

        /// <summary>
        /// Creates a new Context that is a child of an existing Context
        /// </summary>
        /// <param name="parent">The parent context.</param>
        /// <param name="loggerComponentInfo">The component that calls the log, e.g. Class.Method.</param>
        /// <param name="activityId">Activity id.</param>
        public LoggingContext(LoggingContext parent, string loggerComponentInfo, Guid? activityId = null)
            : this(activityId ?? Guid.NewGuid(), loggerComponentInfo, parent.Session, parent)
        {
            Contract.Requires(parent != null);
        }

        /// <summary>
        /// True if an error was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public long TotalErrorsLogged => Volatile.Read(ref GetRootContext().m_errorsLogged);


        /// <summary>
        /// True if an error was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public bool ErrorWasLogged => TotalErrorsLogged > 0;

        /// <summary>
        /// True if an warning was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public long TotalWarningsLogged => Volatile.Read(ref GetRootContext().m_warningsLogged);

        /// <summary>
        /// True if an warning was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public bool WarningWasLogged => TotalWarningsLogged > 0;

        /// <summary>
        /// True if an warning was logged in this context.
        /// </summary>
        [Pure]
        public bool WarningWasLoggedByThisLogger => Volatile.Read(ref m_warningsLogged) > 0;

        /// <summary>
        /// True if a verbose event was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public long TotalVerboseLogged => Volatile.Read(ref GetRootContext().m_verboseLogged);

        /// <summary>
        /// True if a verbose event was logged in this context or parent contexts.
        /// </summary>
        [Pure]
        public bool VerboseWasLogged => TotalVerboseLogged > 0;

        /// <summary>
        /// Total number of events logged. Includes verbose events, warnings and errors
        /// </summary>
        public long TotalEventsLogged => TotalVerboseLogged + TotalWarningsLogged + TotalErrorsLogged;

        /// <summary>
        /// Specifies that an error was logged
        /// </summary>
        public void SpecifyErrorWasLogged(ushort eventId)
        {
            AddEventWithId(eventId);
            Interlocked.Increment(ref GetRootContext().m_errorsLogged);
        }

        /// <summary>
        /// Specifies that an warning was logged
        /// </summary>
        public void SpecifyWarningWasLogged(ushort eventId)
        {
            AddEventWithId(eventId);
            Interlocked.Increment(ref GetRootContext().m_warningsLogged);
        }

        /// <summary>
        /// Specifies that a verbose event was logged
        /// </summary>
        public void SpecifyVerboseWasLogged(ushort eventId)
        {
            AddEventWithId(eventId);
            Interlocked.Increment(ref GetRootContext().m_verboseLogged);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            AddPathToString(stringBuilder);
            return stringBuilder.ToString();
        }

        /// <summary>
        /// Root context.
        /// </summary>
        public LoggingContext GetRootContext()
        {
            LoggingContext context = this;
            while (context.Parent != null)
            {
                context = context.Parent;
            }

            return context;
        }

        private void AddPathToString(StringBuilder stringBuilder)
        {
            if (Parent != null)
            {
                Parent.AddPathToString(stringBuilder);
                stringBuilder.Append('/');
            }

            stringBuilder.Append(LoggerComponentInfo ?? "<null>");  // TODO: remove the null check if the constructor checks for not-null
        }

        private void AddEventWithId(ushort eventId)
        {
            m_eventsLoggedById.Value.AddOrUpdate(eventId, 1, (_, count) => count + 1);
        }
    }
}
