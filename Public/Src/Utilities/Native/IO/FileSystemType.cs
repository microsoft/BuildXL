// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// File system type used on a volume.
    /// </summary>
    public enum FileSystemType
    {
        #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        NTFS,
        ReFS,
        APFS,
        HFS,
        EXT3,
        EXT4,
        Unknown,
        #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}
