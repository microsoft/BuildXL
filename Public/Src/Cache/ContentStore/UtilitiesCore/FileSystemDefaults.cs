// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <summary>
    /// A set of globally available settings for configuring file IO behavior.
    /// </summary>
    public static class FileSystemDefaults
    {
        /// <summary>
        ///     Size of the buffer for FileStreams opened by this class
        /// </summary>
        public const int DefaultFileStreamBufferSize = 4 * 1024;
    }
}
