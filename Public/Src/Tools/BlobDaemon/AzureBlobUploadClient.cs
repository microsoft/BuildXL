// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Core;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// Production <see cref="IBlobUploadClient"/> backed by an Azure <see cref="BlobClient"/>.
    /// </summary>
    internal sealed class AzureBlobUploadClient : IBlobUploadClient
    {
        private readonly BlobClient m_blobClient;
        private readonly IIpcLogger m_logger;
        private readonly string m_logContext;
        private readonly string m_contentType;

        /// <nodoc/>
        public AzureBlobUploadClient(BlobClient blobClient, IIpcLogger logger, string logContext, string contentType)
        {
            m_blobClient = blobClient;
            m_logger = logger;
            m_logContext = logContext;
            m_contentType = contentType;
        }

        /// <inheritdoc />
        public async Task<bool> TryServerSideCopyAsync(Uri sourceUri, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            string copyId = null;
            try
            {
                var copyOperation = await m_blobClient.StartCopyFromUriAsync(sourceUri, cancellationToken: cts.Token);
                copyId = copyOperation.Id;
                await copyOperation.WaitForCompletionAsync(cts.Token);
                // The copy reached a terminal state, so there is no pending copy to abort in the catch block.
                copyId = null;

                // The copy's outcome is read from the destination blob's CopyStatus (not from the operation
                // object): a blob can be the target of at most one pending copy at a time, so once we have
                // awaited our copy above, the blob's properties reflect it. We check CopyStatus explicitly
                // because WaitForCompletionAsync does NOT throw on CopyStatus.Failed/Aborted, and its result
                // (bytes copied) is unreliable - 0 for both empty blobs and failed copies.
                var properties = (await m_blobClient.GetPropertiesAsync(cancellationToken: cts.Token)).Value;
                if (properties.CopyStatus == CopyStatus.Success)
                {
                    // Copy Blob inherits the source's Content-Type with no override, so set it with a separate header write.
                    if (m_contentType != null)
                    {
                        await m_blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = m_contentType }, cancellationToken: cts.Token);
                    }

                    return true;
                }

                m_logger.Warning($"{m_logContext} Server-side copy did not succeed (copy status: '{properties.CopyStatus}', description: '{properties.CopyStatusDescription}'). Falling back to local upload.");
            }
            catch (Exception e)
            {
                // If the copy did not reach a terminal state on the client (e.g., it timed out), the server-side
                // copy may still be pending. Writing to a blob that has a pending copy fails with 409, so we
                // best-effort abort the copy (via its copy id) before falling back to the local-upload path.
                if (copyId != null)
                {
                    try
                    {
                        await m_blobClient.AbortCopyFromUriAsync(copyId, cancellationToken: CancellationToken.None);
                    }
                    catch (Exception abortException)
                    {
                        // Best-effort: an abort failure (e.g., the copy already completed, or a transient
                        // error) should not stop us - log it and still attempt the local-upload fallback.
                        m_logger.Warning($"{m_logContext} Failed to abort the pending server-side copy (copy id '{copyId}'): {abortException.ToStringDemystified()}");
                    }
                }

                var reason = e is OperationCanceledException && cts.IsCancellationRequested
                    ? $"timed out after {timeout}"
                    : $"failed: {e.ToStringDemystified()}";
                m_logger.Warning($"{m_logContext} Server-side copy {reason}. Falling back to local upload.");
            }

            return false;
        }

        /// <inheritdoc />
        public Task UploadAsync(string localFilePath)
        {
            var options = new BlobUploadOptions();
            if (m_contentType != null)
            {
                options.HttpHeaders = new BlobHttpHeaders { ContentType = m_contentType };
            }

            return m_blobClient.UploadAsync(localFilePath, options);
        }
    }
}
