// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Tracing;
using BuildXL.Scheduler.Distribution;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using JetBrains.Annotations;

namespace BuildXL
{
    /// <summary>
    /// This event listener should only be hooked up when AzureDevOps optimized UI is requested by
    /// the user via the /ado command-line flag.
    /// </summary>
    /// <remarks>
    /// Capturing the information for the build summary in azure devops is best done in the code
    /// where the values are computed. Unfortunately this is not always possible, for example in the 
    /// Cache miss analyzer case, there is would be prohibitive to pass through the BuildSummary class
    /// through all the abstraction layers and passing it down through many levels. Therefore this is
    /// a way to still collect the information.
    /// </remarks>
    public sealed class AzureDevOpsListener : FormattingEventListener
    {
        /// <nodoc />
        public const int MaxErrorsToIncludeInSummary = 50;

        /// <summary>
        /// The maximum number of AzureDevOps issues to log. Builds with too many issues can cause the UI to bog down.
        /// </summary>
        private readonly int m_maxIssuesToLog;
        private readonly IConsole m_console;
        private readonly BuildViewModel m_buildViewModel;

        /// <summary>
        /// The last reported percentage. To avoid double reporting the same percentage over and over
        /// </summary>
        private int m_lastReportedProgress = -1;
        private int m_warningCount;
        private int m_errorCount;
        private readonly int m_initialFrequencyMs;
        private int m_loggingFrequencyMs;
        private DateTime m_statusLogTime;
        /// <nodoc />
        public AzureDevOpsListener(
            Events eventSource,
            IConsole console,
            DateTime baseTime,
            BuildViewModel buildViewModel,
            bool useCustomPipDescription,
            [CanBeNull] WarningMapper warningMapper,
            int initialFrequencyMs,
            int maxIssuesToLog = 100)
            : base(eventSource, baseTime, warningMapper: warningMapper, level: EventLevel.Verbose, captureAllDiagnosticMessages: false, timeDisplay: TimeDisplay.Seconds, useCustomPipDescription: useCustomPipDescription)
        {
            Contract.RequiresNotNull(console);
            Contract.RequiresNotNull(buildViewModel);

            m_console = console;
            m_buildViewModel = buildViewModel;
            m_maxIssuesToLog = maxIssuesToLog;
            m_initialFrequencyMs = initialFrequencyMs;
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            m_console.Dispose();
            base.Dispose();
        }

        private int GetCurrentFrequencyMs()
        {
            DateTime currentTime = DateTime.UtcNow;
            long totalElapsedMins = SafeConvert.ToInt32(currentTime.Subtract(BaseTime).TotalMinutes);

            if (totalElapsedMins <= 5)
            {
                return m_initialFrequencyMs;
            }
            else if (totalElapsedMins < 60)
            {
                return 20_000;
            }
            else
            {
                return 60_000;
            }

        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventId)
            {
                case (int)SharedLogEventId.PipStatus:
                case (int)BuildXL.Scheduler.Tracing.LogEventId.PipStatusNonOverwriteable:
                {
                    var payload = eventData.Payload;

                    var executing = (long)payload[10];
                    var succeeded = (long)payload[11];
                    var failed = (long)payload[12];
                    var skipped = (long)payload[13];
                    var pending = (long)payload[14];
                    var waiting = (long)payload[15];

                    var done = succeeded + failed + skipped;
                    var total = done + pending + waiting + executing;

                    var processPercent = (100.0 * done) / (total * 1.0);
                    var currentProgress = Convert.ToInt32(Math.Floor(processPercent));

                    if (currentProgress > m_lastReportedProgress)
                    {
                        m_lastReportedProgress = currentProgress;

                        DateTime currentTime = DateTime.UtcNow;
                        m_loggingFrequencyMs = GetCurrentFrequencyMs();
                        if (currentTime > m_statusLogTime.AddMilliseconds(m_loggingFrequencyMs))
                        {
                            m_statusLogTime = currentTime;
                            m_console.WriteOutputLine(MessageLevel.Info, $"##vso[task.setprogress value={currentProgress};]Pip Execution phase");
                        }
                    }

                    break;
                }
            }
        }

