// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Creates an instance of <see cref="Configuration"/>.
        /// </summary>
        public Configuration()
        {
            EnableTelemetry = true;
        }

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
