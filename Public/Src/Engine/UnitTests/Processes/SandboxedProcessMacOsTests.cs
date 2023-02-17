// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop.Unix;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Interop.Unix.Sandbox.AccessReport;

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresMacOperatingSystem: true)]
    public class SandboxedProcessMacOsTest : SandboxedProcessTestBase
    {
        public SandboxedProcessMacOsTest(ITestOutputHelper output)
            : base(output) { }

        private sealed class Connection : ISandboxConnection
        {
            public ulong MinReportQueueEnqueueTime { get; set; }

            public delegate void ProcessTerminatedHandler(long pipId, int processId);

            public event ProcessTerminatedHandler ProcessTerminated;

            public TimeSpan CurrentDrought
            {
                get
                {
                    ulong nowNs = Sandbox.GetMachAbsoluteTime();
                    ulong minReportTimeNs = MinReportQueueEnqueueTime;
                    return TimeSpan.FromTicks(nowNs > minReportTimeNs ? (long)((nowNs - minReportTimeNs) / 100) : 0);
                }
            }

            public void Dispose() { }

            public bool IsInTestMode => true;

            public bool NotifyUsage(uint cpuUsage, uint availableRamMB) { return true; }
            public void NotifyPipReady(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process, Task reportCompletion) {}
            public bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process) { return true; }
            public IEnumerable<(string, string)> AdditionalEnvVarsToSet(SandboxedProcessInfo info, string uniqueName) { return Enumerable.Empty<(string, string)>(); }

            public void NotifyPipProcessTerminated(long pipId, int processId) { ProcessTerminated?.Invoke(pipId, processId); }

            public void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process) {}

            public bool NotifyPipFinished(long pipId, SandboxedProcessUnix process) { return true; }

            public void ReleaseResources() { }

            public SandboxKind Kind => SandboxKind.MacOsKext;
        }

        private readonly Connection s_connection = new Connection();

        private class ReportInstruction
        {
            public SandboxedProcessUnix Process;
            public Sandbox.AccessReportStatistics Stats;
            public int Pid;
            public FileOperation Operation;
            public string Path;
            public bool Allowed;
        }

        [Fact]
        public async Task CheckProcessTreeTimoutOnReportQueueStarvationAsync()
        {
            var processInfo = CreateProcessInfoWithSandboxConnection(Operation.Echo("hi"));
            processInfo.ReportQueueProcessTimeoutForTests = TimeSpan.FromMilliseconds(10);

            // Set the last enqueue time to now
            s_connection.MinReportQueueEnqueueTime = Sandbox.GetMachAbsoluteTime();

            using (var process = CreateAndStartSandboxedProcess(processInfo))
            {
                // Post nothing to the report queue, and the process tree must be timed out
                // after ReportQueueProcessTimeout has been reached.
                var result = await process.GetResultAsync();

                XAssert.IsTrue(result.Killed, "Expected process to have been killed");
                XAssert.IsFalse(result.TimedOut, "Didn't expect process to have timed out");
            }
        }

        [Fact]
        public async Task CheckProcessTreeTimoutOnReportQueueStarvationAndStuckRootProcessAsync()
        {
            var processInfo = CreateProcessInfoWithSandboxConnection(Operation.Block(), measureTimings: true);
            processInfo.ReportQueueProcessTimeoutForTests = TimeSpan.FromMilliseconds(10);

            // Set the last enqueue time to now
            s_connection.MinReportQueueEnqueueTime = Sandbox.GetMachAbsoluteTime();
            using (var process = CreateAndStartSandboxedProcess(processInfo))
            {
                // Post nothing to the report queue, and the process tree must be timed out after ReportQueueProcessTimeout
                // has been reached, including the stuck root process
                var result = await process.GetResultAsync();

                XAssert.IsTrue(result.Killed, "Expected process to have been killed");
                XAssert.IsFalse(result.TimedOut, "Didn't expect process to have timed out");
            }
        }

        [SuppressMessage("AsyncUsage", "AsyncFixer04:DisposableObjectUsedInFireForgetAsyncCall", Justification = "The task is awaited before the object is disposed")]
        [Fact]
        public async Task CheckProcessTreeTimoutOnNestedChildProcessTimeoutWhenRootProcessExitedAsync()
        {
            var processInfo = CreateProcessInfoWithSandboxConnection(Operation.Echo("hi"));
            processInfo.NestedProcessTerminationTimeout = TimeSpan.FromMilliseconds(10);

            // Set the last enqueue time to now
            s_connection.MinReportQueueEnqueueTime = Sandbox.GetMachAbsoluteTime();

            using (var process = CreateAndStartSandboxedProcess(processInfo))
            {
                var time = s_connection.MinReportQueueEnqueueTime;
                var childProcessPath = "/dummy/exe2";
                var childProcessPid = process.ProcessId + 1;

                // first post some reports indicating that
                //   - a child process was spawned
                //   - the main process exited
                // (not posting that the child process exited)
                var postTask1 = GetContinuouslyPostAccessReportsTask(process, new List<ReportInstruction>
                {
                    new ReportInstruction() {
                        Process = process,
                        Operation = FileOperation.OpProcessStart,
                        Stats = new Sandbox.AccessReportStatistics()
                        {
                            EnqueueTime = time + ((ulong) TimeSpan.FromMilliseconds(100).Ticks * 100),
                            DequeueTime = time + ((ulong) TimeSpan.FromMilliseconds(200).Ticks * 100),
                        },
                        Pid = childProcessPid,
                        Path = childProcessPath,
                        Allowed = true
                    },
                    new ReportInstruction() {
                        Process = process,
                        Operation = FileOperation.OpProcessExit,
                        Stats = new Sandbox.AccessReportStatistics()
                        {
                            EnqueueTime = time + ((ulong) TimeSpan.FromMilliseconds(300).Ticks * 100),
                            DequeueTime = time + ((ulong) TimeSpan.FromMilliseconds(400).Ticks * 100),
                        },
                        Pid = process.ProcessId,
                        Path = "/dummy/exe",
                        Allowed = true
                    },
                    new ReportInstruction() {
                        Process = process,
                        Operation = FileOperation.OpKAuthCreateDir,
                        Stats = new Sandbox.AccessReportStatistics()
                        {
                            EnqueueTime = time + ((ulong) TimeSpan.FromMilliseconds(500).Ticks * 100),
                            DequeueTime = time + ((ulong) TimeSpan.FromMilliseconds(600).Ticks * 100),
                        },
                        Pid = childProcessPid,
                        Path = childProcessPath,
                        Allowed = true
                    },
                });

                // SandboxedProcessMac should decide to kill the process because its child survived;
                // when it does that, it will call this callback.  When that happens, we must post
                // OpProcessTreeCompleted because SandboxedProcessMac will keep waiting for it.
                s_connection.ProcessTerminated += (pipId, pid) =>
                {
                    postTask1.GetAwaiter().GetResult();
                    ContinuouslyPostAccessReports(process, new List<ReportInstruction>
                    {
                        new ReportInstruction() {
                            Process = process,
                            Operation = FileOperation.OpProcessTreeCompleted,
                            Stats = new Sandbox.AccessReportStatistics()
                            {
                                EnqueueTime = time + ((ulong) TimeSpan.FromMilliseconds(900).Ticks * 100),
                                DequeueTime = time + ((ulong) TimeSpan.FromMilliseconds(1000).Ticks * 100),
                            },
                            Pid = process.ProcessId,
                            Path = "/dummy/exe",
                            Allowed = true
                        }
                    });
                };

                var result = await process.GetResultAsync();
                await postTask1; // await here as well just to make AsyncFixer happy

                XAssert.IsTrue(result.Killed, "Expected process to have been killed");
                XAssert.IsFalse(result.TimedOut, "Didn't expect process to have timed out");
                XAssert.IsNotNull(result.SurvivingChildProcesses, "Expected surviving child processes");
                XAssert.IsTrue(result.SurvivingChildProcesses.Any(p => p.Path == childProcessPath),
                    $"Expected surviving child processes to contain {childProcessPath}; " +
                    $"instead it contains: {string.Join(", ", result.SurvivingChildProcesses.Select(p => p.Path))}");
            }
        }

        private SandboxedProcessInfo CreateProcessInfoWithSandboxConnection(Operation op, bool measureTimings = false)
        {
            var info = ToProcessInfo(ToProcess(op), sandboxConnection: s_connection);
            info.MonitoringConfig = measureTimings ? new SandboxedProcessResourceMonitoringConfig(enabled: true, refreshInterval: TimeSpan.FromMilliseconds(10)) : null;
            return info;
        }

        private SandboxedProcessUnix CreateAndStartSandboxedProcess(SandboxedProcessInfo info)
        {
            var process = new SandboxedProcessUnix(info);
            process.Start();
            return process;
        }

        private void ContinuouslyPostAccessReports(SandboxedProcessUnix process, List<ReportInstruction> instructions)
        {
            Analysis.IgnoreResult(GetContinuouslyPostAccessReportsTask(process, instructions), "fire and forget");
        }

        private Task GetContinuouslyPostAccessReportsTask(SandboxedProcessUnix process, List<ReportInstruction> instructions)
        {
            XAssert.IsNotNull(process);
            XAssert.IsNotNull(instructions);
            return Task.Run(async () =>
            {
                foreach (var instr in instructions)
                {
                    PostAccessReport(instr.Process, instr.Operation, instr.Stats, instr.Pid, instr.Path, instr.Allowed);

                    // Advance the minimum enqueue time
                    s_connection.MinReportQueueEnqueueTime = instr.Stats.EnqueueTime;

                    // wait a bit before sending the next one
                    await Task.Delay(100);
                }
            });
        }

        private static Sandbox.AccessReport PostAccessReport(SandboxedProcessUnix proc, FileOperation operation, Sandbox.AccessReportStatistics stats,
                                                             int pid = 1234, string path = "/dummy/path", bool allowed = true)
        {
            var report = new Sandbox.AccessReport
            {
                Operation      = operation,
                Statistics     = stats,
                Pid            = pid,
                PathOrPipStats = Sandbox.AccessReport.EncodePath(path),
                Status         = allowed ? (uint)FileAccessStatus.Allowed : (uint)FileAccessStatus.Denied
            };

            proc.PostAccessReport(report);
            return report;
        }
    }
}
