// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Method of handling existing file when expected to create.
    /// </summary>
    public enum FileReplacementMode
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        None = 0,

        /// <summary>
        ///     An error will be raised if a file to be written already exists.
        /// </summary>
        FailIfExists,

        /// <summary>
        ///     If a file to be written already exists, it will be truncated and replaced.
        /// </summary>
        ReplaceExisting,

        /// <summary>
        ///     If a file already exists don't overwrite the file.
        /// </summary>
        /// <remarks>
        ///     Only use this if you absolutely trust the system to not modify the content that is placed.
        /// </remarks>
        SkipIfExists
    }
}
