// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
