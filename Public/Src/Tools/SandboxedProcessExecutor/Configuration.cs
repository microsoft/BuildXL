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
