// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Utilities.VmCommandProxy
{
    /// <summary>
    /// VM initializer.
    /// </summary>
    /// <remarks>
    /// Currently, VM initialization requires user name and password, and this initialization is considered
    /// temporary due to security leak. The CB team is addressing this issue.
    /// </remarks>
    public class VmInitializer
    {
        /// <summary>
        /// Timeout (in minute) for VM initialization.
        /// </summary>
        /// <remarks>
        /// According to CB team, VM initialization roughly takes 2-3 minutes.
        /// </remarks>
        private const int InitVmTimeoutInMinute = 10;

        /// <summary>
        /// Path to VmCommandProxy executable.
        /// </summary>
        public string VmCommandProxy { get; }

        /// <summary>
        /// Lazy VM initialization.
        /// </summary>
        public readonly Lazy<Task> LazyInitVmAsync;

        private readonly Action<string> m_logStartInit;
        private readonly Action<string> m_logEndInit;
        private readonly Action<string> m_logInitExecution;

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/> from build engine.
        /// </summary>
        public static VmInitializer CreateFromEngine(
            string buildEngineDirectory,
            Action<string> logStartInit = null,
            Action<string> logEndInit = null,
            Action<string> logInitExecution = null) => new VmInitializer(Path.Combine(buildEngineDirectory, VmExecutable.DefaultRelativePath), logStartInit, logEndInit, logInitExecution);

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/>.
        /// </summary>
        public VmInitializer(string vmCommandProxy, Action<string> logStartInit = null, Action<string> logEndInit = null, Action<string> logInitExecution = null)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(vmCommandProxy));

            VmCommandProxy = vmCommandProxy;
            LazyInitVmAsync = new Lazy<Task>(() => InitVmAsync(), true);
            m_logStartInit = logStartInit;
            m_logEndInit = logEndInit;
            m_logInitExecution = logInitExecution;
        }

        private async Task InitVmAsync()
        {
            // (1) Create a process to execute VmCommandProxy.
            string arguments = $"{VmCommand.InitializeVm}";
            var process = CreateVmCommandProxyProcess(arguments, Path.GetDirectoryName(Path.GetTempFileName()));

            m_logStartInit?.Invoke($"{VmCommandProxy} {arguments}");

            var stdOutForStartBuild = new StringBuilder();
            var stdErrForStartBuild = new StringBuilder();

            string provenance = $"[{nameof(VmInitializer)}]";

            // (2) Run VmCommandProxy to start build.
            using (var executor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMinutes(InitVmTimeoutInMinute),
                line => { if (line != null) { stdOutForStartBuild.AppendLine(line); } },
                line => { if (line != null) { stdErrForStartBuild.AppendLine(line); } },
                provenance: provenance,
                logger: message => m_logInitExecution?.Invoke(message)))
            {
                executor.Start();
                await executor.WaitForExitAsync();
                await executor.WaitForStdOutAndStdErrAsync();

                string stdOut = $"{Environment.NewLine}StdOut:{Environment.NewLine}{stdOutForStartBuild.ToString()}";
                string stdErr = $"{Environment.NewLine}StdErr:{Environment.NewLine}{stdErrForStartBuild.ToString()}";

                if (executor.Process.ExitCode != 0)
                {
                    throw new BuildXLException($"Failed to init VM '{VmCommandProxy} {arguments}', with exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
                }

                m_logEndInit?.Invoke($"Exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
            }
        }

        private Process CreateVmCommandProxyProcess(string arguments, string workingDirectory)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = VmCommandProxy,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };
        }
    }
}
