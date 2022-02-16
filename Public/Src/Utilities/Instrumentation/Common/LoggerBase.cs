// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Base class for all loggers
    /// </summary>
    public abstract class LoggerBase
    {
        /// <nodoc />
        public bool PreserveLogEvents { get; protected set; }

        /// <nodoc />
        public bool InspectMessageEnabled { get; protected set; }

        private readonly ConcurrentQueue<Diagnostic> m_capturedDiagnostics = new ConcurrentQueue<Diagnostic>();

        private readonly object m_observersModifyLock = new object();
        private ILogMessageObserver[]? m_messageObservers;

        private int m_errorCount;

        /// <summary>
        /// True when at least one error occurred. The logger must have 
        /// </summary>

        public bool HasErrors => ErrorCount != 0;

        /// <summary>
        /// Returns number of errors.
        /// </summary>
        public int ErrorCount
        {
            get
            {
                Contract.Requires(InspectMessageEnabled);
                return m_errorCount;
            }
        }

        /// <summary>
        /// Gets a copy of the captured diagnostic messages
        /// </summary>
        public IReadOnlyList<Diagnostic> CapturedDiagnostics
        {
            get
            {
                Contract.Requires(InspectMessageEnabled);
                return m_capturedDiagnostics.ToList();
            }
        }

        /// <summary>
        /// Hook for inspecting log messages on a logger
        /// </summary>
        public void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            var diagnostic = new Diagnostic(logEventId, level, message, location);

            if (PreserveLogEvents)
            {
                m_capturedDiagnostics.Enqueue(diagnostic);
            }

            if (m_messageObservers != null)
            {
                foreach (var observer in m_messageObservers)
                {
                    observer.OnMessage(diagnostic);
                }
            }

            if (diagnostic.IsError)
            {
                Interlocked.Increment(ref m_errorCount);
            }
        }

        /// <summary>
        /// See <see cref="LoggingContext.EnqueueLogAction"/>.
        /// </summary>
        protected static void EnqueueLogAction(LoggingContext loggingContext, int logEventId, Action logAction, [CallerMemberName] string? eventName = null)
        {
            loggingContext.EnqueueLogAction(logEventId, logAction, eventName);
        }

        /// <nodoc />
        public void AddObserver(ILogMessageObserver observer)
        {
            // Adding an observer implies we want to inspect messages.
            InspectMessageEnabled = true;

            lock (m_observersModifyLock)
            {
                m_messageObservers = m_messageObservers ?? Array.Empty<ILogMessageObserver>();

                if (observer != null && !m_messageObservers.Contains(observer))
                {
                    m_messageObservers = m_messageObservers.Concat(new[] { observer }).ToArray();
                }
            }
        }

        /// <nodoc />
        public void RemoveObserver(ILogMessageObserver observer)
        {
            lock (m_observersModifyLock)
            {
                if (observer != null && (m_messageObservers?.Contains(observer) == true))
                {
                    m_messageObservers = m_messageObservers.Where(o => o != observer).ToArray();
                }
            }
        }

        /// <summary>
        /// Tries to empty the collection of diagnostics.
        /// </summary>
        /// <returns>Whether it succeeded emptying the diagnostics</returns>
        public bool TryClearCapturedDiagnostics()
        {
            while (!m_capturedDiagnostics.IsEmpty)
            {
                if (!m_capturedDiagnostics.TryDequeue(out Diagnostic result))
                {
                    return false;
                }

                if (result.IsError)
                {
                    Interlocked.Decrement(ref m_errorCount);
                }
            }

            return true;
        }

    }
}
