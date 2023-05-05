// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Extension for FileUtilities.
    /// </summary>
    public static class FileUtilitiesExtensions
    {
        /// <summary>
        /// A platform specific concrete implementation of the file system layer functions
        /// </summary>
        /// <remarks>
        /// When running on Windows but inside the CoreCLR, we use the same concrete implementation
        /// as the vanilla BuildXL build for Windows and skip Unix implementations completely
        /// </remarks>
        private static readonly IFileSystemExtensions s_fileSystemExtensions = OperatingSystemHelper.IsUnixOS
            ? new FileSystemExtensionsUnix()
            : new FileSystemExtensionsWin();

        /// <see cref="IFileSystemExtensions.IsCopyOnWriteSupportedByEnlistmentVolume"/>
        public static bool IsCopyOnWriteSupportedByEnlistmentVolume
        {
            get => s_fileSystemExtensions.IsCopyOnWriteSupportedByEnlistmentVolume;
            set => s_fileSystemExtensions.IsCopyOnWriteSupportedByEnlistmentVolume = value;
        }

        /// <summary>
        /// Checks if a file system volume supports copy on write.
        /// </summary>
        /// <param name="fileHandle"></param>
        /// <returns>true iff copy on write is supported</returns>
        public static bool CheckIfVolumeSupportsCopyOnWriteByHandle(SafeFileHandle fileHandle) => OperatingSystemHelper.IsUnixOS
            ? FileSystemExtensionsUnix.CheckIfVolumeSupportsCopyOnWriteByHandle(fileHandle)
            : FileSystemExtensionsWin.CheckIfVolumeSupportsCopyOnWriteByHandle(fileHandle);

        /// <summary>
        /// Tries to create copy-on-write by calling <see cref="IFileSystemExtensions.CloneFile(string, string, bool)"/>.
        /// </summary>
        /// <param name="source">Source of copy.</param>
        /// <param name="destination">Destination path.</param>
        /// <param name="followSymlink">Flag indicating whether to follow source symlink or not.</param>
        public static Possible<Unit> TryCreateCopyOnWrite(string source, string destination, bool followSymlink)
        {
            try
            {
                using (FileUtilities.Counters?.StartStopwatch(StorageCounters.CopyOnWriteDuration))
                {
                    FileUtilities.Counters?.IncrementCounter(StorageCounters.CopyOnWriteCount);
                    Possible<Unit> result = s_fileSystemExtensions.CloneFile(source, destination, followSymlink);

                    if (result.Succeeded)
                    {
                        FileUtilities.Counters?.IncrementCounter(StorageCounters.SuccessfulCopyOnWriteCount);
                    }

                    return result;
                }
            }
            catch (NativeWin32Exception ex)
            {
                return NativeFailure.CreateFromException(ex);
            }
        }

        /// <summary>
        /// Tries to duplicate a file.
        /// </summary>
        public static Task<FileDuplicationResult> TryDuplicateOneFileAsync(string sourcePath, string destinationPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourcePath));
            Contract.Requires(!string.IsNullOrWhiteSpace(destinationPath));

            if (string.Compare(sourcePath, destinationPath, OperatingSystemHelper.PathComparison) == 0)
            {
                return Task.FromResult(FileDuplicationResult.Existed); // Nothing to do.
            }

            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (FileUtilities.DirectoryExistsNoFollow(destinationDirectory))
                    {
                        if (FileUtilities.FileExistsNoFollow(destinationPath))
                        {
                            FileUtilities.DeleteFile(destinationPath);
                        }
                    }
                    else
                    {
                        FileUtilities.CreateDirectory(destinationDirectory);
                    }

                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        var hardlinkResult = FileUtilities.OsFileSystem.TryCreateHardLinkViaSetInformationFile(destinationPath, sourcePath);

                        if (hardlinkResult == CreateHardLinkStatus.Success)
                        {
                            return FileDuplicationResult.Hardlinked;
                        }
                    }
                    else
                    {
                        if (IsCopyOnWriteSupportedByEnlistmentVolume)
                        {
                            var possiblyCreateCopyOnWrite = TryCreateCopyOnWrite(sourcePath, destinationPath, followSymlink: false);
                            if (possiblyCreateCopyOnWrite.Succeeded)
                            {
                                return FileDuplicationResult.Copied;
                            }
                        }
                    }

                    await FileUtilities.CopyFileAsync(sourcePath, destinationPath);
                    return FileDuplicationResult.Copied;
                },
                ex => { throw new BuildXLException(ex.Message); });
        }
    }
}