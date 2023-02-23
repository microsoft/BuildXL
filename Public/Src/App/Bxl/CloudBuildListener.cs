// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Tracing;
using BuildXL.Scheduler.Distribution;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;

namespace BuildXL
{
    /// <summary>
    /// This event listener should only be hooked up when building inside of CloudBuild. It is used to forward errors
    /// into <see cref="Tracing.CloudBuildEventSource"/>, which writes them into ETW in a format that CloudBuild can
    /// plumb into it's web UI.
    /// </summary>
    public sealed class CloudBuildListener : FormattingEventListener
    {
        /// <nodoc />
        public CloudBuildListener(
            Events eventSource,
            DateTime baseTime,
            bool useCustomPipDescription,
            [CanBeNull] WarningMapper warningMapper)
            : base(
                  eventSource,
                  baseTime,
                  warningMapper: warningMapper,
                  level: EventLevel.Verbose,
                  captureAllDiagnosticMessages: false,
                  timeDisplay: TimeDisplay.Seconds,
                  useCustomPipDescription: useCustomPipDescription)
        {

        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
        }

        /// <inheritdoc />
        protected override void Write(
            EventWrittenEventArgs eventData,
            EventLevel level,
            string message = null,
            bool suppressEvent = false)
        {
            switch (level)
            {
                case EventLevel.Critical:
                case EventLevel.Error:
                    ProcessEvent(eventData);
                    break;
                default:
                    break;
            }
        }

        /// <nodoc />
        private void ProcessEvent(EventWrittenEventArgs eventData)
        {
            ApplyIfPipError(eventData, (pipProcessErrorEventFields, workerId) =>
            {
                string semiStableHash = Pip.FormatSemiStableHash(pipProcessErrorEventFields.PipSemiStableHash);

                Tracing.CloudBuildEventSource.Log.TargetFailedEvent(new TargetFailedEvent
                {
                    WorkerId = workerId,
                    TargetId = semiStableHash,
                    // Need to trim the payload as Batmon may not be able to display the error message if it is too long.
                    StdOutputPath = pipProcessErrorEventFields.OutputToLog?.Substring(0, 2000),
                    PipDescription = pipProcessErrorEventFields.PipDescription,
                    ShortPipDescription = pipProcessErrorEventFields.ShortPipDescription,
                    PipExecutionTimeMs = pipProcessErrorEventFields.PipExecutionTimeMs
                });
            });
        }

        /// <nodoc />
        internal static void ApplyIfPipError(EventWrittenEventArgs eventData, Action<PipProcessErrorEventFields, string> addPipErrors)
        {
            switch (eventData.EventId)
            {
                case (int)LogEventId.PipProcessError:
                {
                    addPipErrors(new PipProcessErrorEventFields(eventData.Payload, false), LocalWorker.MachineName);
                }
                break;
                case (int)SharedLogEventId.DistributionWorkerForwardedError:
                {
                    var actualEventId = (int)eventData.Payload[1];
                    if (actualEventId == (int)LogEventId.PipProcessError)
                    {
                        var pipProcessErrorEventFields = new PipProcessErrorEventFields(eventData.Payload, true);
                        addPipErrors(pipProcessErrorEventFields, (string)eventData.Payload[16]);
                    }
                }
                break;
            }
        }

    }
}
