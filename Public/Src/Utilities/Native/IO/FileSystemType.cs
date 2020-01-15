// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Apple file system.
        /// </summary>
        APFS,

        /// <summary>
        /// HFS or HFS plus file system.
        /// </summary>
        HFS,

        /// <summary>
        /// Anything other than ReFS or NTFS
        /// </summary>
        Unknown,
    }
}
