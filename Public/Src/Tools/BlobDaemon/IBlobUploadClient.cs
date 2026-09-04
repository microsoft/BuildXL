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
        /// <paramref name="timeout"/>. Returns true iff the copy succeeded.
        /// </summary>
        /// <remarks>
        /// <paramref name="sourceSizeBytes"/> selects the API: sources within the synchronous size limit are
        /// copied in a single request, larger ones use the asynchronous copy. Pass a negative value if the size
        /// is unknown, which forces the asynchronous path.
        /// </remarks>
        Task<bool> TryServerSideCopyAsync(Uri sourceUri, long sourceSizeBytes, TimeSpan timeout);

        /// <summary>
        /// Uploads a local file into the destination blob, overwriting any existing blob.
        /// </summary>
        Task UploadAsync(string localFilePath);
    }
}
