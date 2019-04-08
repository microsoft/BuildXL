// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.ContentStore.VstsInterfaces
{
    /// <summary>
    /// A client interface for talking to BlobStore.
    /// </summary>
    public interface IBlobStoreClient : IDisposable
    {
        /// <summary>
        /// Tries to reference the Blob identifiers with a particular timeout and returns missing blobs.
        /// </summary>
        Task<ISet<BlobIdentifier>> TryReferenceAsync(Dictionary<BlobIdentifier, DateTime> references, CancellationToken token);

        /// <summary>
        /// Gets a stream pointer to a blob in blobstore.
        /// </summary>
        Task<Stream> GetBlobAsync(BlobIdentifier blobId, CancellationToken token);

        /// <summary>
        /// Gets download URIs for azure blobs for particular blob identifiers.
        /// </summary>
        Task<IDictionary<BlobIdentifier, ExpirableUri>> GetDownloadUrisAsync(BlobIdentifier[] blobIds, CancellationToken token);

        /// <summary>
        /// Uploads a stream to a specific blobId and sets the expiry to a set time.
        /// </summary>
        Task UploadAndReferenceBlobAsync(BlobIdentifier blobId, Stream stream, DateTime endDateTime);

        /// <summary>
        /// Gets a stream to an azure blob given a download URi.
        /// </summary>
        Task<Stream> GetStreamThroughAzureBlobs(ExpirableUri uri, int? overrideStreamMinimumReadSizeInBytes, CancellationToken token);

        /// <summary>
        /// Gets the error detection strategy for communicating with Azure blob and VSTS blob.
        /// </summary>
        ITransientErrorDetectionStrategy GetTransientErrorDetectionStrategy();

        /// <summary>
        /// Downloads an azure based Http stream to a destination path in a parallel multi-block manner.
        /// </summary>
        /// <param name="httpStream">The stream to download</param>
        /// <param name="destinationPath">The path to place the file</param>
        /// <param name="fileMode">The mode to open a stream handle to the file.</param>
        /// <param name="token">A cancellation token</param>
        /// <param name="context">Logging context</param>
        /// <param name="streamGeneratorFunc">Generates a stream at a given offset (the first int param), for a particular size (second int param)</param>
        Task DownloadAsync(Stream httpStream, string destinationPath, FileMode fileMode, CancellationToken token, Context context, Func<int, int, Task<Stream>> streamGeneratorFunc);
    }
}
