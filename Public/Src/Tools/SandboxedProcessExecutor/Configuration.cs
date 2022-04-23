// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.SandboxedProcessExecutor
{
    /// <summary>
    /// Configuration.
    /// </summary>
    internal sealed class Configuration
    {
        /// <summary>
        /// Path to sandboxed process info input file.
        /// </summary>
        public string SandboxedProcessInfoInputFile { get; set; }

        /// <summary>
        /// Path to sandboxed process result output file.
        /// </summary>
        public string SandboxedProcessResultOutputFile { get; set; }

        /// <summary>
        /// Enables telemetry.
        /// </summary>
        public bool EnableTelemetry { get; set; }

        /// <summary>
        /// Prints observed file/directory accesses to standard output.
        /// </summary>
        /// <remarks>
        /// The observedFileAccesses flag in SandboxedProcessInfo needs to be enabled to show the observed accesses.
        /// </remarks>
        public bool PrintObservedAccesses { get; set; }

        /// <summary>
        /// Path to sandboxed process test hooks file.
        /// </summary>
        public string SandboxedProcessExecutorTestHookFile { get; set; }

        /// <summary>
        /// Path to file containing sideband data for process remoting.
        /// </summary>
        public string RemoteSandboxedProcessDataFile { get; set; }

        /// <summary>
        /// Validates configuration.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(SandboxedProcessInfoInputFile))
            {
                errorMessage = "Missing sandboxed process info file";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SandboxedProcessResultOutputFile))
            {
                errorMessage = "Missing sandboxed process result file";
                return false;
            }

            return true;
        }
    }
}
