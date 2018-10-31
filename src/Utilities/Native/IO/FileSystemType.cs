// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// File system type used on a volume.
    /// </summary>
    public enum FileSystemType
    {
        /// <summary>
        /// NTFS
        /// </summary>
        NTFS,

        /// <summary>
        /// ReFS (Windows 8.1+ / Server 2012R2+)
        /// </summary>
        ReFS,

        /// <summary>
        /// Anything other than ReFS or NTFS
        /// </summary>
        Unknown,
    }
}
