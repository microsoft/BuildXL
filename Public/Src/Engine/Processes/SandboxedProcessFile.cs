// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Processes
{
    /// <summary>
    /// Files potentially created by a sandboxed process
    /// </summary>
    public enum SandboxedProcessFile
    {
        /// <summary>
        /// The standard output
        /// </summary>
        StandardOutput,

        /// <summary>
        /// The standard error
        /// </summary>
        StandardError,

        /// <summary>
        /// Trace file (sandbox observations).
        /// </summary>
        Trace,
    }

    /// <summary>
    /// Logic to provide default file locations for stdout, stderr, and a trace file
    /// </summary>
    public static class SandboxedProcessFileExtensions
    {
        /// <summary>
        /// Gets the default file name for stdout or stderr redirection.
        /// </summary>
        /// <param name="file">The output stream</param>
        /// <returns>The resulting default file name</returns>
        public static string DefaultFileName(this SandboxedProcessFile file)
        {
            switch (file)
            {
                case SandboxedProcessFile.StandardOutput:
                    return "out.txt";
                case SandboxedProcessFile.StandardError:
                    return "err.txt";
                default:
                    Contract.Assert(file == SandboxedProcessFile.Trace);
                    return "trace.txt";
            }
        }
    }
}
