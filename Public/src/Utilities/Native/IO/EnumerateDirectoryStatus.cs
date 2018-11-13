// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Status of attempting to enumerate a directory.
    /// </summary>
    public enum EnumerateDirectoryStatus
    {
        /// <summary>
        /// Enumeration of an existent directory succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// One or more path components did not exist, so the search directory could not be opened.
        /// </summary>
        SearchDirectoryNotFound,

        /// <summary>
        /// A path component in the search path refers to a file. Only directories can be enumerated.
        /// </summary>
        CannotEnumerateFile,

        /// <summary>
        /// Directory enumeration could not complete due to denied access to the search directory or a file inside.
        /// </summary>
        AccessDenied,

        /// <summary>
        /// Directory enumeration failed without a well-known status (see <see cref="EnumerateDirectoryResult.NativeErrorCode"/>).
        /// </summary>
        UnknownError,
    }
}
