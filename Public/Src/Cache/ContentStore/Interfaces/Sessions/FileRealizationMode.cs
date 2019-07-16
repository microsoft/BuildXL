// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Method for realizing a file to disk.
    /// </summary>
    public enum FileRealizationMode : byte
    {
        /// <summary>
        ///     File realization not allowed at all.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Store can pick any mode.
        /// </summary>
        Any,

        /// <summary>
        ///     Copy the file to the destination location, implies copy of content.
        /// </summary>
        Copy,

        /// <summary>
        ///     Create a hard link in the destination location.
        /// </summary>
        HardLink,

        /// <summary>
        ///     Copy the file to the destination location, but do not validate the hash of the destination content.
        /// </summary>
        CopyNoVerify,

        /// <summary>
        ///     Move the file to the destination location.
        /// </summary>
        /// <remarks>
        /// This exists for an internal use case. Note that the file will cease to exist in its current location if successful.
        /// </remarks>
        Move,
    }
}
