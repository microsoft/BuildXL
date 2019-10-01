// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;

namespace BuildXL
{
    /// <summary>
    /// This event listener should only be hooked up when AzureDevOps optimzied UI is requested by
    /// the user via the /ado commandline flag.
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
        private static readonly char[] s_newLineCharArray = Environment.NewLine.ToCharArray();

        private readonly IConsole m_console;

        /// <summary>
        /// The last reported percentage. To avoid double reporting the same percentage over and over
        /// </summary>
        private int m_lastReportedProgress = -1;

        private readonly BuildViewModel m_buildViewModel;

        /// <nodoc />
        public AzureDevOpsListener(
            Events eventSource,
            IConsole console,
            DateTime baseTime,
            BuildViewModel buildViewModel,
            bool useCustomPipDescription)
            : base(eventSource, baseTime, warningMapper: null, level: EventLevel.Verbose, captureAllDiagnosticMessages: false, timeDisplay: TimeDisplay.Seconds, useCustomPipDescription: useCustomPipDescription)
        {
            Contract.Requires(console != null);
            Contract.Requires(buildViewModel != null);

            m_console = console;
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
                case (int)EventId.PipStatus:
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
                            m_console.WriteOutputLine(MessageLevel.Info, $"##vso[task.setprogress value={currentProgress};]Pip Execution phase");
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
                case (int)EventId.CacheMissAnalysis:
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
            LogAzureDevOpsIssue(eventData, "error");

            switch (eventData.EventId)
            {
                case (int)EventId.PipProcessError:
                    {
                        var payload = eventData.Payload;

                        m_buildViewModel.BuildSummary.PipErrors.Add(new BuildSummaryPipDiagnostic
                        {
                            SemiStablePipId = $"Pip{((long)eventData.Payload[0]):X16}",
                            PipDescription = (string)eventData.Payload[1],
                            SpecPath = (string)eventData.Payload[2],
                            ToolName = (string)eventData.Payload[4],
                            ExitCode = (int)eventData.Payload[7],
                            Output = (string)eventData.Payload[5],
                        }); 
                    }
                    break;
            }
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            LogAzureDevOpsIssue(eventData, "warning");
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, int id, string eventName, EventKeywords eventKeywords, string text, bool doNotTranslatePaths = false)
        {
        }

        private void LogAzureDevOpsIssue(EventWrittenEventArgs eventData, string eventType)
        {
            var builder = new StringBuilder();
            builder.Append("##vso[task.logIssue type=");
            builder.Append(eventType);

            var message = eventData.Message;
            var args = eventData.Payload == null ? CollectionUtilities.EmptyArray<object>() : eventData.Payload.ToArray();
            string body;

            // see if this event provides provenance info
            if (message.StartsWith(EventConstants.ProvenancePrefix, StringComparison.Ordinal))
            {
                Contract.Assume(args.Length >= 3, "Provenance prefix contains 3 formatting tokens.");

                // file
                builder.Append(";sourcepath=");
                builder.Append(args[0]);

                //line
                builder.Append(";linenumber=");
                builder.Append(args[1]);

                //column
                builder.Append(";columnnumber=");
                builder.Append(args[2]);

                //code
                builder.Append(";code=DX");
                builder.Append(eventData.EventId.ToString("D4"));
            }

            // report the entire message since Azure DevOps does not yet provide actionalbe information from the metadata.
            body = string.Format(CultureInfo.CurrentCulture, message, args);

            // pip description in the final string only exist when args is not empty
            if (args.Length > 0)
            {
                ProcessCustomPipDescription(ref body, UseCustomPipDescription);
            }

            builder.Append(";]");

            // substitute newlines in the message

            var encodedBody = body.Replace("\r", "%0D").Replace("\n", "%0A");
            builder.Append(encodedBody);

            m_console.WriteOutputLine(MessageLevel.Info, builder.ToString());
        }
    }
}
