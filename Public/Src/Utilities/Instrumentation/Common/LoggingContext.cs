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
        /// The logging queue used for asynchronous logging
        /// </summary>
        private readonly ILoggingQueue m_loggingQueue;

        /// <summary>
        /// Gets whether asynchronous logging is enabled
        /// </summary>
        public bool IsAsyncLoggingEnabled => m_loggingQueue != null;

        /// <summary>
        /// Errors logged by event ID.
        /// This will only be populated for the root context and should be exclusively accessed through <see cref="ErrorsLoggedById"/>.
        /// Lazy since most of the time there will be no errors.
        /// </summary>
        private readonly Lazy<ConcurrentBag<ushort>> m_errorsLoggedById = new Lazy<ConcurrentBag<ushort>>(() => new ConcurrentBag<ushort>());

        /// <summary>
        /// Errors logged by event ID
        /// </summary>
        public ConcurrentBag<ushort> ErrorsLoggedById
        {
            get
            {
                return GetRootContext().m_errorsLoggedById.Value;
            }
        }

        /// <summary>
        /// Creates an instance of Context
        /// </summary>
        public LoggingContext(Guid activityId, string loggerComponentInfo, SessionInfo session, LoggingContext parent = null, ILoggingQueue loggingQueue = null)
        {
            // TODO: we want to always have a component info for debugging purposes.
            // However right now, PerformanceMeasurement and TimedBlock allow nulls and their behavior depends on whether this vaslue is null.
            // Fix these classes and enable this contract check.
            // Contract.Requires(loggerComponentInfo != null);
            Contract.Requires(session != null);

            ActivityId = activityId;
            LoggerComponentInfo = loggerComponentInfo;
            Session = session;
            Parent = parent;
            m_loggingQueue = loggingQueue ?? parent?.m_loggingQueue;
        }

        /// <summary>
        /// Creates a new Context for use as the root Context of the application
        /// </summary>
        /// <param name="loggerComponentInfo">The component that calls the log, e.g. Class.Method.</param>
        /// <param name="environment">Identifies the environment the code is being run in, e.g., dev, test, prod.
        /// It is best to limit the variability of these identifiers if bucketing by them in SkypeRV's heuristics API</param>
        public LoggingContext(string loggerComponentInfo, string environment = null)
            : this(Guid.NewGuid(), loggerComponentInfo, new SessionInfo(Guid.NewGuid().ToString(), environment ?? DefaultEnvironment, Guid.Empty))
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
        /// Specifies that an error was logged
        /// </summary>
        public void SpecifyErrorWasLogged(ushort eventId)
        {
            ErrorsLoggedById.Add(eventId);
            Interlocked.Increment(ref GetRootContext().m_errorsLogged);
        }

        /// <summary>
        /// Specifies that an warning was logged
        /// </summary>
        public void SpecifyWarningWasLogged()
        {
            Interlocked.Increment(ref GetRootContext().m_warningsLogged);
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

        /// <summary>
        /// Enqueues an asynchronous log action for the given event
        /// </summary>
        public void EnqueueLogAction(int eventId, Action logAction, string eventName)
        {
            Contract.Requires(IsAsyncLoggingEnabled);
            m_loggingQueue.EnqueueLogAction(eventId, logAction, eventName);
        }

        /// <summary>
        /// Absorbs the state of another logging context, with respect to errors and warnings logged
        /// </summary>
        internal void AbsorbLoggingContextState(LoggingContext loggingContext)
        {
            m_errorsLogged += loggingContext.m_errorsLogged;
            m_warningsLogged += loggingContext.m_warningsLogged;
            foreach (var item in loggingContext.m_errorsLoggedById.Value)
            {
                m_errorsLoggedById.Value.Add(item);
            }
        }
    }
}
