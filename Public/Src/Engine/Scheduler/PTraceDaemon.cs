// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Logger = BuildXL.Scheduler.Tracing.Logger;
using Process = System.Diagnostics.Process;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// The PTraceDaemon class is a wrapper around the native ptracedaemon service process.
    /// This wrapper is responsible for initializing and terminating the service.
    /// </summary>
    public class PTraceDaemon
    {
        /// <summary>
        /// Sends a signal to the specified PID.
        /// </summary>
        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern unsafe int kill(int pid, int signal);
        
        /// <summary>
        /// User specifiable signal that can be caught by a process.
        /// </summary>
        private const int SIGUSR1 = 10;

        private readonly AsyncProcessExecutor m_daemonExecutor;
        private readonly LoggingContext m_loggingContext;

        private readonly string m_daemonFile;
        private readonly string m_runnerFile;

        /// <summary>
        /// Creates an instance of the PTraceDaemon class without starting the daemon.
        /// </summary>
        /// <param name="loggingContext"></param>
        public PTraceDaemon(LoggingContext loggingContext)
        {
            Contract.Requires(OperatingSystemHelper.IsLinuxOS, $"PTraceDaemon cannot be initialized on a non-Linux OS");

            m_loggingContext = loggingContext;

            m_daemonFile = SandboxedProcessUnix.EnsureDeploymentFile(SandboxConnectionLinuxDetours.PTraceDaemonFileName, setExecuteBit: true);
            m_runnerFile = SandboxedProcessUnix.EnsureDeploymentFile(SandboxConnectionLinuxDetours.PTraceRunnerFileName, setExecuteBit: true);

            var args = $"-m {SandboxConnectionLinuxDetours.LinuxSandboxPTraceMqName} -r {m_runnerFile}";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(m_daemonFile, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.GetDirectoryName(SandboxConnectionLinuxDetours.LinuxSandboxPTraceMqName)
                },
                EnableRaisingEvents = true
            };

            m_daemonExecutor = new AsyncProcessExecutor
            (
                process,
                TimeSpan.FromMinutes(2), // Timeout is only for after we signal the daemon to exit
                outputBuilder: line => { if (line != null) { Logger.Log.PTraceDaemonVerbose(m_loggingContext, line); } },
                errorBuilder: line =>  { if (line != null) { Logger.Log.PTraceDaemonError(m_loggingContext, line);   } }
            );
        }

        /// <summary>
        /// Starts the daemon.
        /// </summary>
        public void Start()
        {
            m_daemonExecutor.Start();
        }

        /// <summary>
        /// Stops the daemon.
        /// </summary>
        public async Task Stop()
        {
 
            // Send a request for the daemon to stop
            _ = kill(m_daemonExecutor.Process.Id, SIGUSR1);

            // If the daemon does not respond to SIGUSR1 it will automatically timeout in 2 minutes after we call WaitForExitAsync
            await m_daemonExecutor.WaitForExitAsync();
            await m_daemonExecutor.WaitForStdOutAndStdErrAsync();

            if (m_daemonExecutor.Process.ExitCode != 0)
            {
                Logger.Log.PTraceDaemonError(m_loggingContext, $"PTraceDaemon returned bad exit code '{m_daemonExecutor.Process.ExitCode}'");
            }

            m_daemonExecutor.Dispose();
        }
    }
}
