// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Demo
{
    /// <summary>
    /// Runs a given process under the sandbox and builds a (historical) process tree based on the execution
    /// </summary>
    public class SandboxedProcessReporter
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public SandboxedProcessReporter()
        {
            m_pathTable = new PathTable();
            m_loggingContext = new LoggingContext(nameof(SandboxedProcessReporter));
        }

        /// <summary>
        /// Runs the given process with the given arguments and reports back the process tree, which includes
        /// the main process and all its children processes
        /// </summary>
        /// <remarks>
        /// Each reported process in the tree contains process data, such as start/end time, arguments, CPU counters, etc.
        /// </remarks>
        public IReadOnlyList<ReportedProcess> RunProcessAndReportTree(string pathToProcess, string arguments)
        {
            var result = RunProcessUnderSandbox(pathToProcess, arguments);
            // The sandbox reports all processes as a list. Let's make them a tree for better visualization.
            return result.Processes;
        }

        /// <summary>
        /// Runs the given tool with the provided arguments under the BuildXL sandbox and reports the result in a <see cref="SandboxedProcessResult"/>
        /// </summary>
        private SandboxedProcessResult RunProcessUnderSandbox(string pathToProcess, string arguments)
        {
            var workingDirectory = Directory.GetCurrentDirectory();

            var info =
                    new SandboxedProcessInfo(
                        m_pathTable,
                        new SimpleSandboxedProcessFileStorage(workingDirectory), 
                        pathToProcess,
                        CreateManifestToLogProcessData(m_pathTable),
                        disableConHostSharing: true,
                        loggingContext: m_loggingContext)
                    {
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory,
                        PipSemiStableHash = 0,
                        PipDescription = "Process tree demo",
                        SandboxedKextConnection = OperatingSystemHelper.IsUnixOS ? new SandboxedKextConnection(numberOfKextConnections: 2) : null
                    };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync().GetAwaiter().GetResult();
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
                // We are particularly interested in monitoring children, since we are after the process tree
                MonitorChildProcesses = true,
                // Let's turn on process data collection, so we can report a richer tree
                LogProcessData = true
            };

            return fileAccessManifest;
        }
    }
}
