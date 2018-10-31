// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

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
    }

    /// <summary>
    /// Logic to provide default file locations for stdout and stderr
    /// </summary>
    public static class SandboxedProcessFileExtenstions
    {
        /// <summary>
        /// The default file name
        /// </summary>
        /// <param name="file">The output stream</param>
        /// <returns>The resulting default file name</returns>
        public static string DefaultFileName(this SandboxedProcessFile file)
        {
            switch (file)
            {
                case SandboxedProcessFile.StandardOutput:
                    return "out.txt";
                default:
                    Contract.Assert(file == SandboxedProcessFile.StandardError);
                    return "err.txt";
            }
        }

        /// <summary>
        /// Get the file artifact that corresponds to stdout or stderr
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
                default:
                    Contract.Assert(file == SandboxedProcessFile.StandardError);
                    return pip.StandardError;
            }
        }
    }
}
