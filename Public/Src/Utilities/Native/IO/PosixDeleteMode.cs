// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// This mode should be used when building with BuildXL on Unix. This mode used to be the default
        /// for building on Windows. However, since a bug in RS5 that causes BuildXL to stop responding on calling POSIX delete,
        /// BuildXL needs to downgrade the applicability of this mode from the first choice to fallback, i.e., <see cref="RunLast"/>.
        /// </remarks>
        RunFirst,

        /// <summary>
        /// Run POSIX delete as the last attempt (fallback) of deleting a file or an empty directory.
        /// </summary>
        RunLast,
    }
}
