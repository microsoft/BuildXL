// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Control mode for running POSIX delete.
    /// </summary>
    public enum PosixDeleteMode
    {
        /// <summary>
        /// Do not run POSIX delete.
        /// </summary>
        NoRun,

        /// <summary>
        /// Run POSIX delete as the first attempt of deleting a file or an empty directory.
        /// </summary>
        /// <remarks>
        /// This mode is the default when building with BuildXL on both Unix and Windows.
        /// Previously, BuildXL downgraded it to RunLast when building on Windows due to bug in RS5. The bug is 2 years old
        /// and now one can configure the mode from the command line if the bug resurfaces.
        /// </remarks>
        RunFirst,

        /// <summary>
        /// Run POSIX delete as the last attempt (fallback) of deleting a file or an empty directory.
        /// </summary>
        RunLast,
    }
}
