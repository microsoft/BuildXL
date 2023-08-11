// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.IO;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.CopyOnWrite;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Native.IO.FileUtilities;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// FileSystem related native implementations for Windows based systems
    /// </summary>
    internal sealed class FileSystemExtensionsWin : IFileSystemExtensions
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
#if NETCOREAPP
            try
            {
                return GetVolumeFileSystemByHandle(fileHandle) == FileSystemType.ReFS;
            }
            catch (NativeWin32Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }

        public Possible<Unit> CloneFile(string source, string destination, bool followSymlink)
        {
#if NETCOREAPP
            try
            {
                // NoFileIntegrityCheck: Cache does not use Windows file integrity.
                // PathIsFullyResolved: No need for CoW library to do Path.GetFullPath() again, full paths are provided from PathTable.     
                ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
                cow.CloneFile(source, destination, CloneFlags.NoFileIntegrityCheck | CloneFlags.PathIsFullyResolved);
            }
            catch (NotSupportedException ex)
            {
                return new Failure<string>($"CloneFile is not supported: {ex.Message}");
            }
            catch (Win32Exception ex)
            {
                return NativeFailure.CreateFromException(new NativeWin32Exception(ex.NativeErrorCode, ex.Message));
            }

            return Unit.Void;
#else
            return new Failure<string>("CloneFile is not supported in non NETCOREAPP");
#endif
        }

        private static bool SupportsCopyOnWrite()
        {
#if NETCOREAPP
            bool disableCopyOnWrite = string.Equals(Environment.GetEnvironmentVariable("DisableCopyOnWriteWin"), "1", StringComparison.Ordinal);

            if (disableCopyOnWrite)
            {
                return false;
            }

            string workingDir = Environment.CurrentDirectory;
            OpenFileResult directoryOpenResult = TryOpenDirectory(
                workingDir,
                FileShare.ReadWrite | FileShare.Delete,
                out SafeFileHandle directoryHandle);

            if (directoryOpenResult.Succeeded)
            {
                using (directoryHandle)
                {
                    return CheckIfVolumeSupportsCopyOnWriteByHandle(directoryHandle);
                }
            }
#endif  
            return false;
        }
    }
}