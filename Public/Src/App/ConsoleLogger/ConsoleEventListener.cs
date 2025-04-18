﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using Strings = BuildXL.ConsoleLogger.Strings;

namespace BuildXL
{
    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and redirects output to the console.
    /// </summary>
    public sealed class ConsoleEventListener : FormattingEventListener
    {
        private const int DefaultMaxStatusPips = 5;

        private static readonly char[] s_newLineCharArray = Environment.NewLine.ToCharArray();

        private readonly IConsole m_console;

        private readonly int m_maxStatusPips;

        /// <summary>
        /// Some console logging behaviors should only be enabled when the build is not a worker
        /// </summary>
        private readonly bool m_notWorker;

        /// <summary>
        /// The full path to the logs directory
        /// </summary>
        private readonly string m_logsDirectory;

        /// <summary>
        /// Whether the console output should be optimized for Azure devops output
        /// </summary>
        private readonly bool m_optimizeForAzureDevOps;
        private readonly CancellationToken m_cancellationToken;

        /// <summary>
        /// This provides access to viewmodel data of the build for instance to get the list of running pips in fancy console mode.
        /// </summary>
        private BuildViewModel m_buildViewModel;

        private readonly StatusMessageThrottler m_statusMessageThrottler;

        /// <summary>
        /// Creates a new instance with optional colorization.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="baseTime">
        /// The UTC time representing time 0 for this listener.
        /// </param>
        /// <param name="colorize">
        /// When true, errors and warnings are colorized in the output. When false, all output is
        /// monochrome.
        /// </param>
        /// <param name="animateTaskbar">
        /// When true, BuildXL animates its taskbar icon during execution.
        /// </param>
        /// <param name="notWorker">
        /// True if this is not a worker.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="level">
        /// The base level of data to be sent to the listener.
        /// </param>
        /// <param name="eventMask">
        /// If specified, an EventMask that allows selectively enabling or disabling events
        /// </param>
        /// <param name="onDisabledDueToDiskWriteFailure">
        /// If specified, called if the listener encounters a disk-write failure such as an out of space condition.
        /// Otherwise, such conditions will throw an exception.
        /// </param>
        /// <param name="updatingConsole">
        /// When true, messages printed to the console may be updated in-place (if supported)
        /// </param>
        /// <param name="pathTranslator">
        /// If specified, translates paths that are logged
        /// </param>
        /// <param name="useCustomPipDescription">
        /// If true, pip description string will be changed to (SemiStableHash, CustomerSuppliedPipDescription).
        /// If true but no custom description available, no changes will be made.
        /// </param>
        /// <param name="cancellationToken">
        /// CancellationToken
        /// </param>
        /// <param name="maxStatusPips">
        /// Maximum number of concurrently executing pips to render in Fancy Console view.
        /// </param>
        /// <param name="optimizeForAzureDevOps">
        /// Whether console output should e optimized for Azure DevOps output.
        /// </param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public ConsoleEventListener(
            Events eventSource,
            DateTime baseTime,
            bool colorize,
            bool animateTaskbar,
            bool updatingConsole,
            bool useCustomPipDescription,
            CancellationToken cancellationToken,
            bool notWorker = true,
            WarningMapper warningMapper = null,
            EventLevel level = EventLevel.Verbose,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            PathTranslator pathTranslator = null,
            int maxStatusPips = DefaultMaxStatusPips,
            bool optimizeForAzureDevOps = false)
            : this(
                eventSource,
                new StandardConsole(colorize, animateTaskbar, updatingConsole, pathTranslator),
                baseTime,
                useCustomPipDescription,
                cancellationToken: cancellationToken,
                notWorker: notWorker,
                warningMapper: warningMapper,
                level: level,
                eventMask: eventMask,
                onDisabledDueToDiskWriteFailure: onDisabledDueToDiskWriteFailure,
                maxStatusPips: maxStatusPips,
                optimizeForAzureDevOps: optimizeForAzureDevOps)
        {
        }

