// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Possible results of materializing files through 
    /// <see cref="FileContentManager.TryMaterializeArtifactsCore(PipInfo, OperationContext, FileContentManager.PipArtifactsState, bool, bool)"/>.
    /// </summary>
    public enum ArtifactMaterializationResult
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// Succeeded.
        /// </summary>
        Succeeded = 1,

        /// <summary>
        ///  Deleting the contents of opaque (or dynamic) directories before deploying files from cache failed.
        /// </summary>
        PrepareDirectoriesFailed = 2,

        /// <summary>
        /// Checking that source file hashes match on distributed workers failed.
        /// </summary>
        VerifySourceFilesFailed = 3,

        /// <summary>
        /// Deleting files with hashes of <see cref="WellKnownContentHashes.AbsentFile"/> failed.
        /// </summary>
        DeleteFilesRequiredAbsentFailed = 4,

        /// <summary>
        /// Placing the file from cache failed (cache target miss in setup).
        /// </summary>
        PlaceFileFailed = 5,
    }
}
