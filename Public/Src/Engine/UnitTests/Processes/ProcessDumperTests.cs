// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Interop;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public unsafe sealed class ProcessDumperTests : TemporaryStorageTestBase
    {
        private ITestOutputHelper m_testOutputHelper;
        public ProcessDumperTests(ITestOutputHelper output)
            : base(output)
        {
            m_testOutputHelper = output;
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
                    Process p = null;
                    bool success;
                    Exception failure;
                    try
                    {
                        p = Process.Start(new ProcessStartInfo(cmd, string.Format(@" /K {0} /K {0} /K ping localhost -t", cmd))
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                        });

                        // Make sure ping is actually running before dumping. Otherwise the process tree might not be fully created.
                        bool found = false;
                        Stopwatch sw = Stopwatch.StartNew();
                        StringBuilder output = new StringBuilder();
                        while (sw.Elapsed.TotalSeconds < 30)
                        {
                            string line = p.StandardOutput.ReadLine();
                            output.AppendLine(line);
                            if (line.Contains("Pinging"))
                            {
                                found = true;
                                break;
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
        /// Sends a signal to a processes identified by pid
        /// </summary>
        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern unsafe int SendSignal(int pid, int signal);

        private const int SIG_ABRT = 6;

        // TODO: Expand this test to also require super user privilages and and make sure the core dump utilities wrote both,
        //       the thread tid mappings and the core dump file to the system location specified
        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void CoreDumpTest()
        {
            string dumpPath = Path.Combine(TemporaryDirectory, "core_dumps");
            Directory.CreateDirectory(dumpPath);

            var testBinRoot = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetExecutingAssembly()));
            string testProcessFolder = Path.Combine(testBinRoot, "TestProcess");
            string platformDir = Dispatch.CurrentOS().ToString();
            string exe = Path.Combine(testProcessFolder, platformDir, "CoreDumpTester");

            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                RedirectStandardOutput = true,
                Arguments = dumpPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(info))
            {
                process.OutputDataReceived += new DataReceivedEventHandler((object sendingProcess, DataReceivedEventArgs outLine) =>
                {
                    var p = (Process)sendingProcess;
                    p.CancelOutputRead();

                    SendSignal(process.Id, SIG_ABRT);
                });

                process.BeginOutputReadLine();
                process.WaitForExit();

                XAssert.IsTrue(process.HasExited);

                var pathToTidMappings = Path.Combine(dumpPath, "thread_tids");
                bool tidMappingsExist = File.Exists(pathToTidMappings);
                XAssert.IsTrue(tidMappingsExist);

                var tidMappingsNotEmpty = new FileInfo(pathToTidMappings).Length > 0;
                XAssert.IsTrue(tidMappingsNotEmpty);
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

            var nonExistentProcessId = -1;
            Exception failure;
            bool ok = ProcessDumper.TryDumpProcessAndChildren(nonExistentProcessId, dumpPath, out failure);
            XAssert.IsFalse(ok, "Expected dump to fail");
            XAssert.IsNotNull(failure);
            XAssert.AreEqual(typeof(BuildXLException), failure.GetType());
            var failureSnippet = $"ArgumentException: Process with an Id of {nonExistentProcessId} is not running";
            XAssert.IsTrue(failure.ToString().Contains(failureSnippet), $"Expected error to contain '{failureSnippet}' but it doesn't: '{failure}'");
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
