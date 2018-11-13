// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    /// <summary>
    /// Access to file names needed by sandboxed process
    /// </summary>
    public interface ISandboxedProcessFileStorage
    {
        /// <summary>
        /// Get file name needed by sandboxed process
        /// </summary>
        /// <remarks>
        /// After the sandboxed process calls this method, it will attempt to create a file with the returned name.
        /// So it better be a valid file name for which the hosting process has the rights to create a file.
        /// </remarks>
        string GetFileName(SandboxedProcessFile file);
    }
}
