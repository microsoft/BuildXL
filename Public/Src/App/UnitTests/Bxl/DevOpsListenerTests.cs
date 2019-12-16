// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL
{
    public class DevOpsListenerTests : XunitBuildXLTest
    {
        public DevOpsListenerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void EnsurePayloadParsableWithoutCrash()
        {
            StandardConsole console = new StandardConsole(colorize: false, animateTaskbar: false, supportsOverwriting: false);
            BuildViewModel viewModel = new BuildViewModel();
            viewModel.BuildSummary = new BuildSummary(Path.Combine(TestOutputDirectory, "test.md"));

            using (AzureDevOpsListener listener = new AzureDevOpsListener(Events.Log, console, DateTime.Now, viewModel, false, null))
            {
                listener.RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
                global::BuildXL.Processes.Tracing.Logger.Log.PipProcessError(LoggingContext,
                    pipSemiStableHash: 24,
                    pipDescription: "my cool pip",
                    pipSpecPath: @"specs\mypip.dsc",
                    pipWorkingDirectory: @"specs\workingDir",
                    pipExe: "coolpip.exe",
                    outputToLog: "Failure message",
                    extraOutputMessage: null,
                    pathsToLog: null,
                    exitCode: -1,
                    optionalMessage: "what does this do?");
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

                global::BuildXL.Scheduler.Tracing.Logger.Log.LogPipStatus(LoggingContext,
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
