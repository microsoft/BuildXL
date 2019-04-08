// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.FileContentTableAccessor
{
    /// <summary>
    /// Windows implementation of <see cref="IFileContentTableAccessor"/>.
    /// </summary>
    internal class FileContentTableAccessorWin : IFileContentTableAccessor
    {
        private readonly VolumeMap m_volumeMap;
        private readonly FileAccessor m_fileAccessor;

        /// <summary>
        /// Creates an instance of <see cref="FileContentTableAccessorWin"/>
        /// </summary>
        /// <param name="volumeMap">Volume map.</param>
        public FileContentTableAccessorWin(VolumeMap volumeMap)
        {
            Contract.Requires(volumeMap != null);

            m_volumeMap = volumeMap;
            m_fileAccessor = m_volumeMap.CreateFileAccessor();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_fileAccessor.Dispose();
        }

        /// <inheritdoc />
        public bool TryGetFileHandleAndPathFromFileIdAndVolumeId(FileIdAndVolumeId fileIdAndVolumeId, FileShare fileShare, out SafeFileHandle handle, out string path)
        {
            path = null;

            FileAccessor.OpenFileByIdResult openResult = m_fileAccessor.TryOpenFileById(
                fileIdAndVolumeId.VolumeSerialNumber,
                fileIdAndVolumeId.FileId,
                FileDesiredAccess.GenericRead,
                fileShare,
                FileFlagsAndAttributes.None,
                out handle);

            switch (openResult)
            {
                case FileAccessor.OpenFileByIdResult.FailedToOpenVolume:
                case FileAccessor.OpenFileByIdResult.FailedToFindFile:
                case FileAccessor.OpenFileByIdResult.FailedToAccessExistentFile:
                    Contract.Assert(handle == null);
                    return false;
                case FileAccessor.OpenFileByIdResult.Succeeded:
                    Contract.Assert(handle != null && !handle.IsInvalid);
                    path = FileUtilities.GetFinalPathNameByHandle(handle, volumeGuidPath: false);
                    return true;
                default:
                    throw Contract.AssertFailure("Unhandled OpenFileByIdResult");
            }
        }
    }
}
