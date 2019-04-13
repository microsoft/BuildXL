// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Class for launching sandboxed process pip from an external tool.
    /// </summary>
    public class ExternalToolSandboxedProcessPipExecutor : ExternalSandboxedProcessPipExecutor
    {
        /// <summary>
        /// Default tool path.
        /// </summary>
        public const string DefaultToolRelativePath = @"tools\SandboxedProcessExecutor\SandboxedProcessExecutor.exe";

        private readonly string m_toolPath;
        private readonly string m_sandboxedProcessInfoInputFile;
        private readonly string m_sandboxedProcessResultOutputFile;

        /// <summary>
        /// Creates an instance of <see cref="ExternalToolSandboxedProcessPipExecutor"/>.
        /// </summary>
        public ExternalToolSandboxedProcessPipExecutor(
            SandboxedProcessInfo sandboxedProcessInfo,
            string toolPath,
            string sandboxedProcessInfoInputFile,
            string sandboxedProcessResultOutputFile,
            CancellationToken cancellationToken,
            ITempDirectoryCleaner tempDirectoryCleaner)
            : base(sandboxedProcessInfo, cancellationToken, tempDirectoryCleaner)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(toolPath));
            Contract.Requires(!string.IsNullOrWhiteSpace(sandboxedProcessInfoInputFile));
            Contract.Requires(!string.IsNullOrWhiteSpace(sandboxedProcessResultOutputFile));

            m_toolPath = toolPath;
            m_sandboxedProcessInfoInputFile = sandboxedProcessInfoInputFile;
            m_sandboxedProcessResultOutputFile = sandboxedProcessResultOutputFile;
        }

        /// <inheritdoc />
        public override async Task<Result> ExecuteAsync()
        {
            var maybeSerialize = await TrySerializeInfoToFileAsync(m_sandboxedProcessInfoInputFile);
            if (!maybeSerialize.Succeeded)
            {
                return Result.CreateForExecutionFailure(maybeSerialize.Failure);
            }

            (int? exitCode, string stdOut, string stdErr) = await RunProcessAsync(
                m_toolPath, 
                CreateArguments(), 
                SandboxedProcessInfo.Timeout.HasValue 
                ? (int) SandboxedProcessInfo.Timeout.Value.TotalMilliseconds 
                : int.MaxValue);

            if (!exitCode.HasValue)
            {
                return Result.CreateForExecutionFailure(new Failure<string>($"'{m_toolPath}' hung"));
            }

            SandboxedProcessExecutorExitCode executorExitCode = FromExternalProcessExitCode(exitCode.Value);

            if (executorExitCode != SandboxedProcessExecutorExitCode.Success)
            {
                return Result.CreateForExecutionFailure(
                    executorExitCode,
                    stdOut,
                    stdErr,
                    new Failure<string>($"Tool exit code is {exitCode.Value}"));
            }

            var maybeDeserialize = await TryDeserializeResultFromFileAsync(m_sandboxedProcessResultOutputFile);
            if (!maybeDeserialize.Succeeded)
            {
                return Result.CreateForExecutionFailure(maybeDeserialize.Failure);
            }

            return Result.CreateForSuccessfulExecution(executorExitCode, maybeDeserialize.Result, stdOut, stdErr);
        }

        private string CreateArguments() => $"/sandboxedProcessInfo:{m_sandboxedProcessInfoInputFile} /sandboxedProcessResult:{m_sandboxedProcessResultOutputFile}";
        

        private static async Task<(int? exitCode, string stdOut, string stdErr)> RunProcessAsync(string fileName, string arguments, int timeout)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new System.Diagnostics.Process() { StartInfo = startInfo })
            {
                var stdOutBuilder = new StringBuilder();
                var stdOutCloseEvent = new TaskCompletionSource<bool>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                    {
                        stdOutCloseEvent.SetResult(true);
                    }
                    else
                    {
                        stdOutBuilder.AppendLine(e.Data);
                    }
                };

                var stdErrBuilder = new StringBuilder();
                var stdErrCloseEvent = new TaskCompletionSource<bool>();

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null)
                    {
                        stdErrCloseEvent.SetResult(true);
                    }
                    else
                    {
                        stdErrBuilder.AppendLine(e.Data);
                    }
                };

                var isStarted = process.Start();
                if (!isStarted)
                {
                    return (process.ExitCode, null, null);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var waitForExit = Task.Run(() => process.WaitForExit(timeout));

                var processTask = Task.WhenAll(waitForExit, stdOutCloseEvent.Task, stdErrCloseEvent.Task);

                if (await Task.WhenAny(Task.Delay(timeout), processTask) == processTask && await waitForExit)
                {
                    return (process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString());
                }
                else
                {
                    try
                    {
                        process.Kill();
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch
                    {
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }

                return (null, null, null);
            }
        }
    }
}