// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Logic to provide default file locations for stdout, stderr, and a trace file
    /// </summary>
    public static class SandboxedProcessPipExecutorFileExtensions
    {
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
