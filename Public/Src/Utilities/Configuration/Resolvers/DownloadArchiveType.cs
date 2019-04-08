// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Nuget package definition
    /// </summary>
    public enum DownloadArchiveType
    {
        /// <summary>
        /// This is just a single file, no action needs to be taken
        /// </summary>
        File = 0,

        /// <summary>
        /// The file is a zip archive and needs to be extracted
        /// </summary>
        Zip = 1,

        /// <summary>
        /// The file is a tar/gzip archive and needs to be uncompressed and then expanded
        /// </summary>
        Gzip = 2,

        /// <summary>
        /// The file is a tar/gzip archive and needs to be uncompressed and then expanded
        /// </summary>
        Tgz= 3,

        /// <summary>
        /// Tape Archive
        /// </summary>
        Tar=4,
    }
}
