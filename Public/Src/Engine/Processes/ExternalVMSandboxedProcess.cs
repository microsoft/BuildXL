// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Processes
{
    /// <summary>
    /// Sandboxed process that will be executed in VM.
    /// </summary>
    public class ExternalVMSandboxedProcess : ExternalSandboxedProcess
    {
        /// <summary>
        /// Default VmCommandProxy relative path.
        /// </summary>
        public const string DefaultVmCommandProxyRelativePath = @"tools\VmCommandProxy\tools\VmCommandProxy.exe";

        private int m_processId = -1;

        /// <inheritdoc />
        public override int ProcessId => m_processId != -1 ? m_processId : (m_processId = Process?.Id ?? -1);

        /// <summary>
        /// Underlying managed <see cref="Process"/> object.
        /// </summary>
        public Process Process => m_processExecutor?.Process;

        /// <inheritdoc />
        public override string StdOut => m_processExecutor?.StdOutCompleted ?? false ? m_error.ToString() : string.Empty;

        /// <inheritdoc />
        public override string StdErr => m_processExecutor?.StdErrCompleted ?? false ? m_error.ToString() : string.Empty;

        /// <inheritdoc />
        public override int? ExitCode => m_processExecutor.ExitCompleted ? Process?.ExitCode : default;

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private AsyncProcessExecutor m_processExecutor;

        private readonly ExternalToolSandboxedProcessExecutor m_tool;
        private readonly string m_vmCommandProxy;

        private readonly string m_userName;
        private readonly SecureString m_password;

        /// <summary>
        /// Creates an instance of <see cref="ExternalVMSandboxedProcess"/>.
        /// </summary>
        public ExternalVMSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo, 
            string vmCommandProxy, 
            ExternalToolSandboxedProcessExecutor tool,
            string userName,
            SecureString password)
            : base(sandboxedProcessInfo)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(vmCommandProxy));
            Contract.Requires(tool != null);

            m_vmCommandProxy = vmCommandProxy;
            m_tool = tool;
            m_userName = userName;
            m_password = password;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_processExecutor?.Dispose();
        }

        /// <inheritdoc />
        public override ulong? GetActivePeakMemoryUsage() => m_processExecutor?.GetActivePeakMemoryUsage();

        /// <inheritdoc />
        [SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Requires(m_processExecutor != null);

            // (1) Wait for VmCommandProxy.
            await m_processExecutor.WaitForExitAsync();
            await m_processExecutor.WaitForStdOutAndStdErrAsync();

            // (2) Validate result of VmCommandProxy.
            if (m_processExecutor.TimedOut || m_processExecutor.Killed)
            {
                // If timed out/killed, then sandboxed process result may have not been deserialized yet.
                return CreateResultForVmCommandProxyFailure();
            }

            if (Process.ExitCode != 0)
            {
                return CreateResultForVmCommandProxyFailure();
            }

            // (3) Validate the result of sandboxed process executor run by VmCommandProxy.
            RunResult runVmResult = JsonConvert.DeserializeObject<RunResult>(File.ReadAllText(RunOutputPath));

            if (runVmResult.ProcessStateInfo.ExitCode != 0)
            {
                return CreateResultForSandboxExecutorFailure(runVmResult);
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override Task KillAsync() => KillProcessExecutorAsync(m_processExecutor);

        /// <inheritdoc />
        public override void Start()
        {
            InitVM();
            RunInVM();
        }

        private void InitVM()
        {
            // (1) Create and serialize 'StartBuild' request.
            var startBuildRequest = new StartBuildRequest
            {
                HostLowPrivilegeUsername = m_userName,
                HostLowPrivilegePassword = m_password != null ? SecureStringToString(m_password) : null
            };

            SerializeVmCommandProxyInput(StartBuildRequestPath, startBuildRequest);

            // (2) Create a process to execute VmCommandProxy.
            string arguments = $"StartBuild /InputJsonFile:\"{StartBuildRequestPath}\"";
            var process = CreateVmCommandProxyProcess(arguments);

            var stdOutForStartBuild = new StringBuilder();
            var stdErrForStartBuild = new StringBuilder();

            // (3) Run VmCommandProxy to start build.
            using (var executor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMilliseconds(-1),
                line => AppendLineIfNotNull(stdOutForStartBuild, line),
                line => AppendLineIfNotNull(stdErrForStartBuild, line),
                SandboxedProcessInfo))
            {
                executor.Start();
                executor.WaitForExitAsync().GetAwaiter().GetResult();
                executor.WaitForStdOutAndStdErrAsync().GetAwaiter().GetResult();

                if (executor.Process.ExitCode != 0)
                {
                    string stdOut = $"{Environment.NewLine}StdOut:{Environment.NewLine}{stdOutForStartBuild.ToString()}";
                    string stdErr = $"{Environment.NewLine}StdErr:{Environment.NewLine}{stdErrForStartBuild.ToString()}";
                    ThrowBuildXLException($"Failed to init VM '{m_vmCommandProxy} {arguments}', with exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
                }
            }
        }

        private void RunInVM()
        {
            // (1) Serialize sandboxed prosess info.
            SerializeSandboxedProcessInfoToFile();

            // (2) Create and serialize run request.
            var runRequest = new RunRequest
            {
                AbsolutePath = m_tool.ExecutablePath,
                Arguments = m_tool.CreateArguments(GetSandboxedProcessInfoFile(), GetSandboxedProcessResultsFile()),
                WorkingDirectory = SandboxedProcessInfo.WorkingDirectory
            };

            SerializeVmCommandProxyInput(RunRequestPath, runRequest);

            // (2) Create a process to execute VmCommandProxy.
            string arguments = $"Run /InputJsonFile:\"{RunRequestPath}\" /OutputJsonFile:\"{RunOutputPath}\"";
            var process = CreateVmCommandProxyProcess(arguments);

            m_processExecutor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMilliseconds(-1), // Timeout should only be applied to the process that the external tool executes.
                line => AppendLineIfNotNull(m_output, line),
                line => AppendLineIfNotNull(m_error, line),
                SandboxedProcessInfo);

            m_processExecutor.Start();
        }

        private string RunRequestPath => GetVmCommandProxyPath("VmRunInput");

        private string RunOutputPath => GetVmCommandProxyPath("VmRunOutput");

        private string StartBuildRequestPath => GetVmCommandProxyPath("VmStartBuild");

        private string GetVmCommandProxyPath(string command) => Path.Combine(GetOutputDirectory(), $"{command}-Pip{SandboxedProcessInfo.PipSemiStableHash:X16}.json");

        private static void SerializeVmCommandProxyInput(string file, object input)
        {
            var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            });

            FileUtilities.CreateDirectory(Path.GetDirectoryName(file));

            using (var streamWriter = new StreamWriter(file))
            using (var jsonTextWriter = new JsonTextWriter(streamWriter))
            {
                jsonSerializer.Serialize(jsonTextWriter, input);
            }
        }

        private Process CreateVmCommandProxyProcess(string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_vmCommandProxy,
                    Arguments = arguments,
                    WorkingDirectory = SandboxedProcessInfo.WorkingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };
        }

        private static string SecureStringToString(SecureString value)
        {
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(value);
                return Marshal.PtrToStringUni(valuePtr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }

        private SandboxedProcessResult CreateResultForVmCommandProxyFailure()
        {
            string output = m_output.ToString();
            string error = m_error.ToString();
            string hint = Path.GetFileNameWithoutExtension(m_tool.ExecutablePath);

            return CreateResultForFailure(
                exitCode: m_processExecutor.TimedOut ? ExitCodes.Timeout : Process.ExitCode,
                killed: m_processExecutor.Killed,
                timedOut: m_processExecutor.TimedOut,
                output: output,
                error: error,
                hint: hint);
        }

        private SandboxedProcessResult CreateResultForSandboxExecutorFailure(RunResult runVmResult)
        {
            Contract.Requires(runVmResult != null);

            return CreateResultForFailure(
                exitCode: runVmResult.ProcessStateInfo.ExitCode,
                killed: false,
                timedOut: false,
                output: runVmResult.StdOut ?? string.Empty,
                error: runVmResult.StdErr ?? string.Empty,
                hint: Path.GetFileNameWithoutExtension(m_tool.ExecutablePath));
        }
    }
}
