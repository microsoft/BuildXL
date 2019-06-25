// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
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
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and demultiplexes into a set of
    /// abstract methods for use by
    /// derived classes.
    /// </summary>
    public abstract class BaseEventListener : EventListener
    {
        /// <summary>
        /// Used to control the handling of a specific warning number flowing through the event infrastructure.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
        public delegate WarningState WarningMapper(int warningNumber);

        /// <summary>
        /// Used to receive a notification that the listener has latched to a disabled state due to a failure to write to disk
        /// (applies to many but not all listeners).
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
        public delegate void DisabledDueToDiskWriteFailureEventHandler(BaseEventListener sender);

        /// <summary>
        /// Match-any keywords when diagnostics are not enabled.
        /// </summary>
        private const EventKeywords NormalKeywords = Keywords.UserMessage | Keywords.Progress | Keywords.SelectivelyEnabled;

        /// <summary>
        /// Match-any keywords when diagnostics are enabled.
        /// </summary>
        private const EventKeywords DiagnosticsKeywords = NormalKeywords | Keywords.Diagnostics;

        private readonly Events m_eventSource;
        private readonly EventLevel m_level;
        private readonly object m_lock = new object();
        private readonly WarningMapper m_warningMapper;
        private readonly DisabledDueToDiskWriteFailureEventHandler m_disabledDueToDiskWriteFailureEventHandler;
        private readonly EventMask m_eventMask;

        /// <summary>
        /// See <see cref="SuppressNonCriticalEventsInPreparationForCrash"/>
        /// </summary>
        private bool m_limitToCriticalLevelOnly = false;

        /// <summary>
        /// Latches to true if an event-write to this listener fails due to exhaustion of space on a volume
        /// (based on <see cref="ExceptionUtilities.AnalyzeExceptionRootCause"/>)
        /// </summary>
        private bool m_disabledDueToDiskWriteFailure = false;

        /// <summary>
        /// Latches to true if any diagnostic-keyword events have been enabled for this listener.
        /// </summary>
        private readonly bool m_diagnosticsEnabled;

        /// <summary>
        /// For each task in <see cref="Tasks"/>, indicates if diagnostic events for that task should be observed by this listener.
        /// </summary>
        private readonly bool[] m_enableTaskDiagnostics = new bool[(int)BuildXL.Utilities.Instrumentation.Common.Tasks.Max + 1];

        /// <summary>
        /// Initializes an instance.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="level">
        /// The base level of data to be sent to the listener.
        /// </param>
        /// <param name="captureAllDiagnosticMessages">
        /// If true, all messages tagged with <see cref="Keywords.Diagnostics" /> at or above <paramref name="level"/> are captured (rather than needing to be enabled per-task).
        /// </param>
        /// <param name="eventMask">
        /// If specified, an EventMask that allows selectively enabling or disabling events
        /// </param>
        /// <param name="onDisabledDueToDiskWriteFailure">
        /// If specified, called if the listener encounters a disk-write failure such as an out of space condition.
        /// Otherwise, such conditions will throw an exception.
        /// </param>
        /// <param name="listenDiagnosticMessages">
        /// If true, all messages tagged with <see cref="Keywords.Diagnostics" /> at or above <paramref name="level"/> are enabled but not captured unless diagnostics are enabled per-task.
        /// This is useful for StatisticsEventListener, where you need to listen diagnostics messages but capture only ones tagged with CommonInfrastructure task.
        /// </param>
        protected BaseEventListener(
            Events eventSource,
            WarningMapper warningMapper,
            EventLevel level = EventLevel.Verbose,
            bool captureAllDiagnosticMessages = false,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            bool listenDiagnosticMessages = false)
        {
            Contract.Requires(eventSource != null);

            m_eventSource = eventSource;
            m_level = level;
            m_warningMapper = warningMapper;
            m_disabledDueToDiskWriteFailureEventHandler = onDisabledDueToDiskWriteFailure;
            m_eventMask = eventMask;

            // If user gives a /diag argument or listenDiagnosticMessage is passed as true to the constructor,
            // diagnostics messages should be enabled. At this point, we do not know whether this event listener will capture them.
            // Capturing the diagnostic messages depends on whether EnableTaskDiagnostics will be called or not.
            // If the /diag argument exists, EnableTaskDiagnostics method will always be called.
            m_diagnosticsEnabled = eventSource.HasDiagnosticsArgument || listenDiagnosticMessages;

            // captureAllDiagnosticMessages is true only for TestEventListeners.
            if (captureAllDiagnosticMessages)
            {
                m_diagnosticsEnabled = true;
                for (int i = 0; i < m_enableTaskDiagnostics.Length; i++)
                {
                    m_enableTaskDiagnostics[i] = true;
                }
            }

            EnableEvents(eventSource, level, m_diagnosticsEnabled ? DiagnosticsKeywords : NormalKeywords);

            eventSource.RegisterEventListener(this);
        }

        /// <summary>
        /// Registers an additional event source
        /// </summary>
        public virtual void RegisterEventSource(EventSource eventSource)
        {
            EnableEvents(eventSource, m_level, m_diagnosticsEnabled ? DiagnosticsKeywords : NormalKeywords);
        }

        /// <summary>
        /// Enables <see cref="Keywords.Diagnostics"/> events (at the listener's level) for the given <paramref name="task"/>.
        /// These events may have been excluded from this listener by default.
        /// </summary>
        public void EnableTaskDiagnostics(EventTask task)
        {
            lock (m_lock)
            {
                m_enableTaskDiagnostics[(int)task] = true;
            }
        }

        /// <summary>
        /// Overrides the configured event level such that only <see cref="EventLevel.Critical"/> events are handled.
        /// This is in preparation for a user-friendly(ish) crash (the critical-level crash details should be visible in preference
        /// to anything else).
        /// </summary>
        public void SuppressNonCriticalEventsInPreparationForCrash()
        {
            lock (m_lock)
            {
                m_limitToCriticalLevelOnly = true;
            }
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            m_eventSource.UnregisterEventListener(this);
            base.Dispose();
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0", Justification = "It IS indeed validated")]
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Ensure this event which routes text log messages to ETW is not handled by
            // event listeners
            if (eventData.EventId == (int)EventId.TextLogEtwOnly)
            {
                return;
            }

            // If diagnostic events are enabled, we may see events not matching the NormalKeywords.
            // In that case we further filter based on the event's Task. See EnableTaskDiagnostics.
            // Keywords == 0 acts like a wildcard (all keywords); see EventSource sources. For some reason EventSource sets some top bits.
            if (unchecked((uint)eventData.Keywords) != 0)
            {
                if ((eventData.Keywords & NormalKeywords) == 0 &&
                    (eventData.Keywords & DiagnosticsKeywords) != 0 &&
                    (eventData.Task > BuildXL.Utilities.Instrumentation.Common.Tasks.Max || !m_enableTaskDiagnostics[unchecked((int)eventData.Task)]))
                {
                    return;
                }

                if ((eventData.Keywords & Keywords.SelectivelyEnabled) != 0 &&
                    (m_eventMask?.IsSelectivelyEnabled != true))
                {
                    // Exclude any event which is selectively enabled if this event listener does
                    // not selectively enable events (i.e. specify specific event ids which should not be
                    // excluded)
                    return;
                }
            }

            // Out-of-band messages from EventListener.ReportOutOfBandMessage end up with event ID 0.
            // We don't allow these messages to be upgraded to errors by the WarningMapper since they typically
            // are not actionable.
            bool isOutOfBand = eventData.EventId == 0;
            EventLevel level = isOutOfBand ? EventLevel.Warning : eventData.Level;
            bool suppressedWarning = false;
            if (level == EventLevel.Warning && !isOutOfBand)
            {
                if (m_warningMapper != null)
                {
                    switch (m_warningMapper(eventData.EventId))
                    {
                        case WarningState.AsError:
                            {
                                level = EventLevel.Error;
                                break;
                            }

                        case WarningState.AsWarning:
                            {
                                // nothing to do
                                break;
                            }

                        case WarningState.Suppressed:
                            {
                                suppressedWarning = true;
                                break;
                            }
                    }
                }
            }

            // Bail out if the event is not enabled based on the event mask
            if (m_eventMask != null && !m_eventMask.IsEnabled(level, eventData.EventId))
            {
                return;
            }

            // Derived listeners don't need to worry about locking, we do it here...
            lock (m_lock)
            {
                try
                {
                    if (m_disabledDueToDiskWriteFailure)
                    {
                        return;
                    }

                    if (m_limitToCriticalLevelOnly && level != EventLevel.Critical)
                    {
                        return;
                    }

                    // dispatch to the appropriate handler method
                    switch (level)
                    {
                        case EventLevel.Critical:
                            {
                                OnCritical(eventData);
                                break;
                            }

                        case EventLevel.Error:
                            {
                                OnError(eventData);
                                break;
                            }

                        case EventLevel.Informational:
                            {
                                OnInformational(eventData);
                                break;
                            }

                        case EventLevel.LogAlways:
                            {
                                OnAlways(eventData);
                                break;
                            }

                        case EventLevel.Verbose:
                            {
                                OnVerbose(eventData);
                                break;
                            }

                        default:
                            {
                                Contract.Assert(level == EventLevel.Warning);

                                if (isOutOfBand)
                                {
                                    OnEventSourceInternalWarning(eventData);
                                }
                                else
                                {
                                    if (!suppressedWarning)
                                    {
                                        OnWarning(eventData);
                                    }
                                    else
                                    {
                                        OnSuppressedWarning(eventData);
                                    }

                                }

                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    // Event listeners have an unfortunate distinction of being very useful for reporting terminal failures (i.e., in an unhandled exception handler).
                    // So, we need graceful failure in the event of systemic inability to write to some listeners (e.g. out of disk space): The unhandled exception handler
                    // should, in that circumstance, be able to write to any not-yet-broken listeners.
                    if (m_disabledDueToDiskWriteFailureEventHandler != null &&
                        ExceptionUtilities.AnalyzeExceptionRootCause(ex) == ExceptionRootCause.OutOfDiskSpace)
                    {
                        m_disabledDueToDiskWriteFailure = true;
                        OnDisabledDueToDiskWriteFailure();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private void OnDisabledDueToDiskWriteFailure()
        {
            Contract.Requires(m_disabledDueToDiskWriteFailureEventHandler != null);

            ThreadPool.QueueUserWorkItem(
                state => m_disabledDueToDiskWriteFailureEventHandler(this));
        }

        /// <summary>
        /// Calls the implementation of UnsynchronizedFlush within the lock used for writing events to ensure no other
        /// writes occur in conjunction with flush
        /// </summary>
        protected void SynchronizedFlush()
        {
            lock (m_lock)
            {
                if (m_disabledDueToDiskWriteFailure)
                {
                    return;
                }

                try
                {
                    UnsynchronizedFlush();
                }
                catch (Exception ex)
                {
                    // See identical handling in OnEventWritten.
                    if (m_disabledDueToDiskWriteFailureEventHandler != null &&
                        ExceptionUtilities.AnalyzeExceptionRootCause(ex) == ExceptionRootCause.OutOfDiskSpace)
                    {
                        m_disabledDueToDiskWriteFailure = true;
                        OnDisabledDueToDiskWriteFailure();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Specifies the logic to be called by <see cref="SynchronizedFlush"/> which will not overlap any writes
        /// </summary>
        protected virtual void UnsynchronizedFlush()
        {
            // Noop if not implemented
        }

        /// <nodoc />
        protected abstract void OnCritical(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected abstract void OnWarning(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected virtual void OnSuppressedWarning(EventWrittenEventArgs eventData)
        {
            // do nothing - the default behavior is to suppress the event
        }

        /// <nodoc />
        protected abstract void OnError(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected abstract void OnInformational(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected abstract void OnVerbose(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected abstract void OnAlways(EventWrittenEventArgs eventData);

        /// <nodoc />
        protected virtual void OnEventSourceInternalWarning(EventWrittenEventArgs eventData)
        {
            // TODO: It would be nice to call OnWarning(eventData); as a default action, but we cannot determine the type of event.
            // Instead for now we latch something on the event source at least, so we can log a single message that some messages were missed.
            m_eventSource.OnEventWriteFailure();
        }
    }
}
