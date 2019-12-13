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
    }
}