        /// <summary>
        /// Creates a new instance with optional colorization.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="console">
        /// Console into which to write messages.
        /// </param>
        /// <param name="baseTime">
        /// The UTC time representing time 0 for this listener.
        /// </param>
        /// <param name="logsDirectory">
        /// The absolute path to the logs directory
        /// </param>
        /// <param name="notWorker">
        /// If this is not a worker.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="level">
        /// The base level of data to be sent to the listener.
        /// </param>
        /// <param name="eventMask">
        /// If specified, an EventMask that allows selectively enabling or disabling events
        /// </param>
        /// <param name="onDisabledDueToDiskWriteFailure">
        /// If specified, called if the listener encounters a disk-write failure such as an out of space condition.
        /// Otherwise, such conditions will throw an exception.
        /// </param>
        /// <param name="useCustomPipDescription">
        /// If true, pip description string will be changed to (SemiStableHash, CustomerSuppliedPipDescription).
        /// If true but no custom description available, no changes will be made.
        /// </param>
        /// <param name="cancellationToken">
        /// CancellationToken
        /// </param>
        /// <param name="maxStatusPips">
        /// Maximum number of concurrently executing pips to render in Fancy Console view.
        /// </param>
        /// <param name="optimizeForAzureDevOps">
        /// Whether console output should be optimized for Azure DevOps output.
        /// </param>
        public ConsoleEventListener(
            Events eventSource,
            IConsole console,
            DateTime baseTime,
            bool useCustomPipDescription,
            CancellationToken cancellationToken,
            string logsDirectory = null,
            bool notWorker = true,
            WarningMapper warningMapper = null,
            EventLevel level = EventLevel.Verbose,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            int maxStatusPips = DefaultMaxStatusPips,
            bool optimizeForAzureDevOps = false)
            : base(eventSource, baseTime, warningMapper, level, false, TimeDisplay.Seconds, eventMask, onDisabledDueToDiskWriteFailure, useCustomPipDescription: useCustomPipDescription)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(console != null);

            m_console = console;
            m_maxStatusPips = maxStatusPips;
            m_cancellationToken = cancellationToken;
            m_logsDirectory = logsDirectory;
            m_notWorker = notWorker;
            m_optimizeForAzureDevOps = optimizeForAzureDevOps;
            m_statusMessageThrottler = new StatusMessageThrottler(baseTime);

