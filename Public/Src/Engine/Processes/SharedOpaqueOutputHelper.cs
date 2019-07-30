// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Security.AccessControl;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Utility class for identifying a file as being an output of a shared opaque. This helps the scrubber to be more precautious when deleting files under shared opaques.
    /// </summary>
    public static class SharedOpaqueOutputHelper
    {
        /// <summary>
        /// Flags the given path as being an output under a shared opaque by setting the creation time to <see cref="WellKnownTimestamps.OutputInSharedOpaqueTimestamp"/>
        /// </summary>
        /// <exception cref="BuildXLException">When the timestamp cannot be set</exception>
        public static void SetPathAsSharedOpaqueOutput(string expandedPath)
        {
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

            DateTime creationTime;

            try
            {
                creationTime = FileUtilities.GetFileTimestamps(expandedPath).CreationTime;
            }
            catch (BuildXLException ex)
            {
                throw new BuildXLException(I($"Failed to open output file '{expandedPath}' for reading."), ex);
            }

            return creationTime == WellKnownTimestamps.OutputInSharedOpaqueTimestamp;
        }

        /// <summary>
        /// Makes sure the given path is flagged as being an output under a shared opaque. Flags the file as such if that was not the case.
        /// </summary>
        public static void EnforceFileIsSharedOpaqueOutput(string expandedPath)
        {
            // If the file has the right timestamps already, then there is nothing to do.
            if (IsSharedOpaqueOutput(expandedPath))
            {
                return;
            }

            // In the case of a no replay, this case can happen if the file got into the cache as a static output, but later was made a shared opaque
            // output without a content change.

#if PLATFORM_WIN
            // Make sure we allow for attribute writing first
            var writeAttributesDenied = !FileUtilities.HasWritableAccessControl(expandedPath);
            if (writeAttributesDenied)
            {
                FileUtilities.SetFileAccessControl(expandedPath, FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes, allow: true);
            }
#endif
            try
            {
                SetPathAsSharedOpaqueOutput(expandedPath);
            }
            finally
            {
#if PLATFORM_WIN
                // Restore the attributes as they were originally set
                if (writeAttributesDenied)
                {
                    FileUtilities.SetFileAccessControl(expandedPath, FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes, allow: false);
                }
#endif
            }
        }
    }
}
