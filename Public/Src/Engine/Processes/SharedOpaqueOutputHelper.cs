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
                if (writeAttributesDenied)
                {
                    FileUtilities.SetFileAccessControl(expandedPath, FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes, allow: true);
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
                        FileUtilities.SetFileAccessControl(expandedPath, FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes, allow: false);
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
            private const string MY_XATTR_NAME = "com.microsoft.buildxl:shared_opaque_output";
            private const long MY_XATTR_VALUE = 42;

            private const int XATTR_NOFOLLOW = 1;

            [DllImport("libc", EntryPoint = "setxattr")]
            private static extern int SetXattr(
                [MarshalAs(UnmanagedType.LPStr)] string path,
                [MarshalAs(UnmanagedType.LPStr)] string name,
                void *value,
                ulong size,
                uint position,
                int options);

            [DllImport("libc", EntryPoint = "getxattr")]
            private static extern long GetXattr(
                [MarshalAs(UnmanagedType.LPStr)] string path,
                [MarshalAs(UnmanagedType.LPStr)] string name,
                void* value,
                ulong size,
                uint position,
                int options);

            /// <summary>
            /// Flags the given path as being an output under a shared opaque by setting
            /// <see cref="MY_XATTR_NAME"/> xattr to a <see cref="MY_XATTR_VALUE"/>.
            /// </summary>
            public static void SetPathAsSharedOpaqueOutput(string expandedPath)
            {
                long value = MY_XATTR_VALUE;
                var err = SetXattr(expandedPath, MY_XATTR_NAME, &value, sizeof(long), 0, XATTR_NOFOLLOW);
                if (err != 0)
                {
                    throw new BuildXLException(I($"Failed to set '{MY_XATTR_NAME}' extended attribute. Error: {err}"));
                }
            }

            /// <summary>
            /// Checks if the given path is an output under a shared opaque by checking if
            /// it contains extended attribute by <see cref="MY_XATTR_NAME"/> name.
            /// </summary>
            public static bool IsSharedOpaqueOutput(string expandedPath)
            {
                long value = 0;
                uint valueSize = sizeof(long);
                var resultSize = GetXattr(expandedPath, MY_XATTR_NAME, &value, valueSize, 0, XATTR_NOFOLLOW);
                return resultSize == valueSize && value == MY_XATTR_VALUE;
            }
        }

        /// <summary>
        /// Marks a given path as "shared opaque output"
        /// </summary>
        /// <exception cref="BuildXLException">When unsuccessful</exception>
        public static void SetPathAsSharedOpaqueOutput(string expandedPath)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                Unix.SetPathAsSharedOpaqueOutput(expandedPath);
            }
            else
            {
                Win.SetPathAsSharedOpaqueOutput(expandedPath);
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
            bool followSymlink = true;

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