            // Only the console listener in interested in this event source. All events specified
            // with /logToConsole will be redirected to this event source and picked up here
            // This avoids the redirection to show anywhere else. Observe that this event source
            // is registered in addition to the specified eventSource
            RegisterEventSource(ConsoleLogger.ETWLogger.Log);
        }

        /// <summary>
        /// Sets the build view model that when set this class can use to print the current running pips.
        /// </summary>
        public void SetBuildViewModel(BuildViewModel buildViewModel)
        {
            m_buildViewModel = buildViewModel;
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            m_console.Dispose();
            base.Dispose();
        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventId)
            {
                case (int)SharedLogEventId.StartEngineRun:
                {
                    m_console.ReportProgress((ulong)(m_notWorker ? 0 : 100), 100);
                    break;
                }

                case (int)SharedLogEventId.EndEngineRun:
                {
                    m_console.ReportProgress(100, 100);
                    break;
                }

                case (int)SharedLogEventId.PipStatus:
                case (int)BuildXL.Scheduler.Tracing.LogEventId.PipStatusNonOverwriteable:
                {
                    ReadOnlyCollection<object> payload = eventData.Payload;

                    var pipsSucceeded = (long)payload[0];
                    var pipsFailed = (long)payload[1];
                    var pipsSkipped = (long)payload[2];
                    var pipsRunning = (long)payload[3];
                    var pipsReady = (long)payload[4];
                    var pipsWaiting = (long)payload[5];
                    var pipsWaitingOnSemaphore = (long)payload[6];
                    var servicePipsRunning = (long)payload[7];
                    string perfInfo = (string)payload[8];
                    var pipsWaitingOnResources = (long)payload[9];
                    var procsExecuting = (long)payload[10];
                    var procsSucceeded = (long)payload[11];
                    var procsFailed = (long)payload[12];
                    var procsSkipped = (long)payload[13];
                    var procsPending = (long)payload[14];
                    var procsWaiting = (long)payload[15];
                    var procsHit = (long)payload[16];
                    var procsNotIgnored = (long)payload[17];
                    var copyFileDone = (long)payload[20];
                    var copyFileNotDone = (long)payload[21];
                    var writeFileDone = (long)payload[22];
                    var writeFileNotDone = (long)payload[23];
                    var remoteProcs = (long)payload[24];
                    long done = pipsSucceeded + pipsFailed + pipsSkipped;
                    long total = done + pipsRunning + pipsWaiting + pipsReady;

                    long procsDone = procsSucceeded + procsFailed + procsSkipped;
                    long procsTotal = procsDone + procsPending + procsWaiting + procsExecuting;

                    long filePipsDone = copyFileDone + writeFileDone;
                    long filePipsTotal = filePipsDone + copyFileNotDone + writeFileNotDone;

                    // For sake of simplicity, both pending & waiting processes are labeled as "waiting" in the console
                    long pendingAndWaiting = procsPending + procsWaiting;

                    using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
                    {
                        StringBuilder sb = wrap.Instance;

                        if (m_notWorker)
                        {
                            sb.Append(@"{{8,{0}}}Processes: [{{4,{0}}} done ({{5}} hit),");
                            if (pipsFailed > 0)
                            {
                                sb.Append(@" {{0,{0}}} succeeded, {{1,{0}}} failed,");
                            }

                            if (pipsSkipped > 0)
                            {
                                sb.Append(@" {{6,{0}}} skipped,");
                            }

                            if (remoteProcs > 0)
                            {
                                sb.Append(@" {{7,{0}}} executing ({{13}} remote), {{2,{0}}} waiting]");
                            }
                            else
                            {
                                sb.Append(@" {{7,{0}}} executing, {{2,{0}}} waiting]");
                            }

                            if (pipsWaitingOnSemaphore > 0)
                            {
                                sb.Append(@" ({{3,{0}}} on semaphores).");
                            }

                            if (filePipsTotal > 0)
                            {
                                sb.Append(@" Files:[{{11}}/{{12}}]");
                            }
                        }
                        else
                        {
                            // For workers in the ADO console, we only display the process pips executing.
                            // This is because most of the other counters are not calculated as expected on the worker machines.
                            // In order to avoid projecting incorrect values, we eliminate them in the ADO console status of the workers.
                            if (Interlocked.Increment(ref m_seeOrchestratroMessageUpdateTicker) % SeeOrchestratorUpdateFrequency == 1)
                            {
                                // ...but every once in a while, instead of printing the amount of running processes, let's just print this informational message
                                sb.Append(@"This agent is running the build as a distributed worker. Please check the orchestrator console for global details on the build progress");
                            }
                            else
                            {
                                sb.Append(@"Currently running {{0,{0}}} processes on this worker...");
                            }
                        }
 
                        string statusLine = sb.ToString();
                        sb.Length = 0;
                        string updatingStatus = null;

                        var format = FinalizeFormatStringLayout(sb, statusLine, 0);
                        bool waitingOnBackgroundOperations = false;

                        // We have a different format of the console status for the workers in ADO console, this is because we only display the process pips executing on the worker machine.
                        if (m_notWorker)
                        {
                            waitingOnBackgroundOperations = (procsDone + filePipsDone == procsTotal + filePipsTotal) && servicePipsRunning != 0;

                            sb.AppendFormat(
                                CultureInfo.InvariantCulture,
                                format,
                                procsSucceeded,
                                procsFailed,
                                pendingAndWaiting,
                                pipsWaitingOnSemaphore,
                                procsDone,
                                procsHit,
                                procsSkipped,
                                procsExecuting,
                                ComputePercentDone(procsDone, procsTotal, filePipsDone, filePipsTotal),
                                done,
                                total,
                                filePipsDone,
                                filePipsTotal,
                                remoteProcs);
                        }
                        else
                        {
                            sb.AppendFormat(
                                CultureInfo.InvariantCulture,
                                format,
                                procsExecuting);
                        }

                        // Ensures backgroundTaskConsoleStatusMessage is displayed in ADO and when the fancyConsole option is disabled, when build progress is 100% and only service pips run.
                        // This is done to ensure that we do not include this message in updatableMessage twice when the fancyConsole option is enabled.
                        // We do not display this message for the workers in ADO.
                        if (m_notWorker && waitingOnBackgroundOperations && !m_console.UpdatingConsole)
                        {
                            sb.Append(" ");
                            sb.Append(Strings.BackgroundTaskConsoleStatusMessage);
                        }

                        string standardStatus = sb.ToString();

                        // We do not need to construct the fancy console message for ADO.
                        if (!m_optimizeForAzureDevOps)
                        {
                            updatingStatus = GetRunningPipsMessage(standardStatus, perfInfo, waitingOnBackgroundOperations);
                        }

                        bool allowStatusThrottling = m_optimizeForAzureDevOps && eventData.EventId != (int)BuildXL.Scheduler.Tracing.LogEventId.PipStatusNonOverwriteable;
                        SendToConsole(eventData, "info", standardStatus, allowStatusThrottling: allowStatusThrottling, updatableMessage: updatingStatus);
                    }

                    if (m_notWorker)
                    {
                        m_console.ReportProgress((ulong)done, (ulong)total);
                    }

                    break;
                }
                // This is the case of regular log events sent to the console honoring a user configurable option
                case (int)ConsoleRedirector.Tracing.LogEventId.LogToConsole:
                {
                    // The message is already fully formatted, so just put it back together
                    object[] args = eventData.Payload == null ? CollectionUtilities.EmptyArray<object>() : eventData.Payload.ToArray();
                    string finalMessage = string.Format(CultureInfo.CurrentCulture, eventData.Message, args);

                    Output(MessageLevel.Info, finalMessage);
                    break;
                }

                default:
                {
                    SendToConsole(eventData, "info", eventData.Message, allowStatusThrottling: false);
                    break;
                }
            }
        }

        /// <summary>
        /// The percentage done is mostly a straight calculation based on the number of completed process pips. There
        /// are 2 exceptions to this rule:
        ///     1. Outstanding file pips contribute to the last 99.9x digit in the overall percent complete. This upholds
        ///         the desire of the overall percent to mirror the process pips, while still allowing some amount
        ///         of progress updating for builds with lots of trailing file pips
        ///     2. The percent shouldn't hit 100% until every last bit of work is complete. So a build with lots of pips
        ///         shouldn't ever round up to 100% when there is still an outstanding pip
        /// </summary>
        internal static string ComputePercentDone(long procsDone, long procsTotal, long filePipsDone, long filePipsTotal)
        {
            const string percentFormat = "{0:F2}%  ";
            if (procsTotal == 0)
            {
                return string.Format(percentFormat, 0.0);
            }

            // The percent purely based on process pips
            double processPercent = (100.0 * procsDone) / (procsTotal * 1.0);

            // The percent with the last 99.9x coming from the completeness of file pips
            double processPercentWithFilePips = filePipsTotal == 0 ? processPercent : Math.Min(processPercent, 99.9) + ((1.0 * filePipsDone) / (filePipsTotal * 10.0));

            double finalPercentage = processPercent;

            // 1. Use last digit for file pips
            if (processPercent > 99.9)
            {
                finalPercentage = Math.Min(processPercent, processPercentWithFilePips);
            }

            // 2. Don't exceed 99.99% if there are any outstanding pips
            if (finalPercentage > 99.99 && ((procsTotal + filePipsTotal) - (procsDone + filePipsDone) > 0))
            {
                finalPercentage = 99.99;
            }

            return string.Format(percentFormat, finalPercentage);
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {

            //Ignore all errors except the cancellation requested error.
#pragma warning disable 618
            if (m_cancellationToken.IsCancellationRequested && (eventData.EventId != (int)SharedLogEventId.CancellationRequested))
#pragma warning restore 618
            {
                return;
            }

            Interlocked.Increment(ref m_errorsLogged);

            // AzureDevOpsListener has already written the event to console, avoid duplication
            if (m_optimizeForAzureDevOps)
            {
                return;
            }

#pragma warning disable 618
            if (eventData.EventId == (int)SharedLogEventId.PipProcessError)
#pragma warning restore 618
            {
                // Try to be a bit fancy and only show the tool errors in red. The pip name and log file will stay in
                // the default color
                string output = (string)eventData.Payload[5];

                if (!string.IsNullOrWhiteSpace(output))
                {
                    string message = CreateFullMessageString(eventData, "error", eventData.Message, BaseTime, UseCustomPipDescription, TimeDisplay);

                    // If there is tool output, break apart the message to only colorize the output
                    int messageStart = message.IndexOf(output, StringComparison.OrdinalIgnoreCase);
                    if (messageStart != -1)
                    {
                        // Note - the MessageLevel below are really just for the sake of colorization
                        Output(MessageLevel.ErrorNoColor, message.Substring(0, messageStart).TrimEnd(s_newLineCharArray));
                        Output(MessageLevel.Error, output);
                        return;
                    }
                }
            }

            // We couldn't do the fancy formatting
            base.OnError(eventData);
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            // Do not show any warnings in console on cancellation.
            if (m_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // AzureDevOpsListener has alreday write the event to console, avoid duplication
            if (m_optimizeForAzureDevOps)
            {
                return;
            }

#pragma warning disable 618
            if (eventData.EventId == (int)SharedLogEventId.PipProcessWarning)
#pragma warning restore 618
            {
                string warnings = (string)eventData.Payload[5];

                if (!string.IsNullOrWhiteSpace(warnings))
                {
                    string message = CreateFullMessageString(eventData, "warning", eventData.Message, BaseTime, UseCustomPipDescription, TimeDisplay);

                    // If there is tool output, break apart the message to only colorize the output
                    int messageStart = message.IndexOf(warnings, StringComparison.OrdinalIgnoreCase);
                    if (messageStart != -1)
                    {
                        // Note - the MessageLevel below are really just for the sake of colorization
                        Output(EventLevel.Informational, eventData, message.Substring(0, messageStart).TrimEnd(s_newLineCharArray));
                        Output(EventLevel.Warning, eventData, warnings);

                        return;
                    }
                }
            }

            // We couldn't do the fancy formatting
            base.OnWarning(eventData);
        }

        private static string FinalizeFormatStringLayout(StringBuilder buffer, string statusLine, long maxNum)
        {
            int numDigits = 0;
            long num = maxNum;
            while (num > 0)
            {
                num /= 10;
                numDigits++;
            }

            if (numDigits == 0)
            {
                numDigits++;
            }

            // finalize any space qualifiers like {{0,{0}}} to {0,<numDigits>}
            string format = buffer.AppendFormat(
                CultureInfo.InvariantCulture,
                statusLine,
                numDigits).ToString();

            buffer.Length = 0;

            return format;
        }

        private void SendToConsole(EventWrittenEventArgs eventData, string label, string message, bool allowStatusThrottling, string updatableMessage = null)
        {
            if (allowStatusThrottling && m_statusMessageThrottler.ShouldThrottleStatusUpdate())
            {
                // Bail out early if this is a status update and the update period is more frequent than desired
                return;
            }

            string finalMessage = CreateFullMessageString(eventData, label, message, BaseTime, UseCustomPipDescription, TimeDisplay.Seconds);

            var keyWords = eventData.Keywords;

            if (!m_optimizeForAzureDevOps &&
                ((keyWords & Keywords.Overwritable) != 0 ||
                (keyWords & Keywords.OverwritableOnly) != 0))
            {
                OutputUpdatable(eventData.Level, finalMessage, updatableMessage ?? finalMessage,
                    (keyWords & Keywords.OverwritableOnly) != 0);
            }
            else
            {
                Output(eventData.Level, eventData, finalMessage);
            }
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
            Output(ConvertLevel(level), text);
        }

        private void Output(MessageLevel level, string text)
        {
            m_console.WriteOutputLine(level, text.TrimEnd(s_newLineCharArray));
        }

        private void OutputUpdatable(EventLevel level, string standardText, string updatableText, bool onlyIfOverwriteIsSupported)
        {
            if (onlyIfOverwriteIsSupported)
            {
                m_console.WriteOverwritableOutputLineOnlyIfSupported(
                    ConvertLevel(level),
                    standardText,
                    updatableText);
            }
            else
            {
                m_console.WriteOverwritableOutputLine(
                    ConvertLevel(level),
                    standardText,
                    updatableText);
            }
        }

        private static MessageLevel ConvertLevel(EventLevel level)
        {
            switch (level)
            {
                case EventLevel.Critical:
                case EventLevel.Error:
                    return MessageLevel.Error;
                case EventLevel.Warning:
                    return MessageLevel.Warning;
                default:
                    return MessageLevel.Info;
            }
        }

        #region PipStatus processing

        private readonly object m_runningPipsLock = new object();
        private Dictionary<PipId, PipInfo> m_runningPips;

        /// <summary>
        /// Count of errors that have been logged
        /// </summary>
        private long m_errorsLogged;

        /// <summary>
        /// We want to print a message on workers referring to the orchestrator console every once in a while
        /// </summary>
        private long m_seeOrchestratroMessageUpdateTicker = 0;
        private const int SeeOrchestratorUpdateFrequency = 25;

        private sealed class PipInfo
        {
            public string PipDescription;
            public DateTime FirstSeen;
            public DateTime LastSeen;
        }

        private string GetRunningPipsMessage(string standardStatus, string perfInfo, bool waitingOnBackgroundOperations)
        {
            lock (m_runningPipsLock)
            {
                if (m_buildViewModel == null)
                {
                    return null;
                }

                var context = m_buildViewModel.Context;
                Contract.Assert(context != null);

                if (m_runningPips == null)
                {
                    m_runningPips = new Dictionary<PipId, PipInfo>();
                }

                DateTime thisCollection = DateTime.UtcNow;

                // Use the viewer's interface to fetch the info about which pips are currently running.
                foreach (var pip in m_buildViewModel.RetrieveExecutingProcessPips())
                {
                    PipInfo runningInfo;
                    if (!m_runningPips.TryGetValue(pip.PipId, out runningInfo))
                    {
                        // This is a new pip that wasn't running the last time the currently running pips were queried
                        Pip p = pip.HydratePip();

                        runningInfo = new PipInfo()
                        {
                            PipDescription = p.GetShortDescription(context),
                            FirstSeen = DateTime.UtcNow,
                        };
                        m_runningPips.Add(pip.PipId, runningInfo);
                    }

                    runningInfo.LastSeen = DateTime.UtcNow;
                }

                // Build up a string based on the snapshot of what pips are being running
                using (var pooledWrapper = Pools.StringBuilderPool.GetInstance())
                {
                    StringBuilder sb = pooledWrapper.Instance;
                    sb.Append(TimeSpanToString(TimeDisplay.Seconds, DateTime.UtcNow - BaseTime));
                    sb.Append(' ');
                    sb.Append(standardStatus);
                    if (!string.IsNullOrWhiteSpace(perfInfo))
                    {
                        sb.Append(". " + perfInfo);
                    }

                    // Display the log file location if there have been errors logged. This allows the user to open the
                    // log files while the build is running to see errors that have scrolled off the console
                    var errors = Interlocked.Read(ref m_errorsLogged);
                    if (errors > 0 && m_logsDirectory != null)
                    {
                        sb.AppendLine();
                        sb.AppendFormat(Strings.App_Errors_LogsDirectory, errors, m_logsDirectory);
                    }

                    // Display backgroundTaskStatusMessage if fancyConsole option is enabled and when build is 100% complete, no process pips running, but service pips are.
                    if (m_runningPips.Count == 0 && waitingOnBackgroundOperations)
                    {
                        sb.AppendLine();
                        sb.Append(Strings.BackgroundTaskConsoleStatusMessage);
                    }

                    int pipCount = 0;
    
                    foreach (var item in m_runningPips.ToArray().OrderBy(kvp => kvp.Value.FirstSeen))
                    {
                        if (item.Value.LastSeen < thisCollection)
                        {
                            // If the pip was last seen before this collection it is no longer running and should be removed
                            m_runningPips.Remove(item.Key);
                        }
                        else
                        {
                            pipCount++;
                            if (pipCount <= m_maxStatusPips)
                            {
                                // Otherwise include it in the string
                                string info = string.Format(CultureInfo.InvariantCulture, "   {0} {1}",
                                    TimeSpanToString(TimeDisplay.Seconds, item.Value.LastSeen - item.Value.FirstSeen),
                                    item.Value.PipDescription);

                                // Don't have a trailing newline for the last message;
                                if (sb.Length > 0)
                                {
                                    sb.AppendLine();
                                }

                                sb.Append(info);
                            }
                        }
                    }

                    if (pipCount > m_maxStatusPips)
                    {
                        sb.AppendLine();
                        sb.AppendFormat(Strings.ConsoleListener_AdditionalPips, pipCount - m_maxStatusPips);
                    }

                    return sb.ToString();
                }
            }
        }

        #endregion
    }
}
