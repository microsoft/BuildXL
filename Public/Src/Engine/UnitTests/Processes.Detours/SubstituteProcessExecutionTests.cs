// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes.Detours
{
    /// <summary>
    /// Tests for substitute process execution shim capability in the Windows Detours code.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public sealed class SubstituteProcessExecutionTests : XunitBuildXLTest
    {
        private readonly ITestOutputHelper m_output;
        private const string SubstituteProcessExecutionShimFileName = "TestSubstituteProcessExecutionShim.exe";
        private const string SubstituteProcessExecutionPluginFileName = "SubstituteProcessExecutionPlugin.dll";
        private const string SubstituteProcessExecutionPluginFolderName = "SubstitutePlugin";
        private const string ShimOutput = "TestShim: Entered with command line: ";

        public SubstituteProcessExecutionTests(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
            RegisterEventSource(ETWLogger.Log);
        }

        /// <summary>
        /// Execute a test that shims all top-level processes of cmd.exe, calling a test shim executable and verifying it was called correctly.
        /// The child process is passed to Detours via the lpApplicationName of CreateProcess(), and is unquoted.
        /// </summary>
        [Theory]
        [InlineData(false, null)]
        [InlineData(true , null)]
        [InlineData(true , "cmd.exe")]  // Filter should match child
        [InlineData(false, "cmd.exe")]  // Filter should match child
        public async Task CmdWithTestShimAsync(bool useQuotesForChildCmdExe, string processMatch)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var shimProgramPath = GetShimProgramPath(context);

            string executable = CmdHelper.CmdX64;
            string childExecutable = executable;
            string quotedExecutable = '"' + executable + '"';
            if (useQuotesForChildCmdExe)
            {
                childExecutable = quotedExecutable;
            }

            var fam = CreateCommonFileAccessManifest(context.PathTable);
            fam.SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                shimProgramPath,
                shimAllProcesses: processMatch == null,  // When we have a process to match, make the shim list opt-in to ensure a match
                processMatches: processMatch == null 
                    ? new ShimProcessMatch[0] 
                    : new[] { new ShimProcessMatch(PathAtom.Create(context.StringTable, processMatch), PathAtom.Invalid) });

            string childOutput = "Child cmd that should be shimmed";
            string childArgs = $"{childExecutable} /D /C @echo {childOutput}";

            // Detours logic should wrap the initial cmd in quotes for easier parsing by shim logic.
            // However, since we're indirecting through a cmd.exe command line it gets dropped along the way.
            string childShimArgs = $"/D /C @echo {childOutput}";

            string args = $"/D /C echo Top-level cmd. Running child process && {childArgs}";

            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();

            SandboxedProcessInfo sandboxedProcessInfo = CreateCommonSandboxedProcessInfo(
                context,
                executable,
                args,
                fam,
                stdOutSb,
                stdErrSb);

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            AssertSuccess(result, stdErr);

            // The shim is an exe on netframework, dll in a temp dir on netcore, so don't try to match it.
            AssertShimmed(stdOut);
            AssertShimArgs(stdOut, childShimArgs);
        }

        /// <summary>
        /// Execute a test that shims all top-level processes of cmd.exe, calling a test shim executable and verifying it was called correctly.
        /// The child process is passed to Detours via the lpApplicationName of CreateProcess(), and is unquoted.
        /// </summary>
        [Fact]
        public async Task CmdWithStartQuoteOnlyFailsToRunFullCommandLineAsync()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var shimProgramPath = GetShimProgramPath(context);

            var fam = CreateCommonFileAccessManifest(context.PathTable);
            fam.SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                shimProgramPath,
                shimAllProcesses: true,
                processMatches: new ShimProcessMatch[0]);

            string executable = CmdHelper.CmdX64;
            string childExecutable = '"' + executable;  // Only an open quote
            string childOutput = "Child cmd that should be shimmed";
            string childArgs = $"{childExecutable} /D /C @echo {childOutput}";

            string args = "/D /C echo Top-level cmd. Running child process && " + childArgs;

            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();

            SandboxedProcessInfo sandboxedProcessInfo = CreateCommonSandboxedProcessInfo(
                context,
                executable,
                args,
                fam,
                stdOutSb,
                stdErrSb);

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            XAssert.AreEqual(1, result.ExitCode);
            XAssert.AreEqual("The system cannot find the path specified.\r\n", stdErr);
        }

        /// <summary>
        /// Shim no top-level processes and ensure the child is shimmed if all processes are instructed to be shimmed.
        /// </summary>
        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public async Task CmdWithTestShim_ShimNothingRunsChildProcessWithoutShimAsync(bool shimAllProcess, bool filterMatch)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var shimProgramPath = GetShimProgramPath(context);

            string executable = CmdHelper.CmdX64;
            string processMatch = filterMatch ? null : "foo.exe"; // Filter should never match foo.exe.

            var fam = CreateCommonFileAccessManifest(context.PathTable);
            fam.SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                shimProgramPath,
                shimAllProcesses: shimAllProcess,
                processMatches: processMatch == null 
                    ? new ShimProcessMatch[0]
                    : new[] { new ShimProcessMatch(PathAtom.Create(context.StringTable, processMatch), PathAtom.Invalid) });

            string predicate = shimAllProcess ? string.Empty : "not ";
            string childOutput = $"Child cmd that should {predicate}be shimmed";
            string childArgs = $"{executable} /D /C @echo {childOutput}";
            string args = "/D /C echo Top-level cmd. Running child process && " + childArgs;

            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();

            SandboxedProcessInfo sandboxedProcessInfo = CreateCommonSandboxedProcessInfo(
                context,
                executable,
                args,
                fam,
                stdOutSb,
                stdErrSb);

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            AssertSuccess(result, stdErr);
            AssertShimmedIf(stdOut, shimAllProcess);
            XAssert.Contains(stdOut, childOutput);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public async Task TestShimChildProcessWithPluginAndEmptyMatchesAsync(bool shimAllProcesses, bool shouldBeShimmed)
        {
            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();
            
            SandboxedProcessResult result = await RunWithPluginAsync(
                shimAllProcesses,
                shouldBeShimmed,
                null, // No process match.
                stdOutSb,
                stdErrSb);

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            AssertSuccess(result, stdErr);
            AssertShimmedIf(stdOut, shimAllProcesses != shouldBeShimmed);
            XAssert.Contains(stdOut, GetChildOutputForPluginTest(shouldBeShimmed));
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 3, MemberType = typeof(TruthTable))]
        public async Task TestShimChildProcessWithPluginAndNonEmptyMatchesAsync(bool shimAllProcesses, bool shouldBeShimmed, bool shouldFindMatch)
        {
            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();

            SandboxedProcessResult result = await RunWithPluginAsync(
                shimAllProcesses,
                shouldBeShimmed,
                shouldFindMatch ? "cmd.exe" : "foo.exe",
                stdOutSb,
                stdErrSb);

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            AssertSuccess(result, stdErr);

            if (shimAllProcesses)
            {
                AssertShimmedIf(stdOut, !shouldFindMatch && !shouldBeShimmed);
            }
            else
            {
                AssertShimmedIf(stdOut, shouldFindMatch || shouldBeShimmed);
            }

            XAssert.Contains(stdOut, GetChildOutputForPluginTest(shouldBeShimmed));
        }

        [Fact]
        public async Task TestShimChildProcessWithPluginWithModifiedArgumentAsync()
        {
            var stdOutSb = new StringBuilder(128);
            var stdErrSb = new StringBuilder();

            SandboxedProcessResult result = await RunWithPluginAsync(
                false,
                true,
                null, // No process match.
                stdOutSb,
                stdErrSb,
                shimmedText: "@responseFile");

            string stdOut = stdOutSb.ToString();
            string stdErr = stdErrSb.ToString();

            m_output.WriteLine($"stdout: {stdOut}");
            m_output.WriteLine($"stderr: {stdErr}");

            AssertSuccess(result, stdErr);
            AssertShimmed(stdOut);

            // Since shimmedText has '@', it will be replaced by "Content".
            // CODESYNC: Public\Src\Engine\UnitTests\Processes.TestPrograms\SubstituteProcessExecutionPlugin\dllmain.cpp
            XAssert.Contains(stdOut, GetChildOutputForPluginTest(true, "Content"));
        }

        private async Task<SandboxedProcessResult> RunWithPluginAsync(
            bool shimAllProcesses,
            bool shouldBeShimmed,
            string processMatch,
            StringBuilder stdOutSb,
            StringBuilder stdErrSb,
            string shimmedText = null)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var shimProgramPath = GetShimProgramPath(context);
            var pluginDlls = GetPluginDlls(context);

            string executable = CmdHelper.CmdX64;

            var fam = CreateCommonFileAccessManifest(context.PathTable);
            fam.SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                shimProgramPath,
                shimAllProcesses: shimAllProcesses,
                processMatches: string.IsNullOrEmpty(processMatch)
                    ? new ShimProcessMatch[0]
                    : new ShimProcessMatch[] { new ShimProcessMatch(PathAtom.Create(context.StringTable, processMatch), PathAtom.Invalid) })
            {
                SubstituteProcessExecutionPluginDll32Path = pluginDlls.x86Dll,
                SubstituteProcessExecutionPluginDll64Path = pluginDlls.x64Dll
            };

            string childOutput = GetChildOutputForPluginTest(shouldBeShimmed, shimmedText);
            string childArgs = $"{executable} /D /C echo {childOutput}";
            string args = $"/D /C echo Top-level cmd. Running child process && {childArgs}";

            SandboxedProcessInfo sandboxedProcessInfo = CreateCommonSandboxedProcessInfo(
                context,
                executable,
                args,
                fam,
                stdOutSb,
                stdErrSb);

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);

            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            // When plugin is entered, expect to see "Entering CommandMatches" text in the log.
            // CODESYNC: Public\Src\Engine\UnitTests\Processes.TestPrograms\SubstituteProcessExecutionPlugin\dllmain.cpp
            AssertLogContains(true, "Entering CommandMatches");
            
            return result;
        }

        /// <summary>
        /// The plugin will match if the command line does not contain "DoNotShimMe".
        /// CODESYNC: Public\Src\Engine\UnitTests\Processes.TestPrograms\SubstituteProcessExecutionPlugin\dllmain.cpp
        /// </summary>
        private static string GetChildOutputForPluginTest(bool shouldBeShimmed, string shimmedText = null) => shouldBeShimmed ? (shimmedText ?? "Whatever") : "DoNotShimMe";

        private static void AssertShimmedIf(string output, bool shimmedCondition)
        {
            if (shimmedCondition)
            {
                XAssert.Contains(output, ShimOutput);
            }
            else
            {
                XAssert.ContainsNot(output, ShimOutput);
            }
        }

        private static void AssertShimmed(string output) => AssertShimmedIf(output, true);

        private static void AssertNotShimmed(string output) => AssertShimmedIf(output, false);

        private static void AssertSuccess(SandboxedProcessResult result, string stdError)
        {
            XAssert.AreEqual(0, result.ExitCode);
            XAssert.AreEqual(string.Empty, stdError);
        }

        private static void AssertShimArgs(string output, string args)
        {
            AssertShimmed(output);
            int indexOfShim = output.IndexOf(ShimOutput, StringComparison.Ordinal);
            int indexOfShimArgs = output.LastIndexOf(args);
            XAssert.IsTrue(indexOfShimArgs > indexOfShim, $"Expecting shim args: {args}");
        }

        /// <summary>
        /// Gets the path to the substitute process execution shim.
        /// </summary>
        private static AbsolutePath GetShimProgramPath(BuildXLContext context)
        {
            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            XAssert.IsNotNull(currentCodeFolder);

            string shimProgram = Path.Combine(currentCodeFolder, SubstituteProcessExecutionShimFileName);
            XAssert.IsTrue(File.Exists(shimProgram), $"Shim test program not found at {shimProgram}");
            
            return AbsolutePath.Create(context.PathTable, shimProgram);
        }

        private static (AbsolutePath x64Dll, AbsolutePath x86Dll) GetPluginDlls(BuildXLContext context)
        {
            AbsolutePath shimProgramPath = GetShimProgramPath(context);
            AbsolutePath shimProgramDirectoryPath = shimProgramPath.GetParent(context.PathTable);
            RelativePath x64PluginRelativePath = RelativePath.Create(context.StringTable, Path.Combine(SubstituteProcessExecutionPluginFolderName, "x64", SubstituteProcessExecutionPluginFileName));
            RelativePath x86PluginRelativePath = RelativePath.Create(context.StringTable, Path.Combine(SubstituteProcessExecutionPluginFolderName, "x86", SubstituteProcessExecutionPluginFileName));

            AbsolutePath x64PluginPath = shimProgramDirectoryPath.Combine(context.PathTable, x64PluginRelativePath);
            AbsolutePath x86PluginPath = shimProgramDirectoryPath.Combine(context.PathTable, x86PluginRelativePath);

            XAssert.IsTrue(
                File.Exists(x64PluginPath.ToString(context.PathTable))
                || File.Exists(x86PluginPath.ToString(context.PathTable)),
                $"No plugin '{x64PluginPath.ToString(context.PathTable)}' or '{x86PluginPath.ToString(context.PathTable)}' can be found.");

            return (x64PluginPath, x86PluginPath);
        }

        /// <summary>
        /// Creates a logging context for testing.
        /// </summary>
        private static LoggingContext CreateLoggingContext()
        {
            Guid sessionId = Guid.NewGuid();
            string sessionIdStr = sessionId.ToString("N");
            return new LoggingContext(sessionId, nameof(SubstituteProcessExecutionTests), new LoggingContext.SessionInfo(sessionIdStr, "TestEnv", sessionId));
        }

        /// <summary>
        /// Creates a common file access manifest for this test.
        /// </summary>
        private static FileAccessManifest CreateCommonFileAccessManifest(PathTable pathTable) =>
            new FileAccessManifest(pathTable)
            {
                FailUnexpectedFileAccesses = false,
                IgnoreCodeCoverage = false,
                ReportFileAccesses = false,
                ReportUnexpectedFileAccesses = false,
                MonitorChildProcesses = false
            };

        /// <summary>
        /// Creates a common sandboxed process info for tests in this class.
        /// </summary>
        private static SandboxedProcessInfo CreateCommonSandboxedProcessInfo(
            BuildXLContext context,
            string executable,
            string arguments,
            FileAccessManifest fileAccessManifest,
            StringBuilder stdOutBuilder,
            StringBuilder stdErrBuilder)
        {
            return new SandboxedProcessInfo(
                context.PathTable,
                new LocalSandboxedFileStorage(),
                executable,
                disableConHostSharing: true,
                loggingContext: CreateLoggingContext(),
                fileAccessManifest: fileAccessManifest)
            {
                PipDescription = executable,
                Arguments = arguments,
                WorkingDirectory = Environment.CurrentDirectory,

                StandardOutputEncoding = Encoding.UTF8,
                StandardOutputObserver = stdOutStr => stdOutBuilder.AppendLine(stdOutStr),

                StandardErrorEncoding = Encoding.UTF8,
                StandardErrorObserver = stdErrStr => stdErrBuilder.AppendLine(stdErrStr),

                EnvironmentVariables = BuildParameters.GetFactory().PopulateFromEnvironment(),

                Timeout = TimeSpan.FromMinutes(1),
            };
        }

        // Based on implementation in SandboxExec Program.cs
        private sealed class LocalSandboxedFileStorage : ISandboxedProcessFileStorage
        {
            public string GetFileName(SandboxedProcessFile file)
            {
                return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
            }
        }
    }
}
