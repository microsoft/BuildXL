// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.FileContentTableAccessor
{
    /// <summary>
    /// Accessor used to inspect the keys of <see cref="FileContentTable"/>.
    /// </summary>
    public interface IFileContentTableAccessor : IDisposable
    {
        /// <summary>
        /// Gets a file handle given file identity (file id and volume id).
        /// </summary>
        /// <param name="fileIdAndVolumeId">File identity.</param>
        /// <param name="fileShare">File share mode.</param>
        /// <param name="handle">Resulting handle.</param>
        /// <param name="path">Resulting path.</param>
        /// <returns>True is successful; otherwise false.</returns>
        /// <remarks>
        /// IMPORTANT: Due to hardlinks, a handle can correspond to multiple paths. The implementation of this method is OS specific.
        ///            These paths should be treated specially and should never be used for caching
        /// </remarks>
        bool TryGetFileHandleAndPathFromFileIdAndVolumeId(FileIdAndVolumeId fileIdAndVolumeId, FileShare fileShare, out SafeFileHandle handle, out string path);
    }
}
