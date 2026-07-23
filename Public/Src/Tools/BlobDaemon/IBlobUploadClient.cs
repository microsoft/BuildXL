// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// Abstraction over the blob operations that <see cref="BlobDaemon"/> performs on a single destination blob.
    /// It exists so the upload orchestration in BlobDaemon can be unit-tested independently of the Azure Storage SDK.
    /// </summary>
    public interface IBlobUploadClient
    {
        /// <summary>
        /// Attempts a server-side copy of <paramref name="sourceUri"/> into the destination blob, bounded by
        /// <paramref name="timeout"/>. Returns true iff the copy ended in CopyStatus.Success. Encapsulates the
        /// start/wait/status-check/best-effort-abort-on-timeout mechanics and logs failures.
        /// </summary>
        Task<bool> TryServerSideCopyAsync(Uri sourceUri, TimeSpan timeout);

        /// <summary>
        /// Uploads a local file into the destination blob, overwriting any existing blob.
        /// </summary>
        Task UploadAsync(string localFilePath);
    }
}
