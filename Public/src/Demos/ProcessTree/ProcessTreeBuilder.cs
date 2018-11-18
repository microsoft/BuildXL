// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Demo
{
    /// <summary>
    /// Runs a given process under the sandbox and builds a (historical) process tree based on the execution
    /// </summary>
    public class ProcessTreeBuilder
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ProcessTreeBuilder()
        {
            m_pathTable = new PathTable();
            m_loggingContext = new LoggingContext(nameof(ProcessTreeBuilder));
        }

        /// <summary>
        /// Runs the given process with the given arguments and reports back the process tree, which includes
        /// the main process and all its children processes
        /// </summary>
        /// <remarks>
        /// Each reported process in the tree contains process data, such as start/end time, arguments, CPU counters, etc.
        /// </remarks>
        public ProcessTree RunProcessAndReportTree(string pathToProcess, string arguments)
        {
            var result = RunProcessUnderSandbox(pathToProcess, arguments);
            // The sandbox reports all processes as a list. Let's make them a tree for better visualization.
            return ComputeTree(result.Processes);
        }

        private ProcessTree ComputeTree(IReadOnlyCollection<ReportedProcess> reportedProcesses)
        {
            // Process Ids can be reused, but let's assume uniqueness, just for demo purposes
            var allProcessNodes = reportedProcesses.ToDictionary(proc => proc.ProcessId, proc => new ProcessNode(proc));

            ProcessNode root = null;
            foreach (var reportedProcess in reportedProcesses)
            {
                // If the parent id is part of the reported processes, update the children. Otherwise, that should be the root process
                if (allProcessNodes.TryGetValue(reportedProcess.ParentProcessId, out ProcessNode parentProcessNode))
                {
                    parentProcessNode.AddChildren(allProcessNodes[reportedProcess.ProcessId]);
                }
                else if (reportedProcess.ParentProcessId == 0)
                {
                    // This case means that the parent process id could not be retrieved. Just ignore the process for sake of the demo
                    continue;
                }
                else
                {
                    // There should be only one process that has a parent id that is not part of the set of reported processes: the main one
                    root = allProcessNodes[reportedProcess.ProcessId];
                }
            }

            // Seal all nodes so children become immutable
            foreach (var processNode in allProcessNodes.Values)
            {
                processNode.Seal();
            }

            Contract.Assert(root != null);

            return new ProcessTree(root);
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
