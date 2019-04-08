// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Native.IO;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage.FileContentTableAccessor
{
    /// <summary>
    /// Implementation of <see cref="IFileContentTableAccessor"/> for Unix.
    /// </summary>
    internal class FileContentTableAccessorUnix : IFileContentTableAccessor
    {
        /// <inheritdoc />
        public void Dispose()
        {
        }
        
        /// <inheritdoc />
        public bool TryGetFileHandleAndPathFromFileIdAndVolumeId(FileIdAndVolumeId fileIdAndVolumeId, FileShare fileShare, out SafeFileHandle handle, out string path)
        {
            // Accessing hidden files in MacOs.
            // Ref: http://www.westwind.com/reference/os-x/invisibles.html
            // Another alternative is using fcntl with F_GETPATH that takes a handle and returns the concrete OS path owned by the handle.
            path = I($"/.vol/{fileIdAndVolumeId.VolumeSerialNumber}/{fileIdAndVolumeId.FileId.Low}");

            OpenFileResult result = FileUtilities.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.GenericRead,
                fileShare,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out handle);

            return result.Succeeded;
        }
    }
}
