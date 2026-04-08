// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes
{
    public sealed class ProcessStartEventListenerTest : SandboxedProcessTestBase
    {
        public ProcessStartEventListenerTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task CallbackIsInvokedWithRootProcessId()
        {
            int callbackPid = 0;
            int callbackCount = 0;
            var sandboxKind = GetEBPFAwareSandboxConnection()?.Kind;

            var listener = new ProcessStartEventListener(pid =>
                {
                    Interlocked.Increment(ref callbackCount);
                    Volatile.Write(ref callbackPid, pid);
                },
                sandboxKind
            );

            var info = ToProcessInfo(
                EchoProcess("Hi", useStdErr: false),
                detoursListener: listener);

            info.FileAccessManifest.ReportFileAccesses = true;

            SandboxedProcessResult result;
            using (var process = await StartProcessAsync(info))
            {
                result = await process.GetResultAsync();
            }

            // The callback should have been invoked exactly once
            XAssert.AreEqual(1, Volatile.Read(ref callbackCount), "Callback should be invoked exactly once");

            // The PID from the callback must match the actual self-reported root process PID
            // For EBPF-based monitoring, the root process is the second one reported (as the first one is the ebpf-runner), 
            // while for other monitoring it's the first one.
            XAssert.AreEqual((int)result.Processes[sandboxKind == SandboxKind.LinuxEBPF ? 1 : 0].ProcessId, Volatile.Read(ref callbackPid),
                "Callback PID should match the root sandboxed process PID, not a child"  + 
                string.Join(System.Environment.NewLine, result.FileAccesses.Select(report => report.Describe())));
        }

        [Fact]
        public async Task CallbackIsInvokedOnlyForFirstProcessWhenChildIsSpawned()
        {
            int callbackPid = 0;
            int callbackCount = 0;
            var sandboxKind = GetEBPFAwareSandboxConnection()?.Kind;

            var listener = new ProcessStartEventListener(pid =>
                {
                    Interlocked.Increment(ref callbackCount);
                    Volatile.Write(ref callbackPid, pid);
                },
                sandboxKind
            );

            // Create a process that spawns a child (which also does an echo)
            var process = ToProcess(
                Operation.Spawn(
                    Context.PathTable,
                    waitToFinish: true,
                    Operation.Echo("child")),
                Operation.Echo("parent"));

            var info = ToProcessInfo(process, detoursListener: listener);
            info.FileAccessManifest.ReportFileAccesses = true;

            SandboxedProcessResult result;
            using (var sandboxedProcess = await StartProcessAsync(info))
            {
                result = await sandboxedProcess.GetResultAsync();
            }

            // Multiple processes should have been reported (parent + child at minimum)
            XAssert.IsTrue(result.Processes?.Count > 1,
                $"Expected more than 1 reported process, but got {result.Processes?.Count ?? 0}");

            // The callback should have been invoked exactly once despite multiple processes
            XAssert.AreEqual(1, Volatile.Read(ref callbackCount),
                "Callback should be invoked exactly once even when child processes are spawned");

            // The PID from the callback must match the root process PID
            // For EBPF-based monitoring, the root process is the second one reported (as the first one is the ebpf-runner), 
            // while for other monitoring it's the first one.
            XAssert.AreEqual((int)result.Processes[sandboxKind == SandboxKind.LinuxEBPF ? 1 : 0].ProcessId, Volatile.Read(ref callbackPid),
                "Callback PID should match the root sandboxed process PID, not a child"  + 
                string.Join(System.Environment.NewLine, result.FileAccesses.Select(report => report.Describe())));
        }
    }
}
