// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Result of a call to IAbsFileSystem.CreateHardLink; otherwise will throw.
    /// </summary>
    public enum CreateHardLinkResult
    {
        /// <summary>
        ///     A link was successfully created.
        /// </summary>
        Success,

        /// <summary>
        ///     A link could not be created because the source and destination paths are not on the same volume.
        /// </summary>
        FailedSourceAndDestinationOnDifferentVolumes,

        /// <summary>
        ///     A link could not be created because the source file already has the maximum number of links.
        /// </summary>
        FailedMaxHardLinkLimitReached,

        /// <summary>
        ///     A link could not be created because a file already exists at the destination path and overwrite was not requested.
        /// </summary>
        FailedDestinationExists,

        /// <summary>
        ///     A link could not be created because the source file does not exist.
        /// </summary>
        FailedSourceDoesNotExist,

        /// <summary>
        ///     A link cound not be created because a file already exists at the destination path and access was denied.
        /// </summary>
        FailedAccessDenied,

        /// <summary>
        ///     A link could not be created because links are not supported on this volume
        /// </summary>
        FailedNotSupported,

        /// <summary>
        ///     A link could not be created because the destination path has length greater than MAX_PATH (260).
        /// </summary>
        FailedPathTooLong,

        /// <summary>
        ///     A link could not be created because of a failure in opening the source file.
        /// </summary>
        FailedSourceHandleInvalid,

        /// <summary>
        ///     A link could not be created because the user does not have permission to the source file.
        /// </summary>
        FailedSourceAccessDenied,

        /// <summary>
        ///     A link could not be created because the parent directory does not exist.
        /// </summary>
        FailedDestinationDirectoryDoesNotExist,

        /// <summary>
        ///     A link was not created and the reason was not set
        /// </summary>
        Unknown
    }
}
