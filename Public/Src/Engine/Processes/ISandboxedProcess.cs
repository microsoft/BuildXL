// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Interface for <see cref="SandboxedProcessPipExecutor"/> to use to start/kill/manage processes.
    /// 
    /// To create an instance of this interface and start executing the created process use 
    /// <see cref="SandboxedProcessFactory.StartAsync"/>.
    /// </summary>
    public interface ISandboxedProcess : IDisposable
    {
        /// <summary>
        /// Gets the unique identifier for the associated process.
        /// </summary>
        int ProcessId { get; }

        /// <summary>
        /// Returns the string representation of the accessed file path.
        /// </summary>
        /// <param name="reportedFileAccess">The file access object on which to get the path location.</param>
        string GetAccessedFileName(ReportedFileAccess reportedFileAccess);
        
        /// <summary>
        /// Gets the peak memory usage while the process is active. If the process exits, the peak memory usage is considered null.
        /// </summary>
        ulong? GetActivePeakMemoryUsage();

        /// <summary>
        /// Gets the maximum heap size of the sandboxed process.
        /// </summary>
        long GetDetoursMaxHeapSize();

        /// <summary>
        /// Gets the difference between sent and received messages from sandboxed process tree to BuildXL.  If no sandboxing is used the return value is 0.
        /// </summary>
        int GetLastMessageCount();

        /// <summary>
        /// Asynchronously starts the process.  All the required process start information must be provided prior to calling this method
        /// (e.g., via the constructor or a custom initialization method).  To wait for the process to finish, call <see cref="GetResultAsync"/>.
        /// To instantly kill the process, call <see cref="KillAsync"/>.
        /// </summary>
        void Start();

        /// <summary>
        /// Waits for the process to finish and returns the result of the process execution.
        /// </summary>
        Task<SandboxedProcessResult> GetResultAsync();

        /// <summary>
        /// Kills the process; only produces result after process has terminated.
        /// </summary>
        /// <remarks>
        /// Also kills all nested processes; if the process hasn't already finished by itself, the Result task gets canceled.
        /// </remarks>
        Task KillAsync();
    }
}
