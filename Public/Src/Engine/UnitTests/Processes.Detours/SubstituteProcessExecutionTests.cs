// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
    public sealed class SubstituteProcessExecutionTests : XunitBuildXLTest
    {
        private readonly ITestOutputHelper _output;

        public SubstituteProcessExecutionTests(ITestOutputHelper output)
            : base(output)
        {
            _output = output;
        }

        /// <summary>
        /// Execute a test that shims all top-level processes of cmd.exe, calling a test shim executable and verifying it was called correctly.
        /// The child process is passed to Detours via the lpApplicationName of CreateProcess(), and is unquoted.
        /// </summary>
        [Theory]
// TODO: erikmav fails on netcore, need an unmanaged shim to avoid exe/dll       [InlineData(false, null)]
// TODO: erikmav        [InlineData(true, null)]
// TODO: erikmav filtering broken generally       [InlineData(true, "cmd.exe")]  // Filter should match child
// TODO: erikmav        [InlineData(false, "cmd.exe")]  // Filter should match child
        public async Task CmdWithTestShim(bool useQuotesForChildCmdExe, string processMatch)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            Contract.Assume(currentCodeFolder != null);

            string executable = CmdHelper.CmdX64;
            string childExecutable = executable;
            string quotedExecutable = '"' + executable + '"';
            if (useQuotesForChildCmdExe)
            {
                childExecutable = quotedExecutable;
            }

            string shimProgram = Path.Combine(currentCodeFolder, "TestSubstituteProcessExecutionShim.exe");
            Assert.True(File.Exists(shimProgram), $"Shim test program not found at {shimProgram}");
            var shimProgramPath = AbsolutePath.Create(context.PathTable, shimProgram);
            
            var fam =
                new FileAccessManifest(context.PathTable)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false,
                    SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                        shimProgramPath,
                        shimAllProcesses: true,
                        processMatch == null ? new ShimProcessMatch[0] : new ShimProcessMatch[] { new ShimProcessMatch(PathAtom.Create(context.StringTable, processMatch), PathAtom.Invalid) })
                };

            Guid sessionId = Guid.NewGuid();
            string sessionIdStr = sessionId.ToString("N");
            var loggingContext = new LoggingContext(sessionId, "TestSession", new LoggingContext.SessionInfo(sessionIdStr, "env", sessionId));

            string childOutput = "Child cmd that should be shimmed";
            string childArgs = $"{childExecutable} /D /C @echo {childOutput}";

            // Detours logic should wrap the initial cmd in quotes for easier parsing by shim logic.
            string childShimArgs = $"{quotedExecutable}  /D /C @echo {childOutput}";

            string args = "/D /C echo Top-level cmd. Running child process && " + childArgs;

            var stdoutSb = new StringBuilder(128);
            var stderrSb = new StringBuilder();

            var sandboxedProcessInfo = new SandboxedProcessInfo(
                context.PathTable,
                new LocalSandboxedFileStorage(),
                executable,
                disableConHostSharing: true,
                loggingContext: loggingContext,
                fileAccessManifest: fam)
            {
                PipDescription = executable,
                Arguments = args,
                WorkingDirectory = Environment.CurrentDirectory,

                StandardOutputEncoding = Encoding.UTF8,
                StandardOutputObserver = stdoutStr => stdoutSb.AppendLine(stdoutStr),

                StandardErrorEncoding = Encoding.UTF8,
                StandardErrorObserver = stderrStr => stderrSb.AppendLine(stderrStr),

                EnvironmentVariables = BuildParameters.GetFactory().PopulateFromEnvironment(),

                Timeout = TimeSpan.FromMinutes(1),
            };

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            string stdout = stdoutSb.ToString();
            _output.WriteLine($"stdout: {stdout}");

            string stderr = stderrSb.ToString();
            _output.WriteLine($"stderr: {stderr}");
            Assert.Equal(0, stderr.Length);

            string shimOutput = "TestShim: Entered with command line: " + childShimArgs;
            int indexOfShim = stdout.IndexOf(shimOutput, StringComparison.Ordinal);
            Assert.True(indexOfShim > 0);
        }

        /// <summary>
        /// Shim no top-level processes and ensure the child is not shimmed.
        /// </summary>
        [Theory]
        [InlineData(false, null)]
// TODO: erikmav filtering broken generally       [InlineData(true, "foo.exe")]  // Filter should not match
        public async Task CmdWithTestShim_ShimNothingRunsChildProcessWithoutShim(bool shimAllProcesses, string processMatch)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            Contract.Assume(currentCodeFolder != null);

            string executable = CmdHelper.CmdX64;

            string shimProgram = Path.Combine(currentCodeFolder, "TestSubstituteProcessExecutionShim.exe");
            Assert.True(File.Exists(shimProgram), $"Shim test program not found at {shimProgram}");
            var shimProgramPath = AbsolutePath.Create(context.PathTable, shimProgram);
            
            var fam =
                new FileAccessManifest(context.PathTable)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false,
                    SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(
                        shimProgramPath,
                        shimAllProcesses: false,
                        processMatch == null ? new ShimProcessMatch[0] : new ShimProcessMatch[] { new ShimProcessMatch(PathAtom.Create(context.StringTable, processMatch), PathAtom.Invalid) })
                };

            Guid sessionId = Guid.NewGuid();
            string sessionIdStr = sessionId.ToString("N");
            var loggingContext = new LoggingContext(sessionId, "TestSession", new LoggingContext.SessionInfo(sessionIdStr, "env", sessionId));

            string childOutput = "Child cmd that should not be shimmed";
            string childArgs = $"{executable} /D /C @echo {childOutput}";
            string args = "/D /C echo Top-level cmd. Running child process && " + childArgs;

            var stdoutSb = new StringBuilder(128);
            var stderrSb = new StringBuilder();

            var sandboxedProcessInfo = new SandboxedProcessInfo(
                context.PathTable,
                new LocalSandboxedFileStorage(),
                executable,
                disableConHostSharing: true,
                loggingContext: loggingContext,
                fileAccessManifest: fam)
            {
                PipDescription = executable,
                Arguments = args,
                WorkingDirectory = Environment.CurrentDirectory,

                StandardOutputEncoding = Encoding.UTF8,
                StandardOutputObserver = stdoutStr => stdoutSb.AppendLine(stdoutStr),

                StandardErrorEncoding = Encoding.UTF8,
                StandardErrorObserver = stderrStr => stderrSb.AppendLine(stderrStr),

                EnvironmentVariables = BuildParameters.GetFactory().PopulateFromEnvironment(),

                Timeout = TimeSpan.FromMinutes(1),
            };

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);

            string stdout = stdoutSb.ToString();
            _output.WriteLine($"stdout: {stdout}");

            string stderr = stderrSb.ToString();
            _output.WriteLine($"stderr: {stderr}");
            Assert.Equal(0, stderr.Length);

            string shimOutput = "TestShim: Entered with command line";
            int indexOfShim = stdout.IndexOf(shimOutput, StringComparison.Ordinal);
            Assert.True(indexOfShim == -1);
            int indexOfChild = stdout.LastIndexOf(childOutput, StringComparison.Ordinal);
            Assert.True(indexOfChild > 0, "Child should have run and written output");
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
