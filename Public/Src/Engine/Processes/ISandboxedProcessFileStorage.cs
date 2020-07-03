// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes
{
    /// <summary>
    /// Access to file names needed by sandboxed process stdout and stderr redirection.
    /// </summary>
    public interface ISandboxedProcessFileStorage
    {
        /// <summary>
        /// Get a file path for a sandboxed process's stdout or stderr stream to copy into.
        /// </summary>
        /// <remarks>
        /// After the sandboxed process calls this method, it will attempt to create a file with the returned name.
        /// So it better be a valid file name for which the hosting process has the rights to create a file.
        /// </remarks>
        string GetFileName(SandboxedProcessFile file);
    }
}
