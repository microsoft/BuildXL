// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Diagnostic messages for FileUtilities
    /// </summary>
    public static class FileUtilitiesMessages
    {
        /// <nodoc />
        public const string FileDeleteFailed = "Deleting a file failed: ";

        /// <nodoc />
        public const string DeleteDirectoryContentsFailed =
            "Delete directory contents failed after exhausting retries because some files or directories " +
            "still existed in the directory. This could be because an external process has a handle open to a file or directory. " +
            "Directory: ";

        /// <nodoc />
        public const string ActiveHandleUsage = "Active handle usage for file: ";

        /// <nodoc />
        public const string NoProcessesUsingHandle = "Did not find any actively running processes using the handle.";

        /// <nodoc />
        public const string PathMayBePendingDeletion = "Path may be in Windows pending deletion state.";
    }
}
