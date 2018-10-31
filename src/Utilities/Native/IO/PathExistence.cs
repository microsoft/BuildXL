// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Existence of a path as determined by probing for it.
    /// </summary>
    public enum PathExistence : byte
    {
        /// <summary>
        /// The path does not exist (as a file or directory).
        /// </summary>
        Nonexistent,

        /// <summary>
        /// File exists.
        /// </summary>
        ExistsAsFile,

        /// <summary>
        /// Directory exists.
        /// </summary>
        ExistsAsDirectory
    }
}
