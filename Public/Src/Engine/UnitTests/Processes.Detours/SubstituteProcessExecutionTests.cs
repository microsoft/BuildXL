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
using Xunit;

namespace Test.BuildXL.Processes.Detours
{
    /// <summary>
    /// Tests for substitute process execution shim capability in the Windows Detours code.
    /// </summary>
    public sealed class SubstituteProcessExecutionTests
    {
        /// <summary>
        /// Execute a test that shims all top-level processes of cmd.exe, calling a test shim executable and verifying it was called correctly.
        /// </summary>
        [Fact]
        public async Task CmdWithTestShim_AllChildren()
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            string currentCodeFolder = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));
            Contract.Assume(currentCodeFolder != null);

            string executable = CmdHelper.CmdX64;

            string shimProgram = Path.Combine(currentCodeFolder, "TestShim", "TestSubstituteProcessExecutionShim.exe");
            var shimProgramPath = AbsolutePath.Create(context.PathTable, shimProgram);
            var fam =
                new FileAccessManifest(context.PathTable)
                {
                    FailUnexpectedFileAccesses = false,
                    IgnoreCodeCoverage = false,
                    ReportFileAccesses = false,
                    ReportUnexpectedFileAccesses = false,
                    MonitorChildProcesses = false,
                    SubstituteProcessExecutionInfo = new SubstituteProcessExecutionInfo(shimProgramPath, shimAllProcesses: true, new ShimProcessMatch[0])
                };

            Guid sessionId = Guid.NewGuid();
            string sessionIdStr = sessionId.ToString("N");
            var loggingContext = new LoggingContext(sessionId, "TestSession", new LoggingContext.SessionInfo(sessionIdStr, "env", sessionId));

            string childOutput = "Child cmd that should be shimmed";
            string childArgs = $"{executable} /D /C @echo {childOutput}";
            string args = "/D /C echo Top-level cmd. Running child process && " + childArgs;

            var stdoutSb = new StringBuilder(128);
            var stderrSb = new StringBuilder(128);

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

                EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(new Dictionary<string, string>()),

                Timeout = TimeSpan.FromMinutes(1),
            };

            ISandboxedProcess sandboxedProcess =
                await SandboxedProcessFactory.StartAsync(sandboxedProcessInfo, forceSandboxing: true)
                    .ConfigureAwait(false);
            SandboxedProcessResult result = await sandboxedProcess.GetResultAsync().ConfigureAwait(false);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(0, stderrSb.Length);
            string stdout = stdoutSb.ToString();
            string shimOutput = "TestShim: Entered with command line: " + childArgs;
            int indexOfShim = stdout.IndexOf(shimOutput, StringComparison.Ordinal);
            Assert.True(indexOfShim > 0);
            int indexOfChild = stdout.LastIndexOf(childOutput, StringComparison.Ordinal);
            Assert.True(indexOfChild > indexOfShim + shimOutput.Length, "Child output should be after shim output");
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
