// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using FileUtilities = BuildXL.Native.IO.FileUtilities;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes
{
    public sealed class SandboxedProcessTest : SandboxedProcessTestBase
    {
        private ITestOutputHelper TestOutput { get; }

        public SandboxedProcessTest(ITestOutputHelper output)
            : base(output)
        {
            TestOutput = output;
        }

        private sealed class MyListener : IDetoursEventListener
        {
            private readonly ISet<string> m_fileAccesses = new HashSet<string>();

            public IEnumerable<string> FileAccesses => m_fileAccesses;
            public int FileAccessPathCount => m_fileAccesses.Count;
            public int ProcessMessageCount { get; private set; }
            public int DebugMessageCount { get; private set; }
            public int ProcessDataMessageCount { get; private set; }
            public int ProcessDetouringStatusMessageCount { get; private set; }

            private readonly List<string> m_debugMessages = new ();

            public IEnumerable<string> DebugMessages => m_debugMessages;

            public override void HandleDebugMessage(DebugData debugData)
            {
                DebugMessageCount++;
                m_debugMessages.Add(debugData.DebugMessage);
            }

            public override void HandleFileAccess(FileAccessData fileAccessData)
            {
                if (fileAccessData.Operation == ReportedFileOperation.Process)
                {
                    ProcessMessageCount++;
                }

                m_fileAccesses.Add(fileAccessData.Path);
            }

            public override void HandleProcessData(ProcessData processData)
            {
                ProcessDataMessageCount++;
            }

            public override void HandleProcessDetouringStatus(ProcessDetouringStatusData processDetouringStatusData)
            {
                ProcessDetouringStatusMessageCount++;
            }
        }

        private SandboxedProcessInfo GetEchoProcessInfo(string message, IDetoursEventListener detoursListener = null, bool useStdErr = false)
            => ToProcessInfo(EchoProcess(message, useStdErr), detoursListener: detoursListener);

        private SandboxedProcessInfo GetInfiniteWaitProcessInfo()
            => ToProcessInfo(ToProcess(Operation.Block()));

        private async Task CheckEchoProcessResult(SandboxedProcessResult result, string echoMessage)
        {
            var stdout = await result.StandardOutput.ReadValueAsync();
            var stderr = await result.StandardError.ReadValueAsync();
            if (result.Killed)
            {
                string survivingProcesses = result.SurvivingChildProcesses != null ? string.Join(",", result.SurvivingChildProcesses.Select(reportedProcess => reportedProcess.Path)) : "none";
                XAssert.Fail($"Process claims it or a child process was killed; exit code: {result.ExitCode}, stdout: '{stdout.Trim()}', " +
                    $"stderr: '{stderr.Trim()}'. Diagnostic information:{Environment.NewLine}{result.DiagnosticMessage}{Environment.NewLine}SurvivingChidren:{survivingProcesses}");
            }

            XAssert.AreEqual(0, result.ExitCode, "Unexpected error code; stdout: '{0}', stderr: '{1}'", stdout, stderr);
            XAssert.AreEqual(string.Empty, stderr.Trim());
            XAssert.AreEqual(echoMessage, stdout.Trim());
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // reported files are not consistent on Mac
        public async Task CheckDetoursNotifications()
        {
            async Task<SandboxedProcessResult> RunEchoProcess(IDetoursEventListener detoursListener = null)
            {
                var echoMessage = "Success";
                var info = GetEchoProcessInfo(echoMessage, detoursListener);

                info.FileAccessManifest.LogProcessData = true;
                info.FileAccessManifest.LogProcessDetouringStatus = true;
                info.FileAccessManifest.ReportFileAccesses = true;
                info.FileAccessManifest.ReportProcessArgs = true;
                info.DiagnosticsEnabled = true;

                var result = await RunProcess(info);
                await CheckEchoProcessResult(result, echoMessage);
                return result;
            }

            IEnumerable<string> CleanFileAccesses(IEnumerable<string> fileAccesses)
            {
                if (!OperatingSystemHelper.IsUnixOS)
                {
                    var inetCache = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache);
                    fileAccesses = fileAccesses.Where(a => !a.StartsWith(inetCache, OperatingSystemHelper.PathComparison));
                }

                return fileAccesses.Select(a => a.ToCanonicalizedPath()).Distinct();
            }

            int ComputeNumAccessedPaths(SandboxedProcessResult result)
            {
                return CleanFileAccesses(result.FileAccesses.Select(a => a.GetPath(Context.PathTable))).Count();
            }

            int numFilePathAccesses = 0;
            int numProcesses = 0;
            int numProcessDetoursStatuses = 0;

            // 1st run: no listener --> just record counts
            {
                var result = await RunEchoProcess();
                numFilePathAccesses = ComputeNumAccessedPaths(result);
                numProcesses = result.Processes.Count();
                numProcessDetoursStatuses = result.DetouringStatuses.Count();
            }

            // 2nd run: empty listener --> assert that the listener counts are 0s, and counts from 'result' didn't change
            {
                var myListener = new MyListener();
                var result = await RunEchoProcess(myListener);

                // check the listener didn't receive any messages
                XAssert.AreEqual(0, myListener.DebugMessageCount);
                XAssert.AreEqual(0, myListener.FileAccessPathCount);
                XAssert.AreEqual(0, myListener.ProcessDataMessageCount);
                XAssert.AreEqual(0, myListener.ProcessDetouringStatusMessageCount);

                // check that the message counts remained the same
                XAssert.AreEqual(numFilePathAccesses, ComputeNumAccessedPaths(result));
                XAssert.AreEqual(numProcesses, result.Processes.Count());
                XAssert.AreEqual(numProcessDetoursStatuses, result.DetouringStatuses.Count());
            }

            // 3rd run: with non-empty listener --> assert that the listener counts are the same as previously recorded ones
            {
                var myListener = new MyListener();
                myListener.SetMessageHandlingFlags(
                    MessageHandlingFlags.DebugMessageNotify |
                    MessageHandlingFlags.FileAccessNotify |
                    MessageHandlingFlags.ProcessDataNotify |
                    MessageHandlingFlags.ProcessDetoursStatusNotify);

                var result = await RunEchoProcess(myListener);
                
                XAssert.AreEqual(0, myListener.DebugMessageCount, string.Join(Environment.NewLine, myListener.DebugMessages));
                XAssert.AreEqual(numFilePathAccesses, CleanFileAccesses(myListener.FileAccesses).Count());
                XAssert.AreEqual(numProcesses, myListener.ProcessMessageCount);
                XAssert.AreEqual(numProcessDetoursStatuses, myListener.ProcessDetouringStatusMessageCount);
            }
        }

        public static IEnumerable<object[]> CmdExeLocationsData()
        {
            yield return new object[] { CmdHelper.CmdX64 };
            yield return new object[] { CmdHelper.CmdX86 };
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(CmdExeLocationsData))]
        public async Task Start(string cmdExeLocation)
        {
            var echoMessage = "Success";
            using (var tempFiles = new TempFileStorage(canGetFileNames: false))
            {
                var pt = new PathTable();
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, cmdExeLocation, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = "/d /c echo " + echoMessage,
                        WorkingDirectory = string.Empty,
                        DiagnosticsEnabled = true,
                    };

                var result = await RunProcess(info);
                await CheckEchoProcessResult(result, echoMessage);
            }
        }

        [Fact]
        public async Task StartKill()
        {
            var info = GetInfiniteWaitProcessInfo();
            using (ISandboxedProcess process = await StartProcessAsync(info))
            {
                // process is running in an infinite loop, let's kill it
                await process.KillAsync();
                SandboxedProcessResult result = await process.GetResultAsync();
                XAssert.IsTrue(result.Killed);
                XAssert.IsFalse(result.TimedOut, "Process claims it was timed out, but instead it was killed.");
            }
        }

        [Fact]
        public async Task StartTimeout()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            var info = GetInfiniteWaitProcessInfo();
            info.Timeout = new TimeSpan(1); // 1 tick == 100 nanoseconds

            // process is running in an infinite loop, but we have a timeout installed
            SandboxedProcessResult result = await RunProcess(info);
            XAssert.IsTrue(result.TimedOut);
            XAssert.IsTrue(result.Killed);
            XAssert.AreEqual(ExitCodes.Timeout, result.ExitCode);
        }

        [Fact]
        public async Task SuspensionExtendsTimeout()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            var info = GetInfiniteWaitProcessInfo();

            using (ISandboxedProcess process = await TryStartProcessAndSuspendImmediately(info, 100, multiplier: 2, retries: 5))
            {
                // If this fails a lot, consider changing the knobs above
                XAssert.IsNotNull(process, "Unable to start sandboxed process and suspend it immediately");

                // The process will time out next, but we will grant it more time while suspended
                await Task.Delay((int)(info.Timeout.Value.TotalMilliseconds * 4));

                // Kill it while suspended
                await process.KillAsync();

                SandboxedProcessResult result = await process.GetResultAsync();
                XAssert.IsTrue(result.Killed);
                XAssert.IsFalse(result.TimedOut, "Process claims it was timed out, but instead it was killed.");
            }
        }

        [Fact]
        public async Task SuspendResumeTimeout()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            var info = GetInfiniteWaitProcessInfo();

            using (ISandboxedProcess process = await TryStartProcessAndSuspendImmediately(info, 100, multiplier: 2, retries: 5))
            {
                // If this fails a lot, consider changing the knobs above
                XAssert.IsNotNull(process, "Unable to start sandboxed process and suspend it immediately");

                // The process will definitely time out next, but we will grant it more time
                await Task.Delay((int)(info.Timeout.Value.TotalMilliseconds * 4));

                // This will succeed because the timeout was extended
                var resumeSucceeded = process.TryResumeProcess();
                XAssert.IsTrue(resumeSucceeded);

                // Now we will definitely time out
                var result = await process.GetResultAsync();
                XAssert.IsTrue(result.TimedOut);
            }
        }

        private async Task<ISandboxedProcess> TryStartProcessAndSuspendImmediately(SandboxedProcessInfo info, double timeout, double multiplier = 2, int retries = 5)
        {
            // When starting the process with a timeout, there's a chance that we won't be able to suspend it before
            // the time is consumed and the process is killed. We try to do it a few times and return the "suspended"
            // process, or null if we failed every time.
            for (var i = 0; i < retries; i++)
            {
                info.Timeout = TimeSpan.FromMilliseconds(timeout);
                var process = await StartProcessAsync(info);
                var suspendedResult = process.TryEmptyWorkingSet(isSuspend: true);
                if (suspendedResult == EmptyWorkingSetResult.Success)
                {
                    return process;
                }

                // We failed, try again
                process.Dispose();
                timeout *= multiplier;
            }

            return null;
        }

        [Fact(Skip = "Test is flakey TFS 495531")]
        public async Task JobCounters()
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string tempFileName = tempFiles.GetUniqueFileName();

                var pt = new PathTable();
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, CmdHelper.CmdX86, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = "/d /c sleep 1s > " + CommandLineEscaping.EscapeAsCommandLineWord(tempFileName),
                    };

                info.FileAccessManifest.FailUnexpectedFileAccesses = false;

                SandboxedProcessResult result = await RunProcess(info);
                XAssert.IsFalse(result.Killed, "Process claims it or a child process was killed.");
                XAssert.AreEqual(1, result.ExitCode);
                XAssert.IsTrue(
                    result.JobAccountingInformation.HasValue,
                    "Job accounting info expected when the OS supports nested jobs");

                JobObject.AccountingInformation accounting = result.JobAccountingInformation.Value;

                if (result.PrimaryProcessTimes.TotalProcessorTime != TimeSpan.Zero)
                {
                    XAssert.AreNotEqual(
                        TimeSpan.Zero,
                        accounting.UserTime + accounting.KernelTime,
                        "Expected a non-zero user+kernel time.");
                }

                XAssert.AreNotEqual(0, accounting.MemoryCounters.PeakWorkingSetMb, "Expecting non-zero memory usage");
                XAssert.AreNotEqual(0, accounting.MemoryCounters.AverageCommitSizeMb, "Expecting non-zero memory usage");
                XAssert.AreNotEqual(0, accounting.MemoryCounters.PeakCommitSizeMb, "Expecting non-zero pagefile usage");
                XAssert.AreNotEqual(0, accounting.MemoryCounters.AverageCommitSizeMb, "Expecting non-zero pagefile usage");

                // Prior to Win10, cmd.exe launched within a job but its associated conhost.exe was exempt from the job.
                // That changed with Bug #633552
                // Unfortunately, the unit test runner isn't manifested to support Win10, so we can't easily check the
                // version.
                XAssert.IsTrue(
                    accounting.NumberOfProcesses == 1 || accounting.NumberOfProcesses == 2,
                    "Expected one main process and no child processes (prior to Win10), or one child conhost.exe on Win10");

                XAssert.AreNotEqual(
                    accounting.IO.WriteCounters.TransferCount >= 3,
                    "Expected at least three bytes written (echo XXX > file)");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Survivors(bool includeAllowedSurvivingChildren)
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            var testProcessName = TestProcessExecutable.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable);

            var info = ToProcessInfo(ToProcess(
                Operation.Spawn(Context.PathTable, waitToFinish: true, Operation.Echo("hi")),
                Operation.Spawn(Context.PathTable, waitToFinish: false, Operation.Block())));

            // We'll wait for at most 1 seconds for nested processes to terminate, as we know we'll have to wait by design of the test
            // We'll wait longer to test allowed surviving children.
            info.NestedProcessTerminationTimeout = includeAllowedSurvivingChildren ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);

            if (includeAllowedSurvivingChildren)
            {
                info.AllowedSurvivingChildProcessNames = new[]
                {
                    testProcessName,
                    Path.GetFileName(CmdHelper.Conhost)
                };
            }

            // Use wall-clock time.
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = await RunProcess(info);

            sw.Stop();

            XAssert.AreEqual(0, result.ExitCode);
            XAssert.IsNotNull(result.SurvivingChildProcesses);

            ToFileNames(result.SurvivingChildProcesses, out var survivorProcessNames, out var survivorNamesJoined);
            ToFileNames(result.Processes, out var reportedProcessNames, out var reportedNamesJoined);

            if (includeAllowedSurvivingChildren)
            {
                // Note that one of the spawned process is blocked indefinitely.
                // However, when surviving children are included, then that process should be killed right-away without waiting.
                if (sw.ElapsedMilliseconds >= info.NestedProcessTerminationTimeout.TotalMilliseconds)
                {
                    // If process is not killed, then there are surviving children that are not allowed.
                    XAssert.IsTrue(
                        survivorProcessNames.IsProperSupersetOf(info.AllowedSurvivingChildProcessNames),
                        "Survivors: {0}, Allowed survivors: {1}",
                        survivorNamesJoined,
                        string.Join(" ; ", info.AllowedSurvivingChildProcessNames));
                }
            }

            // TestProcess must have survived
            XAssert.Contains(survivorProcessNames, testProcessName);

            // conhost.exe may also have been alive. Win10 changed conhost to longer be excluded from job objects.
            var conhostName = Path.GetFileName(CmdHelper.Conhost);
            if (survivorProcessNames.Contains(conhostName))
            {
                // With new Win10, there can be multiple surviving conhost.
                XAssert.IsTrue(
                    result.SurvivingChildProcesses.Count() >= 2,
                    "Unexpected survivors (cmd and conhost were present, but extras were as well): {0}",
                    survivorNamesJoined);
            }
            else
            {
                XAssert.AreEqual(
                    1,
                    result.SurvivingChildProcesses.Count(),
                    "Unexpected survivors: {0}",
                    string.Join(", ", survivorProcessNames.Except(new[] { testProcessName })));
            }

            // We ignore the Conhost process when checking if all survivors got reported, as Conhost seems very special
            foreach (var survivor in survivorProcessNames.Except(new[] { conhostName }))
            {
                XAssert.Contains(reportedProcessNames, survivor);
            }

            void ToFileNames(IEnumerable<ReportedProcess> processes, out HashSet<string> set, out string joined)
            {
                set = new HashSet<string>(processes.Select(p => p.Path).Select(Path.GetFileName), OperatingSystemHelper.PathComparer);
                joined = string.Join(" ; ", set);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // same as Survivors, but using cmd.exe
        [MemberData(nameof(CmdExeLocationsData))]
        public async Task SurvivorsHaveCommandLines(string cmdExeLocation)
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            using (var tempFiles = new TempFileStorage(canGetFileNames: false))
            {
                var pt = new PathTable();
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, cmdExeLocation, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = "/d /c start /B FOR /L %i IN (0,0,1) DO @rem",

                        // launches another cmd.exe that runs how you do an infinite loop with cmd.exe
                        // we'll wait for at most 1 seconds for nested processes to terminate, as we know we'll have to wait by design of the test
                        NestedProcessTerminationTimeout = TimeSpan.FromSeconds(1)
                    };
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
                var result = await RunProcess(info);

                // after we detect surviving child processes, we kill them, so this test won't leave around zombie cmd.exe processes
                XAssert.IsNotNull(result.SurvivingChildProcesses);
                XAssert.AreNotEqual(0, result.SurvivingChildProcesses.Count());

                foreach (var survivorProcess in result.SurvivingChildProcesses)
                {
                    XAssert.IsTrue(!string.IsNullOrEmpty(survivorProcess.ProcessArgs), "Reported process did not have a command line: {0}", survivorProcess.Path);
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task QuickSurvivors(bool waitToFinish)
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            const string echoMessage = "Hello from a child process";
            var spawnOperation = Operation.Spawn(Context.PathTable, waitToFinish, Operation.Echo(echoMessage));
            var info = ToProcessInfo(ToProcess(spawnOperation));
            info.DiagnosticsEnabled = true;
            var result = await RunProcess(info);

            // no survivors
            XAssert.IsNull(result.SurvivingChildProcesses);

            // at least two reported processes (conhost may be there as well on Windows)
            XAssert.IsNotNull(result.Processes);
            XAssert.IsTrue(result.Processes.Count >= 2, "Expected to see at least 2 processes, got {0}", result.Processes.Count);

            // only if waitToFinish is true the console output will contain the echoed message from the child process
            if (waitToFinish)
            {
                await CheckEchoProcessResult(result, echoMessage);
            }
        }

        [Fact]
        public async Task StandardError()
        {
            const string errorMessage = "Error";
            var info = GetEchoProcessInfo(errorMessage, useStdErr: true);
            SandboxedProcessResult result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode);
            XAssert.AreEqual("Error", (await result.StandardError.ReadValueAsync()).Trim());
        }

        [Fact]
        public async Task StandardOutputToFile()
        {
            const string Expected = "Success";
            var info = GetEchoProcessInfo(Expected);
            var result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode);
            await result.StandardOutput.SaveAsync();
            XAssert.AreEqual(Expected, File.ReadAllText(result.StandardOutput.FileName).Trim());
            XAssert.AreEqual(Expected, (await result.StandardOutput.ReadValueAsync()).Trim());
        }

        [Fact]
        public async Task NoStandardInputTerminates()
        {
            // Regression test for Bug 51148: BuildXL used to stop responding if process requires console input, but none was supplied.
            var info = ToProcessInfo(ToProcess(Operation.ReadStdIn()));
            var result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode);
        }

        // The following test tests only that enabling and disabling the ConHost sharing is not crashing.
        // There is no validation of process hierarchy.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UseDisabledAndEnabledConHostSharing(bool disableConHostSharing)
        {
            var echoMessage = "hi";
            var info = ToProcessInfo(EchoProcess(echoMessage), disableConHostSharing: disableConHostSharing);
            info.DiagnosticsEnabled = true;
            SandboxedProcessResult result = await RunProcess(info);
            await CheckEchoProcessResult(result, echoMessage);
        }

        [Fact]
        public async Task StandardInput()
        {
            string[] inputLines = new[] { "0", "2", "42", "1" };
            string input = string.Join(Environment.NewLine, inputLines);
            using (var reader = new StringReader(input))
            {
                var info = ToProcessInfo(ToProcess(Operation.ReadStdIn()));
                info.StandardInputReader = reader;
                var result = await RunProcess(info);

                XAssert.AreEqual(0, result.ExitCode);
                string stdout = await result.StandardOutput.ReadValueAsync();
                XAssert.ArrayEqual(inputLines, ToLines(stdout.Trim(new char[] { '\uFEFF', '\u200B' })));
            }
        }

        [Fact]
        public async Task UnicodeArguments()
        {
            var outFile = CreateOutputFileArtifact(prefix: "\u2605");
            var content = "∂ßœ∑∫µπø";
            var info = ToProcessInfo(ToProcess(Operation.WriteFile(outFile, content)));
            var result = await RunProcess(info);

            XAssert.AreEqual(0, result.ExitCode);
            var outFilePath = outFile.Path.ToString(Context.PathTable);
            XAssert.AreEqual(content, File.ReadAllText(outFilePath).Trim());
        }

        [Fact]
        public async Task LargeStandardOutputToFile()
        {
            var s = new string('S', 100);
            var info = ToProcessInfo(ToProcess(
                Operation.Echo(s),
                Operation.Echo(s)));
            info.MaxLengthInMemory = s.Length - 1;
            info.DiagnosticsEnabled = true;
            var result = await RunProcess(info);

            XAssert.AreEqual(0, result.ExitCode);
            XAssert.IsFalse(result.Killed, $"Process claims that it was killed. Diagnostic information: \n {result.DiagnosticMessage}");
            XAssert.IsFalse(result.StandardOutput.HasException);
            XAssert.IsTrue(result.StandardOutput.HasLength);
            XAssert.AreEqual(result.StandardOutput.Length, (s.Length + Environment.NewLine.Length) * 2);

            await result.StandardOutput.SaveAsync();
            var expectedLines = new[] { s, s };

            string fileName = result.StandardOutput.FileName;
            XAssert.ArrayEqual(expectedLines, File.ReadAllLines(fileName));
            string stdout = await result.StandardOutput.ReadValueAsync();
            XAssert.AreEqual(expectedLines, ToLines(stdout));
        }

        [Fact]
        public async Task ExitCode()
        {
            var exitCode = 42;
            var info = ToProcessInfo(ToProcess(Operation.Fail(exitCode)));
            var result = await RunProcess(info);
            XAssert.AreEqual(exitCode, result.ExitCode);
        }

        [Fact]
        public async Task EnvironmentVariables()
        {
            var envVarName = "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty);
            var envVarValue = "Success";
            var info = ToProcessInfo(
                ToProcess(Operation.ReadEnvVar(envVarName)),
                overrideEnvVars: new Dictionary<string, string>
                {
                    [envVarName] = envVarValue
                });

            info.DiagnosticsEnabled = true;
            SandboxedProcessResult result = await RunProcess(info);
            await CheckEchoProcessResult(result, echoMessage: envVarValue);
        }

        [Fact]
        public async Task WorkingDirectory()
        {
            var workingDir = CreateUniqueDirectory(prefix: "pwd-test").ToString(Context.PathTable);
            var info = ToProcessInfo(ToProcess(Operation.EchoCurrentDirectory()));
            info.WorkingDirectory = workingDir;

            var result = await RunProcess(info);
            var stdout = await result.StandardOutput.ReadValueAsync();
            XAssert.AreEqual(0, result.ExitCode);
            XAssert.AreEqual(workingDir, stdout.Trim());
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessId()
        {
            var wmic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wbem", "wmic.exe");
            var find = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "find.exe");

            using (var tempFiles = new TempFileStorage(canGetFileNames: false))
            {
                var pt = new PathTable();
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, CmdHelper.CmdX64, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = $"/d /c {wmic} process get parentprocessid,name|{find} \"WMIC\"",
                    };
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
                using (ISandboxedProcess process = await StartProcessAsync(info))
                {
                    SandboxedProcessResult result = await process.GetResultAsync();
                    string output = (await result.StandardOutput.ReadValueAsync()).Trim();

                    // there can be multiple instance of WMIC running concurrently,
                    // so we can only check that one of them has this process as the parent
                    var possibleProcessIds = new List<int>();
                    foreach (string s in output.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                    {
                        string t = s.StartsWith("WMIC.exe", StringComparison.Ordinal) ? s.Substring("WMIC.exe".Length).Trim() : s;

                        int i;
                        if (int.TryParse(t, out i))
                        {
                            possibleProcessIds.Add(i);
                        }
                        else
                        {
                            XAssert.Fail("Not an integer: {0}", t);
                        }
                    }

                    XAssert.IsTrue(possibleProcessIds.Contains(process.ProcessId));
                }
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportNoBuildExeTraceLog()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string matchingFileName = "_buildc_dep_out.pass1";
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, matchingFileName);
                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportNoNul()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string nulFileName = "NUL";
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, nulFileName);
                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportNoNulColon()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string nulFileName = "NUL:";
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, nulFileName);
                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportNoFolderNul()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                string nulFileName = Path.Combine(windowsDirectory, "nul");
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, nulFileName);
                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportNoDriveNul()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                string nulFileName = windowsDirectory[0] + ":nul";
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, nulFileName);
                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count);
            }
        }

        private SandboxedProcessInfo CreateCmdSandboxedProcessInfo(PathTable pt, TempFileStorage tempFiles, string fileName)
        {
            // Use unsafe probe by default because this test probes parent directories that can be directory symlinks or junctions.
            // E.g., 'd:\dbs\el\bxlint\Out' with [C:\Windows\system32\cmd.exe:52040](Probe) FindFirstFileEx(...)
            return new SandboxedProcessInfo(
                pt,
                tempFiles,
                CmdHelper.CmdX64,
                loggingContext: LoggingContext,
                disableConHostSharing: false,
                fileAccessManifest: new FileAccessManifest(pt) { ProbeDirectorySymlinkAsDirectory = true })
            {
                PipSemiStableHash = 0,
                PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                Arguments = "/d /c echo >" + CommandLineEscaping.EscapeAsCommandLineWord(fileName),
            };
        }

        private static void AddCmdDependencies(PathTable pt, SandboxedProcessInfo info)
        {
            foreach (AbsolutePath path in CmdHelper.GetCmdDependencies(pt))
            {
                info.FileAccessManifest.AddPath(path, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);
            }

            foreach (AbsolutePath path in CmdHelper.GetCmdDependencyScopes(pt))
            {
                info.FileAccessManifest.AddScope(path, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowRead);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task ReportSingleAccess(bool expectUsn, bool reportUsn)
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pt = new PathTable();
                string tempFileName = tempFiles.GetUniqueFileName();
                File.WriteAllText(tempFileName, "Success");
                Usn usn;
                using (FileStream fs = File.OpenRead(tempFileName))
                {
                    MiniUsnRecord? maybeUsn = FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle);
                    XAssert.IsTrue(maybeUsn.HasValue, "USN journal is either disabled or not supported by the volume");
                    usn = maybeUsn.Value.Usn;
                }

                AbsolutePath tempFilePath = AbsolutePath.Create(pt, tempFileName);
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, CmdHelper.CmdX64, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = "/d /c type " + CommandLineEscaping.EscapeAsCommandLineWord(tempFileName),
                    };
                info.FileAccessManifest.ReportFileAccesses = true; // We expect all accesses reported (we use result.FileAccesses below).
                info.FileAccessManifest.AddPath(
                    tempFilePath,
                    values: FileAccessPolicy.AllowRead | (reportUsn ? FileAccessPolicy.ReportUsnAfterOpen : 0),
                    mask: FileAccessPolicy.MaskNothing,
                    expectedUsn: expectUsn ? usn : ReportedFileAccess.NoUsn);

                // We explicitly do not set ReportAccess (testing catchall reporting)
                SandboxedProcessResult result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual("Success", (await result.StandardOutput.ReadValueAsync()).Trim());
                XAssert.AreEqual(string.Empty, (await result.StandardError.ReadValueAsync()).Trim());
                XAssert.IsNotNull(result.FileAccesses);
                ReportedProcess reportedProcess = result.FileAccesses.FirstOrDefault().Process;
                ReportedFileAccess rfa = ReportedFileAccess.Create(
                    ReportedFileOperation.CreateFile,
                    reportedProcess,
                    RequestedAccess.Read,
                    FileAccessStatus.Allowed,
                    reportUsn,

                    // Explicit flag
                    0,
                    (reportUsn || expectUsn) ? usn : ReportedFileAccess.NoUsn,
                    DesiredAccess.GENERIC_READ,
                    ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                    CreationDisposition.CREATE_NEW | CreationDisposition.CREATE_ALWAYS,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    tempFilePath);

                AssertReportedAccessesContains(pt, result.FileAccesses, rfa);
            }
        }

        [Fact]
        public async Task ReportSingleReadAccessXPlat()
        {
            var srcFile = CreateSourceFile();

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true; // We explicitly do not set ReportAccess (testing catchall reporting)
            fam.FailUnexpectedFileAccesses = false;
            fam.AddPath(srcFile.Path, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);

            var info = ToProcessInfo(
                ToProcess(Operation.ReadFile(srcFile)),
                fileAccessManifest: fam);

            var result = await RunProcess(info);

            XAssert.AreEqual(0, result.ExitCode);
            XAssert.IsNotNull(result.FileAccesses);

            AssertReportedAccessesContains(
                Context.PathTable,
                result.FileAccesses,
                GetFileAccessForReadFileOperation(
                    srcFile.Path,
                    result.FileAccesses.FirstOrDefault().Process,
                    denied: false,
                    explicitlyReported: false));
        }

        [Fact]
        public async Task ReportSingleUnexpectedAccess()
        {
            var srcFile = CreateSourceFile();

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = false;
            fam.FailUnexpectedFileAccesses = false;
            fam.AddPath(srcFile.Path, values: FileAccessPolicy.Deny | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing);

            var info = ToProcessInfo(
                ToProcess(Operation.ReadFile(srcFile)),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode); // Only because FailUnexpectedFileAccesses = false
            XAssert.IsNull(result.FileAccesses); // We aren't reporting file accesses (just violations)
            XAssert.IsNotNull(result.AllUnexpectedFileAccesses);
            XAssert.IsTrue(result.AllUnexpectedFileAccesses.Count > 0);

            AssertReportedAccessesContains(
                Context.PathTable,
                result.AllUnexpectedFileAccesses,
                GetFileAccessForReadFileOperation(
                    srcFile.Path,
                    result.AllUnexpectedFileAccesses.FirstOrDefault().Process,
                    denied: true,
                    explicitlyReported: true));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ReportSingleUnexpectedUsnAccess()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pt = new PathTable();
                string tempFileName = tempFiles.GetUniqueFileName();
                File.WriteAllText(tempFileName, "Success");
                Usn usn;
                using (FileStream fs = File.OpenRead(tempFileName))
                {
                    MiniUsnRecord? maybeUsn = FileUtilities.ReadFileUsnByHandle(fs.SafeFileHandle);
                    XAssert.IsTrue(maybeUsn.HasValue, "USN journal is either disabled or not supported by the volume");
                    usn = maybeUsn.Value.Usn;
                }

                AbsolutePath tempFilePath = AbsolutePath.Create(pt, tempFileName);
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, CmdHelper.CmdX64, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = "/d /c type " + CommandLineEscaping.EscapeAsCommandLineWord(tempFileName),
                    };
                info.FileAccessManifest.ReportFileAccesses = false;
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
                info.FileAccessManifest.AddPath(tempFilePath, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing, expectedUsn: new Usn(usn.Value - 1));

                SandboxedProcessResult result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode); // Only because FailUnexpectedFileAccesses = false
                XAssert.AreEqual(string.Empty, (await result.StandardError.ReadValueAsync()).Trim());
                XAssert.IsNull(result.FileAccesses); // We aren't reporting file accesses (just violations)
                ReportedProcess reportedProcess = result.AllUnexpectedFileAccesses.FirstOrDefault().Process;

                ReportedFileAccess rfa = ReportedFileAccess.Create(
                    ReportedFileOperation.CreateFile,
                    reportedProcess,
                    RequestedAccess.Read,
                    FileAccessStatus.Allowed,
                    true,
                    0,
                    usn,
                    DesiredAccess.GENERIC_READ,
                    ShareMode.FILE_SHARE_READ | ShareMode.FILE_SHARE_WRITE,
                    CreationDisposition.CREATE_NEW | CreationDisposition.CREATE_ALWAYS,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    tempFilePath);

                AssertReportedAccessesContains(pt, result.ExplicitlyReportedFileAccesses, rfa);
            }
        }

        [Fact]
        public async Task ReportGeneralUnexpectedAccess()
        {
            var srcFile = CreateSourceFile();

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.ReportUnexpectedFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;

            var info = ToProcessInfo(
                ToProcess(Operation.ReadFile(srcFile)),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode);
            XAssert.IsNotNull(result.FileAccesses);
            XAssert.IsNotNull(result.AllUnexpectedFileAccesses);

            ReportedFileAccess rfa = GetFileAccessForReadFileOperation(
                srcFile.Path,
                result.FileAccesses.First().Process,
                denied: true,
                explicitlyReported: false);

            AssertReportedAccessesContains(Context.PathTable, result.FileAccesses, rfa);
            AssertReportedAccessesContains(Context.PathTable, result.AllUnexpectedFileAccesses, rfa);
        }

        [Fact]
        public async Task ReportGeneralUnexpectedAccessWithRequestedWriteAccess()
        {
            var outFile = CreateOutputFileArtifact();

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.ReportUnexpectedFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;

            var info = ToProcessInfo(
                ToProcess(Operation.WriteFile(outFile, doNotInfer: true)),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
            XAssert.AreEqual(0, result.ExitCode);
            XAssert.IsNotNull(result.FileAccesses);

            ReportedFileAccess rfa = GetFileAccessForWriteFileOperation(
                outFile.Path,
                result.FileAccesses.FirstOrDefault().Process,
                denied: true,
                explicitlyReported: false);
            AssertReportedAccessesContains(Context.PathTable, result.FileAccesses, rfa);
            AssertReportedAccessesContains(Context.PathTable, result.AllUnexpectedFileAccesses, rfa);
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public async Task OperationErrorsAreReportedOnLinux()
        {
            var existentFile = CreateSourceFile();

            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = false;
            fam.FailUnexpectedFileAccesses = false;

            // Pick a random operation (create directory) and make sure we perform it on an 
            // existing file
            var info = ToProcessInfo(
                ToProcess(
                    Operation.CreateDir(existentFile)
                ),
                fileAccessManifest: fam);

            var result = await RunProcess(info);

            // We should get a report where we see the creation attemp that results in a file exists error
            result.AllUnexpectedFileAccesses.Single(fa => 
                fa.Operation == ReportedFileOperation.KAuthCreateDir && 
                fa.Error == (uint) global::BuildXL.Interop.Unix.IO.Errno.EEXIST);

            // Now perform the same operation but in a way it should succeed
            info = ToProcessInfo(
                ToProcess(
                    Operation.CreateDir(FileArtifact.CreateSourceFile(CreateUniqueSourcePath()))
                ),
                fileAccessManifest: fam);

            result = await RunProcess(info);

            // We should get a report where we see the creation attemp with a successful error code
            result.AllUnexpectedFileAccesses.Single(fa => 
                fa.Operation == ReportedFileOperation.KAuthCreateDir && 
                fa.Error == 0);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task IgnoreInvalidPathRead()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pt = new PathTable();
                string tempDirName = tempFiles.RootDirectory;

                AbsolutePath tempDirPath = AbsolutePath.Create(pt, tempDirName);
                var info =
                    new SandboxedProcessInfo(pt, tempFiles, CmdHelper.CmdX64, disableConHostSharing: false, loggingContext: LoggingContext)
                    {
                        // Adding \|Bad bit| to the end should result in ERROR_INVALID_NAME. The ^ hats are for cmd escaping. Note that type eats quotes, so we can't try those.
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments =
                            "/d /c type " + CommandLineEscaping.EscapeAsCommandLineWord(tempDirName) +
                            "\\^|Bad bit^| 2>nul || (echo Nope & exit /b 0)",
                    };

                info.FileAccessManifest.AddScope(
                    AbsolutePath.Invalid,
                    FileAccessPolicy.MaskNothing,
                    FileAccessPolicy.AllowAll); // Ignore everything outside of the temp directory
                info.FileAccessManifest.AddScope(
                    tempDirPath,
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportAccess);

                // A real access would be reported, but a bad path should be ignored.
                var result = await RunProcess(info);

                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual("Nope", (await result.StandardOutput.ReadValueAsync()).Trim());
                XAssert.AreEqual(string.Empty, (await result.StandardError.ReadValueAsync()).Trim());

                AssertReportedAccessesIsEmpty(pt, result.AllUnexpectedFileAccesses);

                // Note we filter this set, since cmd likes to probe all over the place.
                AssertReportedAccessesIsEmpty(pt, result.ExplicitlyReportedFileAccesses.Where(access => access.GetPath(pt).Contains("Bad")));
            }
        }

        [Fact]
        public async Task StartFileDoesNotExist()
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                // On Linux we don't require that the process exists ahead of time.  Instead,
                // we run a shell (e.g., /bin/sh) and then exec that process from the shell.
                // The actual process executable may be a path relative to a virtual filesystem
                // root in which the process will be executing, so the executable need not exist
                // now and it can still be fetched dynamically (by the virtual file system) once
                // the shell starts executing.
                return;
            }

            var pt = new PathTable();
            var info = new SandboxedProcessInfo(pt, this, "DoesNotExistIHope", disableConHostSharing: false, sandboxConnection: GetSandboxConnection(), loggingContext: LoggingContext)
            {
                PipSemiStableHash = 0,
                PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
            };
            info.FileAccessManifest.PipId = 1;

            try
            {
                await StartProcessAsync(info);
            }
            catch (BuildXLException ex)
            {
                string logMessage = ex.LogEventMessage;
                int errorCode = ex.LogEventErrorCode;
                XAssert.IsTrue(logMessage.Contains("Process creation failed"), "Missing substring in {0}", logMessage);
                XAssert.IsTrue(errorCode == 0x2 || errorCode == 0x3, "Expected ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND: {0}", errorCode);
                return;
            }

            XAssert.Fail("Expected BuildXLException due to process creation failure");
        }

        [Fact]
        public async Task TempAccessesAreUnexpectedByDefault()
        {
            var outFile = CreateOutputFileArtifact(root: TemporaryDirectory, prefix: "not.allowed");
            var info = ToProcessInfo(ToProcess(Operation.WriteFile(outFile)));
            var result = await RunProcess(info);
            XAssert.IsNotNull(result.AllUnexpectedFileAccesses);
            XAssert.IsTrue(
                result.AllUnexpectedFileAccesses.Any(a => a.GetPath(Context.PathTable) == outFile.Path.ToString(Context.PathTable)),
                "Expected to see unexpected file access into restricted temp folder ({0}), got: {1}",
                outFile.Path.ToString(Context.PathTable),
                string.Join(Environment.NewLine, result.AllUnexpectedFileAccesses.Select(p => p.GetPath(Context.PathTable))));
        }

        /// <summary>
        /// Tests that FileAccessManifest.ReportProcessArgs option controls whether the
        /// command line arguments of all launched processes will be captured and reported
        /// </summary>
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // Sandbox Kext on macOS doesn't capture process cmdline
        [InlineData(true)]
        [InlineData(false)]
        public async Task ReportProcessCommandLineArgs(bool reportProcessArgs)
        {
            const string echoMessage = "test";

            var echoOp = Operation.Echo(echoMessage);
            var info = GetEchoProcessInfo(echoMessage);
            info.FileAccessManifest.ReportProcessArgs = reportProcessArgs;
            info.DiagnosticsEnabled = true;

            var result = await RunProcess(info);
            await CheckEchoProcessResult(result, echoMessage);

            // validate the results: we are expecting 1 process with command line args
            var launchedProcesses = ExcludeInjectedOnes(result.Processes).ToList();
            XAssert.AreEqual(1, launchedProcesses.Count, "The number of processes launched is not correct");
            var expectedReportedArgs = reportProcessArgs
                ? TestProcessExecutable.Path.ToString(Context.PathTable) + " " + echoOp.ToCommandLine(Context.PathTable)
                : string.Empty;
            XAssert.AreEqual(
                expectedReportedArgs,
                launchedProcesses[0].ProcessArgs,
                "The captured processes arguments are incorrect");
        }

        [Theory]
        [InlineData("$hi")]
        [InlineData("${hi}")]
        [InlineData("$(hi)")]
        [InlineData("`hi`")]
        [InlineData("` hi `")]
        [InlineData("\"hi\"")]
        [InlineData("\" hi \"")]
        [InlineData("'hi'")]
        [InlineData("' hi '")]
        [InlineData("# hi")]
        [InlineData("#hi")]
        public async Task TestCmdLineArgEscaping(string echoMessage)
        {
            var info = GetEchoProcessInfo(echoMessage);
            var result = await RunProcess(info);
            await CheckEchoProcessResult(result, echoMessage);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public async Task TrackVForkAsync()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string tempFileName = tempFiles.GetUniqueFileName();
                var pt = new PathTable();
                var info =
                    // 'time' uses vfork on macOS
                    new SandboxedProcessInfo(pt, tempFiles, "/usr/bin/time", disableConHostSharing: false, sandboxConnection: GetSandboxConnection(), loggingContext: LoggingContext)
                    {
                        PipSemiStableHash = 0,
                        PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                        Arguments = $"/usr/bin/touch {tempFileName}",
                    };
                info.FileAccessManifest.PipId = GetNextPipId();
                info.FileAccessManifest.ReportFileAccesses = true;
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;

                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.IsNotNull(result.FileAccesses);

                // Restrict reports to those of type 'Process' and 'ProcessExit', then
                // create a mapping from operation type to reported paths for that operation
                var ProcessOperations = new[] { ReportedFileOperation.Process, ReportedFileOperation.ProcessExit };
                Dictionary<ReportedFileOperation, HashSet<string>> op2paths = result
                    .FileAccesses
                    .Where(report => ProcessOperations.Contains(report.Operation))
                    .GroupBy(report => report.Operation)
                    .ToDictionary(
                        grp => grp.Key,
                        grp => grp
                            .Select(report => report.GetPath(pt))
                            .Select(Path.GetFileName)
                            .ToHashSet());

                // assert both 'Process' and 'ProcessExit' operations have been reported
                XAssert.Contains(op2paths.Keys, ReportedFileOperation.Process, ReportedFileOperation.ProcessExit);

                // assert both 'time' and 'touch' processes have been reported
                XAssert.Contains(op2paths[ReportedFileOperation.Process], "time", "touch");
                XAssert.Contains(op2paths[ReportedFileOperation.ProcessExit], "time", "touch");

                // assert that all accesses to 'tempFileName' were done by the 'touch' process
                var accessesToTempFile = result
                    .FileAccesses
                    .Where(report => report.GetPath(pt) == tempFileName)
                    .Select(report => report.Process.Path)
                    .Select(Path.GetFileName)
                    .Distinct()
                    .ToList();
                XAssert.SetEqual(accessesToTempFile, new[] { "touch" });
            }
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public async Task ForkedProcessIsReportedWithRightPid()
        {
            var fam = new FileAccessManifest(Context.PathTable);
            fam.ReportFileAccesses = true;
            fam.FailUnexpectedFileAccesses = false;

            var info = ToProcessInfo(
                ToProcess(new Operation[]
                    {
                        Operation.Spawn(Context.PathTable, waitToFinish: true, Operation.WriteFile(CreateOutputFileArtifact())),
                        Operation.WriteFile(CreateOutputFileArtifact()),
                    }),
                fileAccessManifest: fam);

            var result = await RunProcess(info);
            
            // Retrieve the pids of the fork process reports
            var processReportsPids = result.FileAccesses.Where(fa =>
                fa.Operation == ReportedFileOperation.Process).Select(fa => fa.Process.ProcessId).Distinct();

            // We should see two different pids (root and spawned processes)
            XAssert.AreEqual(2, processReportsPids.Count());
        }

        private void AssertReportedAccessesIsEmpty(PathTable pathTable, IEnumerable<ReportedFileAccess> result)
        {
            if (result == null || !result.Any())
            {
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Expected an empty set of reported accesses.");

            message.AppendLine("These were actually reported:");
            foreach (ReportedFileAccess actual in result)
            {
                message.AppendFormat("\t{0} : {1}\n", actual.GetPath(pathTable), actual.Describe());
            }

            XAssert.Fail(message.ToString());
        }

        private void AssertReportedAccessesContains(PathTable pathTable, ISet<ReportedFileAccess> result, ReportedFileAccess rfa)
        {
            IEqualityComparer<ReportedFileAccess> comparer = OperatingSystemHelper.IsUnixOS
                ? new RelaxedReportedFileAccessComparer(Context.PathTable)
                : EqualityComparer<ReportedFileAccess>.Default;
            if (result.Contains(rfa, comparer))
            {
                return;
            }

            var rfaPath = rfa.GetPath(pathTable);
            var rfaDescribe = rfa.Describe();
            var message = new StringBuilder();
            message.Append($"Expected the following access: {rfaPath}: {rfaDescribe}{Environment.NewLine}");
            message.Append($"These were actually reported (same manifest path):{Environment.NewLine}");
            foreach (ReportedFileAccess actual in result)
            {
                var actualPath = actual.GetPath(pathTable);
                var actualDescribe = actual.Describe();
                if (actualPath == rfaPath)
                {
                    if (actualDescribe == rfaDescribe)
                    {
                        return;
                    }

                    message.Append($"\t{actualPath}: {actualDescribe}{Environment.NewLine}");
                }
            }

            message.AppendLine("(others; different manifest path):");
            foreach (ReportedFileAccess actual in result)
            {
                var actualPath = actual.GetPath(pathTable);
                if (actualPath != rfaPath)
                {
                    message.Append($"\t{actualPath}: {actual.Describe()}{Environment.NewLine}");
                }
            }

            XAssert.Fail(message.ToString());
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CheckPreloadedDll()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                string nulFileName = Path.Combine(windowsDirectory, "nul");
                var pt = new PathTable();
                var info = CreateCmdSandboxedProcessInfo(pt, tempFiles, nulFileName);
                info.FileAccessManifest.IgnorePreloadedDlls = false;

                AddCmdDependencies(pt, info);
                var result = await RunProcess(info);
                XAssert.AreEqual(0, result.ExitCode);
                XAssert.AreEqual(0, result.AllUnexpectedFileAccesses.Count); // In our tests we use the shared conhost, so for this test nothing extra was loaded in the reused test.
            }
        }

        [Theory(Skip = "Flaky test. Work item - 1950089")]
        [MemberData(nameof(TruthTable.GetTable), 3, MemberType = typeof(TruthTable))]
        public async Task HandleHardNativeCrash(bool shouldParentCrash, bool shouldChildCrash, bool waitForChildToFinish)
        {
            var outFile = CreateOutputFileArtifact();
            var info = ToProcessInfo(ToProcess(
                Operation.WriteFile(outFile),
                Operation.Spawn(Context.PathTable, waitToFinish: waitForChildToFinish,
                    shouldChildCrash ? Operation.CrashHardNative() : Operation.Echo("Child :: not crashing")),
                shouldParentCrash
                    ? Operation.CrashHardNative()
                    : Operation.Echo("Parent :: not crashing")));

            var result = await RunProcess(info);
            var msg =
                $"Code: {result.ExitCode}, Killed: {result.Killed}, Num Survivors: {result.SurvivingChildProcesses?.Count()}, Stdout:{Environment.NewLine}" +
                await result.StandardOutput.ReadValueAsync();
            TestOutput.WriteLine(msg);

            XAssert.IsFalse(result.Killed);
            XAssert.IsNull(result.SurvivingChildProcesses);
            if (shouldParentCrash)
            {
                XAssert.AreNotEqual(0, result.ExitCode);
            }
            else
            {
                XAssert.AreEqual(0, result.ExitCode);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("LD_PRELOAD=")]
        // CODESYNC:  bxl_observer.hpp
        [InlineData("__BUILDXL_FAM_PATH=")]
        [InlineData("__BUILDXL_DETOURS_PATH=")]
        [InlineData("__BUILDXL_ROOT_PID=")]
        [InlineData("LD_PRELOAD=\0__BUILDXL_FAM_PATH=\0__BUILDXL_DETOURS_PATH=\0__BUILDXL_ROOT_PID=")]
        public async Task TestChildProcessWasDetouredLibAsync(string envVarToReset)
        {
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                return;
            }

            var parentInput = CreateSourceFile();
            var childInput = CreateSourceFile();
            var grandChildInput = CreateSourceFile();

            var info = ToProcessInfo(ToProcess(
                new Operation[]
                {
                    Operation.ReadFile(parentInput),
                    Operation.SpawnWithEnvs(
                        Context.PathTable,
                        true,
                        new Operation[]
                        {
                            Operation.ReadFile(childInput),
                            Operation.Spawn(
                                Context.PathTable,
                                true,
                                new Operation[]
                                {
                                    Operation.ReadFile(grandChildInput)
                                })
                        },
                        envVarToReset)
                }));
            info.FileAccessManifest.ReportFileAccesses = true;

            var result = await RunProcess(info);

            XAssert.AreEqual(0, result.ExitCode);

            var observedAccesses = result.FileAccesses
                .Select(reportedAccess => AbsolutePath.TryCreate(Context.PathTable, reportedAccess.GetPath(Context.PathTable), out AbsolutePath result) ? result : AbsolutePath.Invalid)
                .ToArray();

            XAssert.AreEqual(3, result.Processes.Count, "The parent and child process should have been detoured.");

            // We should see the access that happens on the main test process
            XAssert.IsTrue(observedAccesses.Contains(parentInput.Path), "Input file of parent process should have been observed");
            // We should see the access that happens on the spawned child process
            XAssert.IsTrue(observedAccesses.Contains(childInput.Path), "Input file of child process should have been observed");
            // We should see the access that happens on the spawned grandchild process
            XAssert.IsTrue(observedAccesses.Contains(grandChildInput.Path), "Input file of grandchild process should have been observed");
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task VerifyOpenedFileOrDirectoryAttributesWithFiles()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = new PathTable();
                var tempRootDir = AbsolutePath.Create(pathTable, tempFiles.RootDirectory);
                var fileToBeCreated = tempFiles.GetUniqueFileName();
                var fileToBeDeleted = tempFiles.GetUniqueFileName();
                var arguments = $"/d /c echo test > {fileToBeCreated} & del {fileToBeDeleted}";

                File.WriteAllText(fileToBeDeleted, "delete");

                await ExecuteAndAssertOpenedFileOrDirectoryAttributeTest(isDirectory: false, tempRootDir, arguments, pathTable, tempFiles);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task VerifyOpenedFileOrDirectoryAttributesWithDirectories()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var pathTable = new PathTable();
                var tempRootDir = AbsolutePath.Create(pathTable, tempFiles.RootDirectory);
                var directoryToBeCreated = tempFiles.GetUniqueDirectoryWithoutCreate();
                var directoryToBeDeleted = tempFiles.GetUniqueDirectory();
                var arguments = $"/d /c mkdir {directoryToBeCreated} & rmdir {directoryToBeDeleted}";

                await ExecuteAndAssertOpenedFileOrDirectoryAttributeTest(isDirectory: true, tempRootDir, arguments, pathTable, tempFiles);
            }
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(CmdExeLocationsData))]
        public async Task VerifyExternallyProvidedJobObjectNotDisposed(string cmdExeLocation)
        {
            var echoMessage = "Success";
            using var tempFiles = new TempFileStorage(canGetFileNames: false);
            var pt = new PathTable();
            using var jobObject = new JobObject(null);
            var info =
                new SandboxedProcessInfo(pt, tempFiles, cmdExeLocation, disableConHostSharing: false, loggingContext: LoggingContext)
                {
                    PipSemiStableHash = 0,
                    PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                    Arguments = "/d /c echo " + echoMessage,
                    WorkingDirectory = string.Empty,
                    DiagnosticsEnabled = true,
                    ExternallyProvidedJobObject = jobObject,
                };

            var result = await RunProcess(info);
            await CheckEchoProcessResult(result, echoMessage);

            // jobObject will throw if it is double-disposed.
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public async Task SandboxedProcessJobAccountingInformationAsync()
        {
            if (OperatingSystemHelper.IsMacOS)
            {
                return;
            }

            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                string tempFileName = tempFiles.GetUniqueFileName();
                var pt = new PathTable();
                var info = new SandboxedProcessInfo(pt, tempFiles, "/usr/bin/time", disableConHostSharing: false, loggingContext: LoggingContext)
                {
                    PipSemiStableHash = 0,
                    PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                    Arguments = $"/bin/sh -c \"sleep 1; openssl rand -out {tempFileName} 100000000; sleep 1;\"",
                };

                info.FileAccessManifest.PipId = GetNextPipId();
                info.FileAccessManifest.ReportFileAccesses = true;
                info.FileAccessManifest.FailUnexpectedFileAccesses = false;
                info.MonitoringConfig = new SandboxedProcessResourceMonitoringConfig(enabled: true, refreshInterval: TimeSpan.FromTicks(1));
                info.SandboxConnection = GetSandboxConnection();

                var result = await RunProcess(info);

                XAssert.AreEqual(0, result.ExitCode);
                XAssert.IsNotNull(result.FileAccesses);
                XAssert.IsTrue(result.JobAccountingInformation.HasValue);

                /*
                    With /usr/bin/time being the root process and not tracked, we expect the following child processes to be tracked (t):

                    /usr/bin/time
                    t    /bin/sh
                    t        sleep 1
                    t        openssl
                    t        sleep 1

                    The processes can be wrappend in id/sudo invocations depending on the environemt, so we relax the
                    assertion and make sure we tracked at least four processes.
                */
                XAssert.IsTrue(result.JobAccountingInformation.Value.NumberOfProcesses >= 4);

                XAssert.IsTrue(result.JobAccountingInformation.Value.MemoryCounters.PeakWorkingSetMb > 0);
                XAssert.IsTrue(result.JobAccountingInformation.Value.MemoryCounters.AverageWorkingSetMb > 0);

                XAssert.IsTrue(result.JobAccountingInformation.Value.UserTime.Ticks > 0);
                XAssert.IsTrue(result.JobAccountingInformation.Value.KernelTime.Ticks > 0);

                XAssert.IsTrue(result.JobAccountingInformation.Value.IO.ReadCounters.OperationCount > 0);
                XAssert.IsTrue(result.JobAccountingInformation.Value.IO.WriteCounters.OperationCount > 0);
            }
        }

        private async Task ExecuteAndAssertOpenedFileOrDirectoryAttributeTest(bool isDirectory, AbsolutePath tempRootDir, string cmdLineArguments, PathTable pathTable, TempFileStorage tempFiles)
        {
            var info =
                new SandboxedProcessInfo(pathTable, tempFiles, CmdHelper.CmdX64, disableConHostSharing: false, loggingContext: LoggingContext)
                {
                    PipSemiStableHash = 0,
                    PipDescription = DiscoverCurrentlyExecutingXunitTestMethodFQN(),
                    Arguments = cmdLineArguments,
                };

            info.FileAccessManifest.AddScope(
                AbsolutePath.Invalid,
                FileAccessPolicy.MaskNothing,
                FileAccessPolicy.AllowAll); // Ignore everything outside of the temp directory
            info.FileAccessManifest.AddScope(
                tempRootDir,
                mask: FileAccessPolicy.MaskNothing,
                values: FileAccessPolicy.AllowAll | FileAccessPolicy.ReportAccess); // explicitly report all accesses inside temp directory

            var result = await RunProcess(info);
            var fileAccesses = result.ExplicitlyReportedFileAccesses.ToList();

            foreach (var access in fileAccesses)
            {
                if (isDirectory)
                {
                    Assert.True(access.IsOpenedHandleDirectory());
                }
                else
                {
                    Assert.False(access.IsOpenedHandleDirectory());
                }
            }
        }

        private static ReportedFileAccess GetFileAccessForReadFileOperation(
            AbsolutePath path,
            ReportedProcess process,
            bool denied,
            bool explicitlyReported)
        {
            return ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Read,
                denied ? FileAccessStatus.Denied : FileAccessStatus.Allowed,
                explicitlyReported,
                0,
                ReportedFileAccess.NoUsn,
                DesiredAccess.GENERIC_READ,
                ShareMode.FILE_SHARE_READ,
                CreationDisposition.OPEN_EXISTING,
                FlagsAndAttributes.FILE_FLAG_OPEN_NO_RECALL,
                path);
        }

        private static ReportedFileAccess GetFileAccessForWriteFileOperation(
            AbsolutePath path,
            ReportedProcess process,
            bool denied,
            bool explicitlyReported)
        {
            return ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                RequestedAccess.Write,
                denied ? FileAccessStatus.Denied : FileAccessStatus.Allowed,
                explicitlyReported,
                0,
                ReportedFileAccess.NoUsn,
                DesiredAccess.GENERIC_WRITE,
                ShareMode.FILE_SHARE_READ,
                CreationDisposition.OPEN_ALWAYS,
                FlagsAndAttributes.FILE_FLAG_OPEN_NO_RECALL,
                path);
        }

        private static string[] ToLines(string str)
            => str.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    internal class RelaxedReportedFileAccessComparer : EqualityComparer<ReportedFileAccess>
    {
        private readonly PathTable m_pathTable;

        internal RelaxedReportedFileAccessComparer(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        public override bool Equals(ReportedFileAccess x, ReportedFileAccess y)
        {
            return RelevantFieldsToString(x).Equals(RelevantFieldsToString(y));
        }

        public override int GetHashCode(ReportedFileAccess obj)
        {
            return RelevantFieldsToString(obj).GetHashCode();
        }

        private string RelevantFieldsToString(ReportedFileAccess rfa)
        {
            return I($"{rfa.GetPath(m_pathTable)}|{rfa.Status}|{rfa.RequestedAccess}|{rfa.ExplicitlyReported}");
        }
    }
}
