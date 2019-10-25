// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Utility class for identifying a file as being an output of a shared opaque.
    /// This helps the scrubber to be more precautious when deleting files under shared opaques.
    /// </summary>
    /// <remarks>
    /// Currently, we use two different strategies when running on Windows and non-Windows platforms
    ///   - on Windows, we set a magic timestamp as file's creation (birth) date
    ///   - on Mac, we set an extended attribute with a special name.
    /// On Mac, some tools tend to change the timestamps (even the birth date) which is the reason for this.
    ///
    /// TODO: eventually we should unify this and use the same mechanism for all platforms, if possible.
    /// </remarks>
    public static class SharedOpaqueOutputHelper
    {
        private static class Win
        {
            /// <summary>
            /// Flags the given path as being an output under a shared opaque by setting the creation time to 
            /// <see cref="WellKnownTimestamps.OutputInSharedOpaqueTimestamp"/>.
            /// </summary>
            /// <exception cref="BuildXLException">When the timestamp cannot be set</exception>
            public static void SetPathAsSharedOpaqueOutput(string expandedPath)
            {
                // In the case of a no replay, this case can happen if the file got into the cache as a static output,
                // but later was made a shared opaque output without a content change.
                // Make sure we allow for attribute writing first
                var writeAttributesDenied = !FileUtilities.HasWritableAttributeAccessControl(expandedPath);
                var writeAttrs = FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes;
                if (writeAttributesDenied)
                {
                    FileUtilities.SetFileAccessControl(expandedPath, writeAttrs, allow: true);
                }

                try
                {
                    // Only the creation time is used to identify a file as the output of a shared opaque
                    FileUtilities.SetFileTimestamps(expandedPath, new FileTimestamps(WellKnownTimestamps.OutputInSharedOpaqueTimestamp));
                }
                catch (BuildXLException ex)
                {
                    // On (unsafe) double writes, a race can occur, we give the other contender a second chance
                    if (IsSharedOpaqueOutput(expandedPath))
                    {
                        return;
                    }

                    // Since these files should be just created outputs, this shouldn't happen and we bail out hard.
                    throw new BuildXLException(I($"Failed to open output file '{expandedPath}' for writing."), ex);
                }
                finally
                {
                    // Restore the attributes as they were originally set
                    if (writeAttributesDenied)
                    {
                        FileUtilities.SetFileAccessControl(expandedPath, writeAttrs, allow: false);
                    }
                }
            }

            /// <summary>
            /// Checks if the given path is an output under a shared opaque by verifying whether <see cref="WellKnownTimestamps.OutputInSharedOpaqueTimestamp"/> is the creation time of the file
            /// </summary>
            /// <remarks>
            /// If the given path is a directory, it is always considered part of a shared opaque
            /// </remarks>
            public static bool IsSharedOpaqueOutput(string expandedPath)
            {
                try
                {
                    var creationTime = FileUtilities.GetFileTimestamps(expandedPath).CreationTime;
                    return creationTime == WellKnownTimestamps.OutputInSharedOpaqueTimestamp;
                }
                catch (BuildXLException ex)
                {
                    throw new BuildXLException(I($"Failed to open output file '{expandedPath}' for reading."), ex);
                }
            }
        }

        private static unsafe class Unix
        {
            private const string BXL_SHARED_OPAQUE_XATTR_NAME = "com.microsoft.buildxl:shared_opaque_output";

            // arbitrary value; in the future, we could store something more useful here (e.g., the producer PipId or something)
            private const long BXL_SHARED_OPAQUE_XATTR_VALUE = 42;

            // from xattr.h:
            // #define XATTR_NOFOLLOW   0x0001     /* Don't follow symbolic links */
            private const int XATTR_NOFOLLOW = 1;

            [DllImport("libc", EntryPoint = "setxattr", SetLastError = true)]
            private static extern int SetXattr(
                [MarshalAs(UnmanagedType.LPStr)] string path,
                [MarshalAs(UnmanagedType.LPStr)] string name,
                void *value,
                ulong size,
                uint position,
                int options);

            [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
            private static extern long GetXattr(
                [MarshalAs(UnmanagedType.LPStr)] string path,
                [MarshalAs(UnmanagedType.LPStr)] string name,
                void *value,
                ulong size,
                uint position,
                int options);

            private const Interop.MacOS.IO.FilePermissions S_IWUSR = Interop.MacOS.IO.FilePermissions.S_IWUSR;

            /// <summary>
            /// Flags the given path as being an output under a shared opaque by setting
            /// <see cref="BXL_SHARED_OPAQUE_XATTR_NAME"/> xattr to a <see cref="BXL_SHARED_OPAQUE_XATTR_VALUE"/>.
            /// </summary>
            public static void SetPathAsSharedOpaqueOutput(string expandedPath)
            {
                bool followSymlink = false;
                var currentMode = (Interop.MacOS.IO.FilePermissions)Interop.MacOS.IO.GetFilePermissionsForFilePath(expandedPath, followSymlink);
                bool isWritableByUser = (currentMode & S_IWUSR) != 0;

                // set u+w if not already set
                if (!isWritableByUser)
                {
                    Interop.MacOS.IO.SetFilePermissionsForFilePath(expandedPath, currentMode | S_IWUSR, followSymlink);
                }

                // set xattr
                long value = BXL_SHARED_OPAQUE_XATTR_VALUE;
                var err = SetXattr(expandedPath, BXL_SHARED_OPAQUE_XATTR_NAME, &value, sizeof(long), 0, XATTR_NOFOLLOW);
                var xattrErrorCode = err != 0 ? Marshal.GetLastWin32Error() : 0;

                // reset permissions if we changed them
                if (!isWritableByUser)
                {
                    Interop.MacOS.IO.SetFilePermissionsForFilePath(expandedPath, currentMode, followSymlink);
                }

                // throw if neither SetXattr succeeded nor the path is properly marked
                if (xattrErrorCode != 0 && !IsSharedOpaqueOutputWithFallback(expandedPath, checkFallback: false))
                {
                    throw new BuildXLException(I($"Failed to set '{BXL_SHARED_OPAQUE_XATTR_NAME}' extended attribute for file '{expandedPath}'. Error: {xattrErrorCode}."));
                }
            }

            /// <summary>
            /// Checks if the given path is an output under a shared opaque by checking if
            /// it contains extended attribute by <see cref="BXL_SHARED_OPAQUE_XATTR_NAME"/> name.
            /// </summary>
            public static bool IsSharedOpaqueOutput(string expandedPath) => IsSharedOpaqueOutputWithFallback(expandedPath, checkFallback: true);

            // TODO: delete the fallback logic after a successful transition from old to new logic
            private static bool IsSharedOpaqueOutputWithFallback(string expandedPath, bool checkFallback)
            {
                long value = 0;
                uint valueSize = sizeof(long);
                var resultSize = GetXattr(expandedPath, BXL_SHARED_OPAQUE_XATTR_NAME, &value, valueSize, 0, XATTR_NOFOLLOW);
                if (resultSize == valueSize && value == BXL_SHARED_OPAQUE_XATTR_VALUE)
                {
                    return true;
                }

                if (checkFallback && FileUtilities.GetFileTimestamps(expandedPath).CreationTime == WellKnownTimestamps.OutputInSharedOpaqueTimestamp)
                {
                    return true;
                }

                return false;
            }
        }

        private const int MaxNumberOfAttemptsForMarkingSharedOpaqueOutputs = 3;
        private static readonly TimeSpan SleepDurationBetweenMarkingAttempts = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Marks a given path as "shared opaque output"
        /// </summary>
        /// <remarks>
        /// Retries are needed because: (Win|Unix).SetPathAsSharedOpaqueOutput does something like
        ///   - check if file has write access rights
        ///   - if it doesn't, give it those rights
        ///   - mark the file as shared opaque output
        ///   - ...
        /// There is a race between checking and setting access rights, so it is possible that
        /// here we check its rights, we see that it has the correct rights, then someone else 
        /// (e.g., the cache) revokes those rights, and so we fail to mark the file as shared opaque output.
        /// </remarks>
        /// <exception cref="BuildXLException">When unsuccessful</exception>
        public static void SetPathAsSharedOpaqueOutput(string expandedPath)
        {
            int attempt = 0;
            while (true)
            {
                attempt += 1;

                // wait a bit between attempts
                if (attempt > 1)
                {
                    System.Threading.Thread.Sleep(SleepDurationBetweenMarkingAttempts);
                }

                try
                {
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        Unix.SetPathAsSharedOpaqueOutput(expandedPath);
                    }
                    else
                    {
                        Win.SetPathAsSharedOpaqueOutput(expandedPath);
                    }

                    return;
                } 
                catch (BuildXLException e)
                {
                    if (attempt >= MaxNumberOfAttemptsForMarkingSharedOpaqueOutputs)
                    {
                        throw new BuildXLException($"Exceeded max number of attempts ({MaxNumberOfAttemptsForMarkingSharedOpaqueOutputs}) to mark '{expandedPath}' as shared opaque output", e);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the given path is an output under a shared opaque by verifying whether <see cref="WellKnownTimestamps.OutputInSharedOpaqueTimestamp"/> is the creation time of the file
        /// </summary>
        /// <remarks>
        /// If the given path is a directory, it is always considered part of a shared opaque
        /// </remarks>
        public static bool IsSharedOpaqueOutput(string expandedPath)
        {
            // On Windows: FOLLOW symlinks
            //   - directory symlinks are not fully supported (e.g., producing directory symlinks)
            //   - other reparse points are at play (e.g., junctions, Helium containers) which necessitate 
            //     that we transparently follow those reparse points
            // On non-Windows: NO_FOLLOW symlinks
            //   - directory symlinks are supported
            //   - all symlinks are treated uniformly as files
            //   - if 'expandedPath' is a symlink pointing to a non-existent file, 'expandedPath' should hence
            //     still be treated as an existent file
            //   - similarly, if 'expandedPath' is a symlink pointing to a directory, it should still be treated as a file
            bool followSymlink = !OperatingSystemHelper.IsUnixOS;

            // If the file is not there (the check may be happening against a reported write that later got deleted)
            // then there is nothing to do
            var maybeResult = FileUtilities.TryProbePathExistence(expandedPath, followSymlink);
            if (maybeResult.Succeeded && maybeResult.Result == PathExistence.Nonexistent)
            {
                return true;
            }

            // We don't really track directories as part of shared opaques.
            // So we consider them all potential members and return true.
            // It is important to track directory symlinks, because they are considered files for sake of shared opaque scrubbing.
            if (maybeResult.Succeeded && maybeResult.Result == PathExistence.ExistsAsDirectory)
            {
                return true;
            }

            return OperatingSystemHelper.IsUnixOS
                ? Unix.IsSharedOpaqueOutput(expandedPath)
                : Win.IsSharedOpaqueOutput(expandedPath);
        }

        /// <summary>
        /// Makes sure the given path is flagged as being an output under a shared opaque. Flags the file as such if that was not the case.
        /// </summary>
        public static void EnforceFileIsSharedOpaqueOutput(string expandedPath)
        {
            // If the file is already marked, then there is nothing to do.
            if (IsSharedOpaqueOutput(expandedPath))
            {
                return;
            }

            SetPathAsSharedOpaqueOutput(expandedPath);
        }
    }
}
