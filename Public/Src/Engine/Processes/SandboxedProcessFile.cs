// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

#pragma warning disable SA1649 // File name must match first type name

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
    public static class SandboxedProcessFileExtenstions
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

        /// <summary>
        /// Get the file artifact that corresponds to stdout, stderr, or a trace file
        /// </summary>
        /// <param name="file">The output stream</param>
        /// <param name="pip">The pip this request is about</param>
        /// <returns>The file artifact</returns>
        public static FileArtifact PipFileArtifact(this SandboxedProcessFile file, Process pip)
        {
            switch (file)
            {
                case SandboxedProcessFile.StandardOutput:
                    return pip.StandardOutput;
                case SandboxedProcessFile.StandardError:
                    return pip.StandardError;
                default:
                    Contract.Assert(file == SandboxedProcessFile.Trace);
                    return pip.TraceFile;
            }
        }
    }
}