        /// <inheritdoc />
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventId)
            {
                case (int)SharedLogEventId.CacheMissAnalysis:
                {
                    var payload = eventData.Payload;

                    m_buildViewModel.BuildSummary.CacheSummary.Entries.Add(
                        new CacheMissSummaryEntry
                        {
                            PipDescription = (string)payload[0],
                            Reason = (string)payload[1],
                            FromCacheLookup = (bool)payload[2],
                        }
                    );
                }
                break;
                case (int)SharedLogEventId.CacheMissAnalysisBatchResults:
                {
                    m_buildViewModel.BuildSummary.CacheSummary.BatchEntries.Add((string)eventData.Payload[0]);
                }
                break;
            }
        }

        /// <inheritdoc />
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
            LogAzureDevOpsIssue(eventData, "error");
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {
            LogIssueWithLimit(ref m_errorCount, eventData, "error");

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

            void addPipErrors(PipProcessErrorEventFields pipProcessErrorEventFields, string workerId)
            {
                string semiStableHash = Pip.FormatSemiStableHash(pipProcessErrorEventFields.PipSemiStableHash);
                m_buildViewModel.BuildSummary.AddPipError(new BuildSummaryPipDiagnostic
                {
                    SemiStablePipId = semiStableHash,
                    PipDescription = pipProcessErrorEventFields.PipDescription,
                    SpecPath = pipProcessErrorEventFields.PipSpecPath,
                    ToolName = pipProcessErrorEventFields.PipExe,
                    ExitCode = pipProcessErrorEventFields.ExitCode,
                    Output = pipProcessErrorEventFields.OutputToLog,
                    
                },
                MaxErrorsToIncludeInSummary);

                Tracing.CloudBuildEventSource.Log.TargetFailedEvent(new TargetFailedEvent
                {
                    WorkerId = workerId,
                    TargetId = semiStableHash,
                    StdOutputPath = pipProcessErrorEventFields.OutputToLog,
                    PipDescription = pipProcessErrorEventFields.PipDescription,
                    ShortPipDescription = pipProcessErrorEventFields.ShortPipDescription,
                    PipExecutionTimeMs = pipProcessErrorEventFields.PipExecutionTimeMs                    
                });
            }
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            LogIssueWithLimit(ref m_warningCount, eventData, "warning");
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
        }

        private void LogAzureDevOpsIssue(EventWrittenEventArgs eventData, string eventType)
        {
            using (var pooledInstance = Pools.StringBuilderPool.GetInstance())
            {
                var builder = pooledInstance.Instance;
                builder.Append("##vso[task.logIssue type=");
                builder.Append(eventType);

                int dxCode = eventData.EventId;
                var message = eventData.Message;
                var args = eventData.Payload == null ? CollectionUtilities.EmptyArray<object>() : eventData.Payload.ToArray();
                string body;

                var newArgs = args;
                // construct a short message for ADO console
                if ((eventData.EventId == (int)LogEventId.PipProcessError)
                    || (eventData.EventId == (int)SharedLogEventId.DistributionWorkerForwardedError && (int)args[1] == (int)LogEventId.PipProcessError))
                {
                    dxCode = (int)LogEventId.PipProcessError;
                    var pipProcessError = new PipProcessErrorEventFields(eventData.Payload, eventData.EventId != (int)LogEventId.PipProcessError);
                    args[0] = Pip.FormatSemiStableHash(pipProcessError.PipSemiStableHash);
                    args[1] = pipProcessError.ShortPipDescription;
                    args[2] = pipProcessError.PipSpecPath;
                    args[3] = pipProcessError.ExitCode;
                    args[4] = pipProcessError.OptionalMessage;
                    args[5] = pipProcessError.OutputToLog;
                    args[6] = pipProcessError.MessageAboutPathsToLog;
                    args[7] = pipProcessError.PathsToLog;
                    message = "[{0}, {1}, {2}] - failed with exit code {3}, {4}\r\n{5}\r\n{6}\r\n{7}";
                }
                else if (eventData.EventId == (int)SharedLogEventId.DistributionWorkerForwardedError || eventData.EventId == (int)SharedLogEventId.DistributionWorkerForwardedWarning)
                {
                    message = "{0}";
                }

                body = string.Format(CultureInfo.CurrentCulture, message, args);
                builder.Append(";]");
                
                // DX code
                builder.Append("DX");
                builder.Append(dxCode.ToString("D4"));
                builder.Append(' ');

                // substitute newlines in the message
                var encodedBody = body.Replace("\r\n", $"%0D%0A##[{eventType}]")
                                      .Replace("\r", $"%0D##[{eventType}]")
                                      .Replace("\n", $"%0A##[{eventType}]");
                builder.Append(encodedBody);

                m_console.WriteOutputLine(MessageLevel.Info, builder.ToString());
            }
        }

        private void LogIssueWithLimit(ref int counter, EventWrittenEventArgs eventData, string level)
        {
            int errorCount = Interlocked.Increment(ref counter);
            if (errorCount < m_maxIssuesToLog + 1)
            {
                LogAzureDevOpsIssue(eventData, level);
            }
            else if (errorCount == m_maxIssuesToLog + 1)
            {
                m_console.WriteOutputLine(MessageLevel.Info, $"##vso[task.logIssue type={level};] Future messages of this level are truncated");
            }
        }
    }
}
