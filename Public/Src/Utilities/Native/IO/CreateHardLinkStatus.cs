// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Status of a <see cref="IFileSystem.TryCreateHardLink"/> operation.
    /// </summary>
    public enum CreateHardLinkStatus
    {
        /// <summary>
        /// Succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// Hardlinks may not span volumes, but the destination path is on a different volume.
        /// </summary>
        FailedSinceDestinationIsOnDifferentVolume,

        /// <summary>
        /// The source file cannot have more links. It is at the filesystem's link limit.
        /// </summary>
        FailedDueToPerFileLinkLimit,

        /// <summary>
        /// The filesystem containing the source and destination does not support hardlinks.
        /// </summary>
        FailedSinceNotSupportedByFilesystem,

        /// <summary>
        /// AccessDenied was returned
        /// </summary>
        FailedAccessDenied,

        /// <summary>
        /// Generic failure.
        /// </summary>
        Failed,
    }
}
