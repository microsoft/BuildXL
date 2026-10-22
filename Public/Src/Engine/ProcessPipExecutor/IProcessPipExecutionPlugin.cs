// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Processes;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Optional plugin hooks for process execution.
    /// </summary>
    public interface IProcessPipExecutionPlugin
    {
        /// <summary>
        /// Gets the message types supported by the loaded plugins.
        /// </summary>
        IReadOnlyCollection<string> LoadedPluginSupportedMessageTypes { get; }

        /// <summary>
        /// Provides the finalized process information to the plugin.
        /// </summary>
        void SetProcessInfo(SandboxedProcessInfo processInfo);

        /// <summary>
        /// Allows the plugin to parse process output before it is logged.
        /// </summary>
        Task<string> ProcessStdOutAndErrorAsync(string message, bool isErrorOutput);

        /// <summary>
        /// Allows the plugin to provide changes to the process result.
        /// </summary>
        Task<ProcessPipExecutionPluginResult> ProcessResultAsync(SandboxedProcessResult result);
    }

    /// <summary>
    /// Changes to apply to a process result after plugin processing.
    /// </summary>
    public sealed class ProcessPipExecutionPluginResult
    {
        /// <summary>
        /// Text to append to standard output.
        /// </summary>
        public string StandardOutToAppend { get; set; }

        /// <summary>
        /// Text to append to standard error.
        /// </summary>
        public string StandardErrToAppend { get; set; }

        /// <summary>
        /// Replacement exit code, if one was provided.
        /// </summary>
        public int? ExitCode { get; set; }
    }
}
