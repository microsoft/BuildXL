// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL;
using BuildXL.Processes.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL
{
    public class DevOpsListenerTests : XunitBuildXLTest
    {
        private PipProcessEventFields m_pipProcessEventFields;
        private const int AdoConsoleMaxIssuesToLog = 100;
        private const string PipProcessEditEventMessage = "You may have edited the PipProcessEvent and/or WorkerForwardedEvent fields, update the test and/or struct PipProcessEventFields.";

        public DevOpsListenerTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LogAzureDevOpsIssueTest(bool isPipProcessError)
        {
            m_eventListener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += (eventData, s) =>
            {
                if (isPipProcessError)
                {
                    m_pipProcessEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: false, isPipProcessError: true);
                }
                else
                {
                    m_pipProcessEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: false, isPipProcessError: false);
                }
            };

            using (var testElements = PipProcessEventTestElement.Create(this, isPipProcessError: isPipProcessError))
            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.Console, DateTime.Now, testElements.ViewModel, false, null, AdoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
                if (isPipProcessError)
                {
                    testElements.LogPipProcessError();
                    XAssert.AreEqual(m_pipProcessEventFields, testElements.PipProcessError, PipProcessEditEventMessage);
                    AssertErrorEventLogged(LogEventId.PipProcessError);
                }
                else
                {
                    testElements.LogPipProcessWarning();
                    XAssert.AreEqual(m_pipProcessEventFields, testElements.PipProcessWarning, PipProcessEditEventMessage);
                    AssertWarningEventLogged(LogEventId.PipProcessWarning);
                }
                testElements.Console.ValidateCallForPipProcessEventinADO(MessageLevel.Info, testElements.ExpectingConsoleLog);

            }
        }

        [Fact]
        public void ValidateErrorCap()
        {
            var adoConsoleMaxIssuesToLog = 1;
            m_eventListener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += (eventData, _) =>
            {
                m_pipProcessEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: false, isPipProcessError: true);
            };

            using (var testElements = PipProcessEventTestElement.Create(this, isPipProcessError: true))
            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.Console, DateTime.Now, testElements.ViewModel, false, null, adoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);

                // First log should go through as normal
                testElements.LogPipProcessError();
                testElements.Console.ValidateCallForPipProcessEventinADO(MessageLevel.Info, testElements.ExpectingConsoleLog);

                // Second will log the message about being truncated
                testElements.LogPipProcessError();
                testElements.Console.ValidateCallForPipProcessEventinADO(MessageLevel.Info, new List<string> { $"##vso[task.logIssue type=error;] Future messages of this level are truncated" });

                // Third should result in no more messages logged
                testElements.LogPipProcessError();
                testElements.Console.ValidateNoCall();
            }

            // The TestEventListener is watching all errors, not the AzureDevOpsListener processed ones. Make sure it's cool with seeing 3 errors
            AssertErrorEventLogged(LogEventId.PipProcessError, 3);
        }

        [Fact]
        public void ForwardedPipProcessErrorTest()
        {
            var eventName = "PipProcessError";
            var text = "Pip process error message";
            var pipSemiStableHash = (long)24;

            m_eventListener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += (eventData, _) =>
            {
                m_pipProcessEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: true, isPipProcessError: true);
            };

            using (var testElements = PipProcessEventTestElement.Create(this, isPipProcessError: true))
            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.Console, DateTime.Now, testElements.ViewModel, false, null, AdoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError(LoggingContext, new WorkerForwardedEvent()
                {
                    EventId = (int)LogEventId.PipProcessError,
                    EventName = eventName,
                    EventKeywords = 0,
                    Text = text,
                    PipProcessEvent = testElements.PipProcessError,
                });
                testElements.Console.ValidateCallForPipProcessEventinADO(MessageLevel.Info, testElements.ExpectingConsoleLog);
                XAssert.IsTrue(testElements.ViewModel.BuildSummary.PipErrors.Any(e => e.SemiStablePipId == $"Pip{(pipSemiStableHash):X16}"));
                XAssert.AreEqual(m_pipProcessEventFields, testElements.PipProcessError, PipProcessEditEventMessage);
                AssertErrorEventLogged(SharedLogEventId.DistributionWorkerForwardedError);
            }
        }

        [Theory]
        [InlineData("error")]
        [InlineData("warning")]
        [InlineData("event")]
        public void DependOnASpecificMessageFormatForForwardedEvents(string eventType)
        {
            // For console logging on ADO, we depend on a specific format for the rendered text in a forwarded event.
            // This test makes sure these messages are not changed willy-nilly in the future, possibly making these assumptions invalid.
            // See also ConsoleEventListenerTests.TestEventRedirectionOfForwardedEvents, which relies on these assumptions
            // If this test fails, the logic in ConsoleRedirectorEventListener.Output might fail too.

            var forwardedText = "This is the full text of the event logged in the worker";

            m_eventListener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);

            // Capture the fully formatted message when logged
            string loggedMessage = "";
            m_eventListener.NestedLoggerHandler += (_, s) =>
            {
                XAssert.IsTrue(loggedMessage == "");    // We expect a single message to be logged
                loggedMessage = s;
            };

            using var testElements = PipProcessEventTestElement.Create(this, isPipProcessError: eventType == "error");
            using var listener = new AzureDevOpsListener(Events.Log, testElements.Console, DateTime.Now, testElements.ViewModel, false, null, AdoConsoleMaxIssuesToLog);

            listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);

            (Action<LoggingContext, WorkerForwardedEvent> LogAction, LogEventId EventId) testData =
                eventType switch
                {
                    "error" => (global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError, LogEventId.PipProcessError),
                    "warning" => (global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedWarning, LogEventId.PipProcessWarning),
                    "event" => (global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedEvent, LogEventId.PipProcessOutput),
                    _ => throw new InvalidArgumentException("Shouldn't happen"),
                };

            testData.LogAction.Invoke(LoggingContext, new WorkerForwardedEvent()
            {
                WorkerName = "0",
                EventId = (int)testData.EventId,
                EventName = testData.EventId.ToString(),
                EventKeywords = 0,
                Text = forwardedText,
                PipProcessEvent = eventType == "error" ? testElements.PipProcessError : eventType == "warning" ? testElements.PipProcessWarning : default,
            });


            XAssert.IsFalse(string.IsNullOrEmpty(loggedMessage));

            // The heart of this test: we rely on the raw message logged on the worker side to be
            // logged after a single newline, so we can strip it and log it transparently to the console.
            var regex = new Regex(@"Worker .* logged " + eventType + @":\n");
            XAssert.IsTrue(regex.IsMatch(loggedMessage));

            var splits = loggedMessage.Split('\n', 2);
            XAssert.IsTrue(splits.Length == 2);
            XAssert.AreEqual(forwardedText, splits[1]);
            
            switch (eventType)
            {
                case "error":
                    AssertErrorEventLogged(SharedLogEventId.DistributionWorkerForwardedError);
                    break;
                case "warning":
                    AssertWarningEventLogged(SharedLogEventId.DistributionWorkerForwardedWarning);
                    break;
                case "event":
                    AssertVerboseEventLogged(SharedLogEventId.DistributionWorkerForwardedEvent);
                    break;
            }
        }

        [Fact]
        public void ForwardedPipProcessWarningTest()
        {
            var eventName = "PipProcessWarning";
            var warningMessage = "I'm a warning you want to ignore; it hurts.";

            m_eventListener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += (eventData, _) =>
            {
                m_pipProcessEventFields = new PipProcessEventFields(eventData.Payload, forwardedPayload: true, isPipProcessError: false);
            };

            using (var testElements = PipProcessEventTestElement.Create(this, isPipProcessError: false))
            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.Console, DateTime.Now, testElements.ViewModel, false, null, AdoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedWarning(LoggingContext, new WorkerForwardedEvent()
                {
                    EventId = (int)LogEventId.PipProcessWarning,
                    EventName = eventName,
                    EventKeywords = 0,
                    Text = warningMessage,
                    PipProcessEvent = testElements.PipProcessWarning,
                });
                testElements.Console.ValidateCallForPipProcessEventinADO(MessageLevel.Info, testElements.ExpectingConsoleLog);
                XAssert.AreEqual(m_pipProcessEventFields, testElements.PipProcessWarning, PipProcessEditEventMessage);
                AssertWarningEventLogged(SharedLogEventId.DistributionWorkerForwardedWarning);
            }
        }

        /// <summary>
        /// Encapsulates some boilerplate code shared by tests that require calls to log methods for PipProcessError and PipProcessWarning events.
        /// </summary>
        private class PipProcessEventTestElement : IDisposable
        {
            public PipProcessEventFields PipProcessError;
            public PipProcessEventFields PipProcessWarning;
            public List<string> ExpectingConsoleLog = new List<string>();
            public MockConsole Console;
            public BuildViewModel ViewModel;
            private LoggingContext m_loggingContext;

            public static PipProcessEventTestElement Create(BuildXLTestBase testBase, bool isPipProcessError = false)
            {
                var result = new PipProcessEventTestElement();
                long totalElapsedTimeMs = Convert.ToInt64(TimeSpan.FromSeconds(15).TotalMilliseconds);

                if (isPipProcessError)
                {
                    var pipProcessError = PipProcessEventFields.CreatePipProcessErrorEventFields(
                        (long)24,
                        "my cool pip",
                        @"specs\mypip.dsc",
                        @"specs\workingDir",
                        "coolpip.exe",
                        $"Failure message Line1{Environment.NewLine}Failure message Line2{Environment.NewLine}Failure message Line3{Environment.NewLine}",
                        "Find output file in following path:",
                        @"specs\workingDir\out.txt",
                        -1,
                        "what does this do?",
                        "my pip",
                        totalElapsedTimeMs);
                    result.PipProcessError = pipProcessError;

                    // Construct PipProcessError message.
                    string expectedText =
@$"##vso[task.logIssue type=error;]DX0064 [Pip0000000000000018, {pipProcessError.ShortPipDescription}, {pipProcessError.PipSpecPath}] - failed with exit code {pipProcessError.ExitCode}, {pipProcessError.OptionalMessage}
##[error]Failure message Line1
##[error]Failure message Line2
##[error]Failure message Line3
##[error]
##[error]{pipProcessError.MessageAboutPathsToLog}
##[error]{pipProcessError.PathsToLog}";

                    result.ExpectingConsoleLog.Add(GenerateExpectedMessage(expectedText));
                }
                else
                {
                    var pipProcessWarning = PipProcessEventFields.CreatePipProcessWarningEventFields(
                    (long)24,
                    "my cool pip",
                    @"specs\mypip.dsc",
                    @"specs\workingDir",
                    "coolpip.exe",
                    $"Failure message Line1{Environment.NewLine}Failure message Line2{Environment.NewLine}Failure message Line3{Environment.NewLine}",
                    "Find output file in following path:",
                    @"specs\workingDir\out.txt");
                    result.PipProcessWarning = pipProcessWarning;

                    // Construct PipProcessWarning message.
                    string expectedText =
@$"##vso[task.logIssue type=warning;]DX0065 [Pip0000000000000018, {pipProcessWarning.PipDescription}, {pipProcessWarning.PipSpecPath}] - warnings
##[warning]Failure message Line1
##[warning]Failure message Line2
##[warning]Failure message Line3
##[warning]
##[warning]{pipProcessWarning.MessageAboutPathsToLog}
##[warning]{pipProcessWarning.PathsToLog}";

                    result.ExpectingConsoleLog.Add(GenerateExpectedMessage(expectedText));
                }

                result.Console = new MockConsole();
                result.ViewModel = new BuildViewModel();
                var buildSummaryFilePath = Path.Combine(testBase.TestOutputDirectory, "test.md");
                result.ViewModel.BuildSummary = new BuildSummary(buildSummaryFilePath);
                result.m_loggingContext = testBase.LoggingContext;

                return result;
            }

            private static string GenerateExpectedMessage(string rawMessage)
            {
                // A few steps are needed to convert the raw message to the expected message.
                // First, all Windows line endings should be normalized to OS specific ones. This
                // is necessary because the .cs source file of the test will itself have OS specific line endings.
                // The second step is to replace the OS specific line endings with the Azure DevOps ones. This varies
                // depending on which OS the test is currently running in.
                return rawMessage.Replace("\r\n", Environment.NewLine).Replace(Environment.NewLine, OperatingSystemHelper.IsWindowsOS ? "%0D%0A" : "%0A");
            }

            public void LogPipProcessError()
            {
                global::BuildXL.Processes.Tracing.Logger.Log.PipProcessError(m_loggingContext,
                    PipProcessError.PipSemiStableHash,
                    PipProcessError.PipDescription,
                    PipProcessError.PipSpecPath,
                    PipProcessError.PipWorkingDirectory,
                    PipProcessError.PipExe,
                    PipProcessError.OutputToLog,
                    PipProcessError.MessageAboutPathsToLog,
                    PipProcessError.PathsToLog,
                    PipProcessError.ExitCode,
                    PipProcessError.OptionalMessage,
                    PipProcessError.ShortPipDescription,
                    PipProcessError.PipExecutionTimeMs);
            }

            public void LogPipProcessWarning()
            {
                global::BuildXL.Processes.Tracing.Logger.Log.PipProcessWarning(m_loggingContext,
                    PipProcessWarning.PipSemiStableHash,
                    PipProcessWarning.PipDescription,
                    PipProcessWarning.PipSpecPath,
                    PipProcessWarning.PipWorkingDirectory,
                    PipProcessWarning.PipExe,
                    PipProcessWarning.OutputToLog,
                    PipProcessWarning.MessageAboutPathsToLog,
                    PipProcessWarning.PathsToLog);
            }

            public void Dispose()
            {
                Console.Dispose();
            }
        }

        [Fact]
        public void ForwardedErrorOrWarningTest()
        {
            const string ErrorName = "MyTestErrorEvent";
            const string ErrorText = "Error Event logged from worker";
            const string WarningName = "MyTestWarningEvent";
            const string WarningText = "Warning Event logged from worker";
            var console = new MockConsole();
            BuildViewModel viewModel = new BuildViewModel();

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, console, DateTime.Now, viewModel, false, null, AdoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError(LoggingContext, new WorkerForwardedEvent()
                {
                    EventId = 100,
                    EventName = ErrorName,
                    EventKeywords = (int)global::BuildXL.Utilities.Instrumentation.Common.Keywords.UserError,
                    Text = ErrorText,
                });
                console.ValidateCall(MessageLevel.Info, ErrorText);
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedWarning(LoggingContext, new WorkerForwardedEvent()
                {
                    EventId = 200,
                    EventName = WarningName,
                    EventKeywords = (int)global::BuildXL.Utilities.Instrumentation.Common.Keywords.UserError,
                    Text = WarningText,
                });
                console.ValidateCall(MessageLevel.Info, WarningText);
            }
        }

        [Fact]
        public void BuildProgressTest()
        {
            var console = new MockConsole();
            BuildViewModel viewModel = new BuildViewModel();

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, console, DateTime.Now, viewModel, false, null, AdoConsoleMaxIssuesToLog))
            {
                listener.RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
                listener.RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
                var procsExecuting = 10;
                var procsSucceeded = 10;
                var procsFailed = 10;
                var procsSkipped = 10;
                var procsPending = 10;
                var procsWaiting = 10;
                var done = procsSucceeded + procsFailed + procsSkipped;
                var total = done + procsExecuting + procsWaiting + procsPending;
                var processPercent = (100.0 * done) / (total * 1.0);
                var currentProgress = Convert.ToInt32(Math.Floor(processPercent));

                global::BuildXL.Scheduler.Scheduler.LogPipStatus(LoggingContext,
                    pipsSucceeded: 10,
                    pipsFailed: 10,
                    pipsSkippedDueToFailedDependencies: 10,
                    pipsRunning: 10,
                    pipsReady: 10,
                    pipsWaiting: 10,
                    pipsWaitingOnSemaphore: 10,
                    servicePipsRunning: 10,
                    perfInfoForConsole: "",
                    pipsWaitingOnResources: 10,
                    procsExecuting: procsExecuting,
                    procsSucceeded: procsSucceeded,
                    procsFailed: procsFailed,
                    procsSkippedDueToFailedDependencies: procsSkipped,
                    procsPending: procsPending,
                    procsWaiting: procsWaiting,
                    procsCacheHit: 10,
                    procsNotIgnored: 10,
                    limitingResource: "",
                    perfInfoForLog: "",
                    overwriteable: true,
                    copyFileDone: 100,
                    copyFileNotDone: 100,
                    writeFileDone: 10,
                    writeFileNotDone: 10,
                    procsRemoted: 0);
                console.ValidateCall(MessageLevel.Info, $"##vso[task.setprogress value={currentProgress};]Pip Execution phase");
            }
        }
    }
}
