// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Interop;
using BuildXL.Native.Processes;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using WindowsProcess = BuildXL.Interop.Windows.Process;
#if !FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace Test.BuildXL.Processes
{
    public unsafe sealed class ProcessDumperTests : TemporaryStorageTestBase
    {
        private readonly ITestOutputHelper m_testOutputHelper;

        public ProcessDumperTests(ITestOutputHelper output)
            : base(output)
        {
            m_testOutputHelper = output;
        }

        /// <summary>
        /// Don't crash when dumping a process that's already exited
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void DumpProcessThatHasExited()
        {
            string cmdPath = Environment.GetEnvironmentVariable("comspec");
            Process p = Process.Start(cmdPath, " /c");
            p.WaitForExit();
            Exception dumpException;
            bool result = ProcessDumper.TryDumpProcessAndChildren(p.Id, TemporaryDirectory, out dumpException);
            XAssert.IsFalse(result, "Expected failure since there is no process to dump");
        }

        /// <summary>
        /// Don't dump processes that are running under a different user. This provides protection from dumping system processes
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void OnlyDumpUnderSameUsername()
        {
            string cmdPath = Environment.GetEnvironmentVariable("comspec");
            Process p = Process.Start(cmdPath, " /c");
            p.WaitForExit();
            var startTime = p.StartTime;

            Process systemProcess = Process.GetProcessesByName("System")[0];
            XAssert.IsNotNull(systemProcess);

            Exception exception;

            bool result = ProcessDumper.TryDumpProcessAndChildren(systemProcess.Id, TemporaryDirectory, out exception);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void DumpProcessTreeTest()
        {
            // This test has proven to occasionally be flakey, primarily by failing to create some of the dump files.
            // We add retries to prevent it from failing our automation.
            int MaxTries = 4;
            for (int i = 1; i <= MaxTries; i++)
            {
                try
                {
                    string dumpPath = Path.Combine(TemporaryDirectory, "dumps" + i);
                    Directory.CreateDirectory(dumpPath);
                    var cmd = CmdHelper.CmdX64;
                    var ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "ping.exe");
                    Process p = null;
                    bool success;
                    Exception failure;
                    try
                    {
                        p = Process.Start(new ProcessStartInfo(cmd, string.Format(@$" /K {cmd} /K {cmd} /K {ping} localhost -t"))
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                        });

                        // Make sure ping is actually running before dumping. Otherwise the process tree might not be fully created.
                        bool found = false;
                        Stopwatch sw = Stopwatch.StartNew();
                        StringBuilder output = new StringBuilder();
                        while (sw.Elapsed.TotalSeconds < 30)
                        {
                            string line = p.StandardOutput.ReadLine();
                            if (line != null)
                            {
                                output.AppendLine(line);
                                if (line.Contains("Pinging"))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (!found)
                        {
                            TestRetryException.Assert(false, "Didn't find result of ping in standard out. Full output:" + Environment.NewLine +
                                output.ToString() + Environment.NewLine + p.StandardError.ReadToEnd());
                        }

                        var processIds = ProcessDumper.GetProcessTreeIds(p.Id, 0);
                        XAssert.IsTrue(processIds.Count == 1, processIds.Count.ToString());

                        success = ProcessDumper.TryDumpProcessAndChildren(p.Id, dumpPath, out failure);
                    }
                    finally
                    {
                        Stopwatch killTimer = Stopwatch.StartNew();
                        if (p != null)
                        {
                            while (killTimer.Elapsed.TotalSeconds < 10)
                            {
                                // Make sure we kill all of the child processes before exiting the test
                                var kill = Process.Start(new ProcessStartInfo("taskkill", "/pid " + p.Id + " /T /F")
                                {
                                    CreateNoWindow = true,
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                });
                                kill.WaitForExit();

                                if (p.HasExited)
                                {
                                    break;
                                }
                                else
                                {
                                    Thread.Sleep(200);
                                }
                            }
                        }
                    }

                    // Make sure all of the dumps exist
                    TestRetryException.Assert(success, "Dumping processes failed: " + (failure == null ? "Failure was null" : failure.ToString()));

                    AssertFileExists(Path.Combine(dumpPath, "1_cmd.exe.dmp"));

#if NET_FRAMEWORK
                    // ConHost is not launched with the CoreCLR through the xUnit runner
                    AssertFileExists(Path.Combine(dumpPath, "1_2_cmd.exe.dmp"));
                    AssertFileExists(Path.Combine(dumpPath, "1_2_1_cmd.exe.dmp"));
                    AssertFileExists(Path.Combine(dumpPath, "1_2_1_1_PING.EXE.dmp"));
#endif
                }
                catch (TestRetryException ex)
                {
                    if (i >= MaxTries)
                    {
                        TestRetryException.Assert(false, "Test failed after exhausting retries. Retry number {0}. Failure: {1}", i, ex);
                    }

                    m_testOutputHelper.WriteLine("Test iteration failed. Pausing and retrying");
                    // Take a break before retrying the test
                    Thread.Sleep(3000);
                    continue;
                }

                // Success
                break;
            }
        }

        /// <summary>
        /// Exception for test failure that allows test to be retried
        /// </summary>
        public class TestRetryException : Exception
        {
            public TestRetryException(string message) : base(message)
            {
            }

            public static void Assert(bool condition, string format, params object[] args)
            {
                if (!condition)
                {
                    throw new TestRetryException(string.Format(format, args));
                }

            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void DumpProcessTreeWhenProcessDoesNotExist()
        {
            string dumpPath = Path.Combine(TemporaryDirectory, "dumps2");
            Directory.CreateDirectory(dumpPath);

            var nonExistentProcessId = 999999999;
            Exception failure;
            bool ok = ProcessDumper.TryDumpProcessAndChildren(nonExistentProcessId, dumpPath, out failure);
            XAssert.IsFalse(ok, "Expected dump to fail");
            XAssert.IsNotNull(failure);
            XAssert.AreEqual(typeof(BuildXLException), failure.GetType());
            var failureSnippet = $"ArgumentException: Process with an Id of {nonExistentProcessId} is inaccessible or not running";
            XAssert.IsTrue(failure.ToString().Contains(failureSnippet), $"Expected error to contain '{failureSnippet}' but it doesn't: '{failure}'");
        }

        /// <summary>
        /// Exercises the new fallback dump + thread state diagnostics on a suspended process.
        /// A suspended process simulates the scenario where Detours injection is in progress
        /// and the full heap dump may fail.
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void DumpSuspendedProcessExercisesFallbackAndThreadDiagnostics()
        {
            string dumpPath = Path.Combine(TemporaryDirectory, "suspended_dumps");
            Directory.CreateDirectory(dumpPath);

            // Create a process in suspended state using CREATE_SUSPENDED
            string cmdPath = Environment.GetEnvironmentVariable("comspec");
            var startInfo = new STARTUPINFO();
            startInfo.cb = Marshal.SizeOf<STARTUPINFO>();
            var processInfo = new PROCESS_INFORMATION();

            bool created = CreateProcess(
                cmdPath,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x00000004, // CREATE_SUSPENDED
                IntPtr.Zero,
                null,
                ref startInfo,
                out processInfo);

            XAssert.IsTrue(created, $"CreateProcess failed with error {Marshal.GetLastWin32Error()}");

            try
            {
                int processId = (int)processInfo.dwProcessId;
                m_testOutputHelper.WriteLine($"Created suspended process {processId} (cmd.exe)");

                // Collect debug log messages
                var debugMessages = new List<string>();
                Action<string> debugLogger = (msg) =>
                {
                    debugMessages.Add(msg);
                    m_testOutputHelper.WriteLine($"[DEBUG] {msg}");
                };

                // PROCESS_DUP_HANDLE is required for MiniDumpWriteDump to enumerate handle data
                // (MiniDumpWithHandleData). Internally, it calls DuplicateHandle on each handle in
                // the target process to inspect its type and name. Without this right, dump creation
                // may fail with access errors for suspended or partially-initialized processes.
                // See: https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump
                using (SafeProcessHandle processHandle = ProcessUtilities.OpenProcess(
                    ProcessSecurityAndAccessRights.PROCESS_QUERY_INFORMATION |
                    ProcessSecurityAndAccessRights.PROCESS_VM_READ |
                    ProcessSecurityAndAccessRights.PROCESS_DUP_HANDLE,
                    false,
                    (uint)processId))
                {
                    string dumpFile = Path.Combine(dumpPath, $"suspended_cmd_{processId}.zip");
                    bool dumpResult = ProcessDumper.TryDumpProcess(
                        processHandle,
                        processId,
                        dumpFile,
                        out Exception dumpException,
                        compress: true,
                        debugLogger: debugLogger);

                    // Log all captured debug messages
                    m_testOutputHelper.WriteLine($"\n=== Dump result: {(dumpResult ? "SUCCESS" : "FAILED")} ===");
                    if (dumpException != null)
                    {
                        m_testOutputHelper.WriteLine($"Exception: {dumpException}");
                    }

                    foreach (var msg in debugMessages)
                    {
                        m_testOutputHelper.WriteLine($"  {msg}");
                    }

                    // Whether full dump or lightweight fallback succeeded, we should get a file
                    if (dumpResult)
                    {
                        XAssert.IsTrue(File.Exists(dumpFile), $"Dump file should exist at {dumpFile}");
                        var fileSize = new FileInfo(dumpFile).Length;
                        m_testOutputHelper.WriteLine($"Dump file size: {fileSize:N0} bytes");
                        XAssert.IsTrue(fileSize > 0, "Dump file should not be empty");

                        // If fallback kicked in, we should see the diagnostic messages
                        if (debugMessages.Any(m => m.Contains("lightweight dump flags")))
                        {
                            m_testOutputHelper.WriteLine(">>> Lightweight fallback was used (full dump failed as expected for suspended process)");
                            XAssert.IsTrue(debugMessages.Any(m => m.Contains("thread state")),
                                "Should have logged thread state diagnostics");
                        }
                        else
                        {
                            m_testOutputHelper.WriteLine(">>> Full dump succeeded (process was dumpable even while suspended)");
                        }
                    }
                    else
                    {
                        // Both full and lightweight dumps failed — still verify diagnostics were captured
                        m_testOutputHelper.WriteLine(">>> Both dump attempts failed — verifying diagnostics were logged");
                        XAssert.IsTrue(debugMessages.Count > 0, "Should have debug diagnostics even on failure");
                        XAssert.IsTrue(debugMessages.Any(m => m.Contains("thread state")),
                            "Should have thread state diagnostics on failure");
                    }
                }
            }
            finally
            {
                // Always terminate and clean up the suspended process
                TerminateProcess(processInfo.hProcess, 0);
                WindowsProcess.CloseHandle(processInfo.hProcess);
                WindowsProcess.CloseHandle(processInfo.hThread);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize;
            public int dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        private void AssertFileExists(string path)
        {
            TestRetryException.Assert(
                File.Exists(path),
                "File {0} did not exist. Directory contents: {1}{2}",
                path,
                Environment.NewLine,
                string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.GetDirectoryName(path))));
        }
    }
}
