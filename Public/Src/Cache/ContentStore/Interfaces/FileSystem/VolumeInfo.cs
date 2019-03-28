// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Information about a storage volume.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct VolumeInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="VolumeInfo"/> struct.
        /// </summary>
        public VolumeInfo(long size, long freeSpace)
        {
            Size = size;
            FreeSpace = freeSpace;
        }

        /// <summary>
        ///     Gets total size of storage space, in bytes.
        /// </summary>
        public long Size { get; }

        /// <summary>
        ///     Gets amount of free space, in bytes.
        /// </summary>
        public long FreeSpace { get; }
    }
}
