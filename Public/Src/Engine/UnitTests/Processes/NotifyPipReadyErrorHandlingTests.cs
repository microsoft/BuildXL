// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public class NotifyPipReadyErrorHandlingTests : SandboxedProcessTestBase
    {
        public NotifyPipReadyErrorHandlingTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [FactIfSupported(requiresEBPFEnabled: true)]
        public void PipReadyExceptionCausesInfraError()
        {
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = false,
                MonitorChildProcesses = false,
                PipId = GetNextPipId(),
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    this,
                    "/usr/bin/echo",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext)
                {
                    Arguments = "hello",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = 0x1234,
                    PipDescription = "TestPipReadyFailure",
                    SandboxConnection = new FailingNotifyPipReadySandboxConnection(),
                };

            using var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();

            // The process should be marked as having sandbox failures due to the exception in NotifyPipReady
            XAssert.AreEqual(ExitCodes.MessageProcessingFailure, result.ExitCode, 
                $"Expected MessageProcessingFailure exit code, got {result.ExitCode}");

            // Verify that sandbox internal error was logged (full message and error message)
            AssertVerboseEventLogged(LogEventId.FullSandboxInternalErrorMessage);
            AssertVerboseEventLogged(LogEventId.SandboxInternalError);
        }

        /// <summary>
        /// A sandbox connection that throws a BuildXLException on NotifyPipReady to simulate an EBPF initialization failure.
        /// </summary>
        private sealed class FailingNotifyPipReadySandboxConnection : ISandboxConnection
        {
            public SandboxKind Kind => SandboxKind.LinuxDetours;
            public bool IsInTestMode => true;

            public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion)
            {
                throw new BuildXLException("Simulated EBPF daemon initialization failure");
            }

            public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process) => true;
            public bool NotifyUsage(uint cpuUsageBasisPoints, uint availableRamMB) => true;
            public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName) => Array.Empty<(string, string)>();
            public void OverrideProcessStartInfo(ProcessStartInfo processStartInfo) { }
            public bool NotifyRootProcessExited(long pipId, SandboxedProcessUnix process) => false;
            public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process) => true;
            public void NotifyPipProcessTerminated(long pipId, int processId) { }
            public void Dispose() { }
        }
    }
}
