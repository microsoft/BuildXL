// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Unix.IO;
using static BuildXL.Native.IO.FileUtilities;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// FileSystem related native implementations for Unix based systems
    /// </summary>
    internal sealed class FileSystemExtensionsUnix : IFileSystemExtensions
    {
        private Lazy<bool> m_supportCopyOnWrite = new Lazy<bool>(() => SupportsCopyOnWrite());

        /// <inheritdoc />
        public bool IsCopyOnWriteSupportedByEnlistmentVolume
        {
            get => m_supportCopyOnWrite.Value;
            set => m_supportCopyOnWrite = new Lazy<bool>(() => value);
        }

        /// <inheritdoc />
        public static bool CheckIfVolumeSupportsCopyOnWriteByHandle(SafeFileHandle fileHandle)
        {
            try
            {
                return GetVolumeFileSystemByHandle(fileHandle) == FileSystemType.APFS;
            }
            catch (NativeWin32Exception)
            {
                return false;
            }
        }

        public Possible<Unit> CloneFile(string source, string destination, bool followSymlink)
        {
            var flags = followSymlink ? CloneFileFlags.CLONE_NONE : CloneFileFlags.CLONE_NOFOLLOW;
            int result = Interop.Unix.IO.CloneFile(source, destination, flags);
            if (result != 0)
            {
                return new NativeFailure(Marshal.GetLastWin32Error(), I($"Failed to clone '{source}' to '{destination}'"));
            }

            return Unit.Void;
        }

        private static bool SupportsCopyOnWrite()
        {
            // Use temp file name as an approximation whether file system supports copy-on-write.
            string path = FileUtilities.GetTempFileName();
            bool result = false;

            using (var fileStream = CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, FileOptions.None, false))
            {
                result = CheckIfVolumeSupportsCopyOnWriteByHandle(fileStream.SafeFileHandle);
            }

            File.Delete(path);
            return result;
        }
    }
}