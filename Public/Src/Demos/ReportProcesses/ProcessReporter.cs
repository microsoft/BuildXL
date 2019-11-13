// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Demo
{
    /// <summary>
    /// Runs a given process under the sandbox and retrieve a list of process spawned by the execution
    /// </summary>
    public class ProcessReporter : ISandboxedProcessFileStorage
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ProcessReporter()
        {
            m_pathTable = new PathTable();
            m_loggingContext = new LoggingContext(nameof(ProcessReporter));
        }

        /// <summary>
        /// Runs the given process with the given arguments and reports back the list of processes spawned,
        /// which includes the main process and all its children processes
        /// </summary>
        /// <remarks>
        /// Each reported process in the list contains process data, such as start/end time, arguments, CPU counters, etc.
        /// </remarks>
        public IReadOnlyList<ReportedProcess> RunProcessAndReport(string pathToProcess, string arguments)
        {
            var result = RunProcessUnderSandbox(pathToProcess, arguments);
            // The sandbox reports all processes as a list.
            return result.Processes;
        }

        /// <summary>
        /// Runs the given tool with the provided arguments under the BuildXL sandbox and reports the result in a <see cref="SandboxedProcessResult"/>
        /// </summary>
        private SandboxedProcessResult RunProcessUnderSandbox(string pathToProcess, string arguments)
        {
            var info = new SandboxedProcessInfo(
                m_pathTable,
                this,
                pathToProcess,
                CreateManifestToLogProcessData(m_pathTable),
                disableConHostSharing: true,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                loggingContext: m_loggingContext)
            {
                Arguments = arguments,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                PipSemiStableHash = 0,
                PipDescription = "Process list demo",
                SandboxConnection = OperatingSystemHelper.IsUnixOS ? new SandboxConnectionKext() : null
            };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync().GetAwaiter().GetResult();
        }

        /// <nodoc />
        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
        }

        /// <summary>
        /// The manifest is configured so the sandbox monitors all child processes and collect process data.
        /// </summary>
        private static FileAccessManifest CreateManifestToLogProcessData(PathTable pathTable)
        {
            var fileAccessManifest = new FileAccessManifest(pathTable)
            {
                // We don't want to block any accesses
                FailUnexpectedFileAccesses = false,
                // Monitor children processes spawned
                MonitorChildProcesses = true,
                // Optional data about the processes
                LogProcessData = true
            };

            return fileAccessManifest;
        }
    }
}
