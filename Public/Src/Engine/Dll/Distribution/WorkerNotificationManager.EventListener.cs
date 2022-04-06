// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Distribution
{
    internal sealed partial class WorkerNotificationManager
    {
        /// <summary>
        /// Event listener for error and warning events which will be forwarded to the orchestrator
        /// </summary>
        private sealed class ForwardingEventListener : FormattingEventListener
        {
            private readonly WorkerNotificationManager m_notificationManager;
            private int m_nextEventId;

            public ForwardingEventListener(WorkerNotificationManager notificationManager)
                : base(Events.Log, GetStartTime(), eventMask: GetEventMask())
            {
                m_notificationManager = notificationManager;
            }

            private static EventMask GetEventMask()
            {
                // Mask out events about failed call since the call might be
                // from trying to send the notification
                return new EventMask(
                    enabledEvents: null,
                    disabledEvents: DistributionHelpers.DistributionAllMessages.ToArray());
            }

            private static DateTime GetStartTime()
            {
                return Process.GetCurrentProcess().StartTime.ToUniversalTime();
            }

            protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
            {
                if ((level != EventLevel.Error) && (level != EventLevel.Warning))
                {
                    return;
                }

                if (((long)eventData.Keywords & (long)Keywords.NotForwardedToOrchestrator) > 0)
                { 
                    return;
                }

                try
                {
                    PipProcessErrorEvent pipProcessErrorEvent = null;
                    if (eventData.EventId == (int)LogEventId.PipProcessError)
                    {
                        var pipProcessErrorEventFields = new PipProcessErrorEventFields(eventData.Payload, false);
                        pipProcessErrorEvent = new PipProcessErrorEvent()
                        {
                            PipSemiStableHash = pipProcessErrorEventFields.PipSemiStableHash,
                            PipDescription = pipProcessErrorEventFields.PipDescription,
                            PipSpecPath = pipProcessErrorEventFields.PipSpecPath,
                            PipWorkingDirectory = pipProcessErrorEventFields.PipWorkingDirectory,
                            PipExe = pipProcessErrorEventFields.PipExe,
                            OutputToLog = pipProcessErrorEventFields.OutputToLog,
                            MessageAboutPathsToLog = pipProcessErrorEventFields.MessageAboutPathsToLog,
                            PathsToLog = pipProcessErrorEventFields.PathsToLog,
                            ExitCode = pipProcessErrorEventFields.ExitCode,
                            OptionalMessage = pipProcessErrorEventFields.OptionalMessage,
                            ShortPipDescription = pipProcessErrorEventFields.ShortPipDescription,
                            PipExecutionTimeMs = pipProcessErrorEventFields.PipExecutionTimeMs
                        };
                    }

                    m_notificationManager.ReportEventMessage(new EventMessage()
                    {
                        Id = Interlocked.Increment(ref m_nextEventId),
                        Level = (int)level,
                        EventKeywords = (long)eventData.Keywords,
                        EventId = eventData.EventId,
                        EventName = eventData.EventName,
                        Text = text,
                        PipProcessErrorEvent = pipProcessErrorEvent,
                    });
                }
                catch (Exception ex) when (ExceptionUtilities.HandleUnexpectedException(ex))
                {
                    // Do nothing. Exception filter handles the logic.
                }
            }

            /// <nodoc />
            internal void Cancel()
            {
                Events.Log.UnregisterEventListener(this);
            }
        }
    }
}
