// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if !PLATFORM_WIN
using System;
using System.Runtime.InteropServices;
#endif

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Helpers for Unix based file system functionality
    /// </summary>
    public static class UnixHelpers
    {
#if !PLATFORM_WIN
        private static class LibC
        {
            /// <nodoc />
            [DllImport("libc", SetLastError = true)]
            public static extern int chmod(string path, int mode);

            public static readonly int AllFilePermssionMask = Convert.ToInt32("777", 8);
        }
#endif

        /// <nodoc />
        public static void OverrideFileAccessMode(bool changePermissions, string path)
        {
#if !PLATFORM_WIN
            if (changePermissions)
            {
                // Force 0777 on the file at 'path' - this is a temporary hack when placing files as our cache layer
                // currently does not track Unix file access flags when putting / placing files
                LibC.chmod(path, LibC.AllFilePermssionMask);
            }
#endif
        }
    }
}
