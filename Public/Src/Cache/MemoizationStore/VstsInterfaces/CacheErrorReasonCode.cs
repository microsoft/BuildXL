// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.MemoizationStore.VstsInterfaces
{
    /// <summary>
    /// The reason for a cache exception.
    /// </summary>
    public enum CacheErrorReasonCode
    {
        /// <summary>
        /// Content hashlist not found in the remote
        /// </summary>
        ContentHashListNotFound = 0,

        /// <summary>
        /// Incorporate Failures in the server.
        /// </summary>
        IncorporateFailed = 1,

        /// <summary>
        /// Blobs failed to get finalized
        /// </summary>
        BlobFinalizationFailure = 2,

        /// <summary>
        /// Unknown reason.
        /// </summary>
        Unknown = 3
    }
}
