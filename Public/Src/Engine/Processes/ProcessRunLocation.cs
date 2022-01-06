// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes
{
    /// <summary>
    /// Location where the process will run.
    /// </summary>
    public enum ProcessRunLocation
    {
        /// <summary>
        /// Use the default configuration; most likely local (see <see cref="Local"/>).
        /// </summary>
        Default,

        /// <summary>
        /// Process is forced to run locally on the same machine.
        /// </summary>
        /// <remarks>
        /// The process can be run as an immediate child process of BuildXL, i.e., it is executed directly by <see cref="SandboxedProcessPipExecutor"/>.
        /// Or, the process can be run using the external sandboxed process executor, but still on the same machine.
        /// Or, the process can be run in the VM hosted by the machine.
        /// </remarks>
        Local,

        /// <summary>
        /// Process is expected to run on a remote agent via AnyBuild.
        /// </summary>
        Remote,
    }
}
