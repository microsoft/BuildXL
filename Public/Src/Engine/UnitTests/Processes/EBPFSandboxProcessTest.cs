// Copyright (c) Microsoft Corporation.
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
using System.Diagnostics;
using System.Linq;

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

        [Fact]
        public void ValidateRingBufferExceedCapacity()
        {
            if (OperatingSystemHelperExtension.GetLinuxDistribution()?.Equals(new LinuxDistribution("ubuntu", new Version("20.04"))) == true)
            {
                // This test is valid for all supported Linux distributions supported by BuildXL except for Ubuntu 20.04.
                // TODO: Remove this check once support for Ubuntu 20.04 is dropped.
                return;
            }

            // For now this test only runs in ADO builds, where we can set the required capabilities without an interactive prompt.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                return;
            }

            // CODESYNC: Public/Src/Engine/UnitTests/Processes/Test.BuildXL.Processes.dsc
            string ringBufferTest = SandboxedProcessUnix.EnsureDeploymentFile("RingBufferTest/ringbuffer_test");

            // The test needs the ability to retrieve a pinned EBPF map
            UnixGetCapUtils.SetEBPFCapabilitiesIfNeeded(ringBufferTest);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    ringBufferTest,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = "",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF capacity test",
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

            // The test triggers the capacity exceeded callback by writing more than the ring buffer can hold.
            // The test should succeed, which means the capacity exceeded callback was called and all the expected events were written to the queue.
            XAssert.AreEqual(0, result.ExitCode);
        }

        [Theory]
        [InlineData("/test/this/path", "/test/this/other/path")]
        [InlineData("/test/this/path", "/unrelated/path")]
        public void ValidateIncrementalPaths(string path1, string path2)
        {
            if (OperatingSystemHelperExtension.GetLinuxDistribution()?.Equals(new LinuxDistribution("ubuntu", new Version("20.04"))) == true)
            {
                // This test is valid for all supported Linux distributions supported by BuildXL except for Ubuntu 20.04.
                // TODO: Remove this check once support for Ubuntu 20.04 is dropped.
                return;
            }

            // Skip test if not using EBPF sandbox
            if (!UsingEBPFSandbox)
            {
                return;
            }

            // For now this test only runs in ADO builds, where we can set the required capabilities without an interactive prompt.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                return;
            }

            // CODESYNC: Public/Src/Engine/UnitTests/Processes/Test.BuildXL.Processes.dsc
            string incrementalPathTest = SandboxedProcessUnix.EnsureDeploymentFile("RingBufferTest/incremental_path_test");

            // The test needs the ability to retrieve a pinned EBPF map
            UnixGetCapUtils.SetEBPFCapabilitiesIfNeeded(incrementalPathTest);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    incrementalPathTest,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = $"{path1} {path2}",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF incremental test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(ebpfDaemonTask: null),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            var messages = m_eventListener.GetLogMessagesForEventId((int)LogEventId.LogDetoursDebugMessage);

            // On managed side, paths should roundtrip as is, in the same order.
            var managedSide = messages.Where(s => s.Contains("(( test_synthetic:10 ))")).ToList();
            XAssert.AreEqual(2, managedSide.Count);
            XAssert.Contains(managedSide[0], path1);
            XAssert.Contains(managedSide[1], path2);

            // On the native side, first path should be sent as is. Second path should be incrementally encoded.
            var nativeSide = messages.Where(s => s.Contains("kernel function: test_synthetic")).ToList();
            XAssert.AreEqual(2, nativeSide.Count);
            XAssert.Contains(nativeSide[0], $"path: '{path1}'");
            XAssert.Contains(nativeSide[1], $"path: '{path2.Substring(CommonPrefixLength(path1, path2))}'");
        }

        private static int CommonPrefixLength(string s1, string s2)
        {
            int minLength = Math.Min(s1.Length, s2.Length);
            int i = 0;
            for (; i < minLength; i++)
            {
                if (s1[i] != s2[i])
                {
                    break;
                }
            }

            return i;
        }
    }
}
