// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{    
    /// <summary>
    /// Result of file duplication.
    /// </summary>
    public enum FileDuplicationResult
    {
        /// <summary>
        /// Duplicate file target has existed.
        /// </summary>
        Existed,

        /// <summary>
        /// Duplication is created by hardlinking the file.
        /// </summary>
        Hardlinked,

        /// <summary>
        /// Duplication is created by copying the file.
        /// </summary>
        Copied
    }
}
