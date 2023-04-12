// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using BuildXL.Distribution.Grpc;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities.Core;
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
                    PipProcessEvent pipProcessEvent = null;
                    if (eventData.EventId == (int)LogEventId.PipProcessError)
                    {
                        var pipProcessErrorEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: false, isPipProcessError: true);
                        pipProcessEvent = new PipProcessEvent()
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

                    if (eventData.EventId == (int)LogEventId.PipProcessWarning)
                    {
                        var pipProcessWarningEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: false, isPipProcessError: false);
                        pipProcessEvent = new PipProcessEvent()
                        {
                            PipSemiStableHash = pipProcessWarningEventFields.PipSemiStableHash,
                            PipDescription = pipProcessWarningEventFields.PipDescription,
                            PipWorkingDirectory = pipProcessWarningEventFields.PipWorkingDirectory,
                            PipExe = pipProcessWarningEventFields.PipExe,
                            OutputToLog = pipProcessWarningEventFields.OutputToLog,
                            MessageAboutPathsToLog = pipProcessWarningEventFields.MessageAboutPathsToLog,
                            PathsToLog = pipProcessWarningEventFields.PathsToLog,
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
                        PipProcessEvent = pipProcessEvent,
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
