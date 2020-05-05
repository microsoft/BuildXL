// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Processes.Tracing;
using BuildXL.ViewModel;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL
{
    public class DevOpsListenerTests : XunitBuildXLTest
    {
        PipProcessErrorEventFields m_eventFields;

        public DevOpsListenerTests(ITestOutputHelper output) : base(output){}

        [Fact]
        public void LogAzureDevOpsIssueTest()
        {
            m_eventListener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += eventData =>
            {
                m_eventFields = new PipProcessErrorEventFields(eventData.Payload, false);
            };

            var testElements = CreatePipProcessErrorTestElement();
            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.console, DateTime.Now, testElements.viewModel, false, null))
            {
                listener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
                global::BuildXL.Processes.Tracing.Logger.Log.PipProcessError(LoggingContext,
                    testElements.pipProcessError.PipSemiStableHash,
                    testElements.pipProcessError.PipDescription,
                    testElements.pipProcessError.PipSpecPath,
                    testElements.pipProcessError.PipWorkingDirectory,
                    testElements.pipProcessError.PipExe,
                    testElements.pipProcessError.OutputToLog,
                    testElements.pipProcessError.MessageAboutPathsToLog,
                    testElements.pipProcessError.PathsToLog,
                    testElements.pipProcessError.ExitCode,
                    testElements.pipProcessError.OptionalMessage,
                    testElements.pipProcessError.ShortPipDescription);
                testElements.console.ValidateCall(MessageLevel.Info, testElements.expectingConsoleLog);
                XAssert.AreEqual(m_eventFields, testElements.pipProcessError, "You may edit the PipProcessError event fields, update the test and/or struct PipProcessErrorEventFields.");
                AssertErrorEventLogged(LogEventId.PipProcessError);
            }
        }

        [Fact]
        public void ForwardedPipProcessErrorTest()
        {
            var testElements = CreatePipProcessErrorTestElement();
            var eventName = "PipProcessError";
            var text = "Pip process error message";
            var pipSemiStableHash = (long)24;

            m_eventListener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            m_eventListener.NestedLoggerHandler += eventData =>
            {
                m_eventFields = new PipProcessErrorEventFields(eventData.Payload, true);
            };

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, testElements.console, DateTime.Now, testElements.viewModel, false, null))
            {
                listener.RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
                global::BuildXL.Engine.Tracing.Logger.Log.DistributionWorkerForwardedError(LoggingContext, new WorkerForwardedEvent()
                {
                    EventId = (int)LogEventId.PipProcessError,
                    EventName = eventName,
                    EventKeywords = 0,
                    Text = text,
                    PipProcessErrorEvent = testElements.pipProcessError,
                });
                testElements.console.ValidateCall(MessageLevel.Info, testElements.expectingConsoleLog);
                XAssert.IsTrue(testElements.viewModel.BuildSummary.PipErrors.Exists(e => e.SemiStablePipId == $"Pip{(pipSemiStableHash):X16}"));
                XAssert.AreEqual(m_eventFields, testElements.pipProcessError, "You may edit the PipProcessError and/or WorkerForwardedEvent fields, and/or struct PipProcessErrorEventFields.");
                AssertErrorEventLogged(SharedLogEventId.DistributionWorkerForwardedError);
            }
        }

        private (PipProcessErrorEventFields pipProcessError, string expectingConsoleLog, MockConsole console, BuildViewModel viewModel) CreatePipProcessErrorTestElement()
        {

            var pipProcessError = new PipProcessErrorEventFields(
                (long)24,
                "my cool pip",
                @"specs\mypip.dsc",
                @"specs\workingDir",
                "coolpip.exe",
                "Failure message Line1\r\nFailure message Line2\rFailure message Line3\n",
                "Find output file in following path:",
                @"specs\workingDir\out.txt",
                -1,
                "what does this do?",
                "my pip");

            var processedOutputToLog = "Failure message Line1%0D%0A##[error]Failure message Line2%0D##[error]Failure message Line3%0A##[error]";
            var expectingConsoleLog = @$"##vso[task.logIssue type=error;][Pip0000000000000018, {pipProcessError.ShortPipDescription}, {pipProcessError.PipSpecPath}] - failed with exit code {pipProcessError.ExitCode}, {pipProcessError.OptionalMessage}%0D%0A##[error]{processedOutputToLog}%0D%0A##[error]{pipProcessError.MessageAboutPathsToLog}%0D%0A##[error]{pipProcessError.PathsToLog}";

            var console = new MockConsole();
            var viewModel = new BuildViewModel();
            var buildSummaryFilePath = Path.Combine(TestOutputDirectory, "test.md");
            viewModel.BuildSummary = new BuildSummary(buildSummaryFilePath);

            return (pipProcessError, expectingConsoleLog, console, viewModel);
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

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, console, DateTime.Now, viewModel, false, null))
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

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, console, DateTime.Now, viewModel, false, null))
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
                    copyFileDone:100,
                    copyFileNotDone: 100,
                    writeFileDone: 10,
                    writeFileNotDone:10);
                console.ValidateCall(MessageLevel.Info, $"##vso[task.setprogress value={currentProgress};]Pip Execution phase");
            }
        }
    }
}
