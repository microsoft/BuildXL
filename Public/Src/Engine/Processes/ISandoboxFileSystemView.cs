// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// File system view exposed to <see cref="SandboxedProcessPipExecutor"/>
    /// </summary>
    public interface ISandboxFileSystemView
    {
        /// <summary>
        /// Reports that a given directory was created by a pip in the output file system
        /// </summary>
        void ReportOutputFileSystemDirectoryCreated(AbsolutePath path);
    }
}
