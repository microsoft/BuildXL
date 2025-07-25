﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;

#nullable enable

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public class EBPFSandboxProcessTest : SandboxedProcessTestBase
    {        
        public EBPFSandboxProcessTest(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [Fact]
        public void ValidateEBPFLoader()
        {
            if (OperatingSystemHelperExtension.GetLinuxDistribution()?.Equals(new LinuxDistribution("ubuntu", new Version("20.04"))) == true)
            {
                // This test is valid for all supported Linux distributions supported by BuildXL except for Ubuntu 20.04.
                // TODO: Remove this check once support for Ubuntu 20.04 is dropped.
                return;
            }

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            int maxConcurrency = 42;

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    "/usr/bin/echo",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true,
                    maxConcurrency: maxConcurrency)
                {
                    Arguments = "hi",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF test",
                    // Unconditionally load the EBPF programs
                    // This is actually testing the loading of the currently defined programs, regardless of the version of the running daemon (which may
                    // be coming from an LKG) 
                    EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(new Dictionary<string, string>
                    {
                        { SandboxConnectionLinuxDetours.BuildXLUnconditionallyLoadEBPF, "1" },
                    }),
                    SandboxConnection = new SandboxConnectionLinuxEBPF(ebpfDaemonTask: null),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();

            // Verify that the process ran successfully, which means the EBPF loader was able to load the programs
            XAssert.AreEqual(0, result.ExitCode);

            // Verify EBPF actually loaded the programs
            string sandboxMessages = string.Join(Environment.NewLine, m_eventListener.GetLog());
            XAssert.Contains(sandboxMessages, "Unconditionally loading EBPF programs");
            // Validate specified concurrency properly reached the daemon 
            XAssert.IsTrue(sandboxMessages.Contains($"Concurrency was originally requested to be '{maxConcurrency}'")
                || sandboxMessages.Contains($"EBPF map sizes set to '{maxConcurrency}'"));
        }
    }
}
