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
        /// Path to VmCommandProxy executable.
        /// </summary>
        public string VmCommandProxy { get; }

        private readonly string m_userName;
        private readonly string m_password;

        /// <summary>
        /// Lazy VM initialization.
        /// </summary>
        public readonly Lazy<Task> LazyInitVmAsync;

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/> from build engine.
        /// </summary>
        public static VmInitializer CreateFromEngine(
            string buildEngineDirectory, 
            string userName = null, 
            string password = null) => new VmInitializer(Path.Combine(buildEngineDirectory, VmExecutable.DefaultRelativePath), userName ?? string.Empty, password ?? string.Empty);

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/>.
        /// </summary>
        public VmInitializer(string vmCommandProxy, string userName, string password)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(vmCommandProxy));
            Contract.Requires(userName != null);
            Contract.Requires(password != null);

            VmCommandProxy = vmCommandProxy;
            m_userName = userName;
            m_password = password;
            LazyInitVmAsync = new Lazy<Task>(() => InitVmAsync(), true);
        }

        private async Task InitVmAsync()
        {
            // (1) Create and serialize 'StartBuild' request.
            var startBuildRequest = new StartBuildRequest
            {
                HostLowPrivilegeUsername = m_userName,
                HostLowPrivilegePassword = m_password
            };

            string startBuildRequestPath = Path.GetTempFileName();
            VmSerializer.SerializeToFile(startBuildRequestPath, startBuildRequest);

            // (2) Create a process to execute VmCommandProxy.
            string arguments = $"{VmCommand.StartBuild} /{VmCommand.Param.InputJsonFile}:\"{startBuildRequestPath}\"";
            var process = CreateVmCommandProxyProcess(arguments, Path.GetDirectoryName(startBuildRequestPath));

            var stdOutForStartBuild = new StringBuilder();
            var stdErrForStartBuild = new StringBuilder();

            // (3) Run VmCommandProxy to start build.
            using (var executor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMilliseconds(-1),
                line => { if (line != null) { stdOutForStartBuild.AppendLine(line); } },
                line => { if (line != null) { stdErrForStartBuild.AppendLine(line); } }))
            {
                executor.Start();
                await executor.WaitForExitAsync();
                await executor.WaitForStdOutAndStdErrAsync();

                if (executor.Process.ExitCode != 0)
                {
                    string stdOut = $"{Environment.NewLine}StdOut:{Environment.NewLine}{stdOutForStartBuild.ToString()}";
                    string stdErr = $"{Environment.NewLine}StdErr:{Environment.NewLine}{stdErrForStartBuild.ToString()}";
                    throw new BuildXLException($"Failed to init VM '{VmCommandProxy} {arguments}', with exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
                }
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
