// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
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
        /// <summary>
        /// Maximum source size accepted by a synchronous upload-from-URI request (5,000 MiB). Larger sources
        /// use the asynchronous copy, which has no practical size limit.
        /// </summary>
        internal const long MaxSyncUploadSourceBytes = 5000L * 1024 * 1024;

        /// <summary>
        /// Ceiling for a synchronous server-side copy. The caller's timeout bounds the asynchronous copy, which
        /// can legitimately take many minutes; a synchronous request that has not returned in this long is not
        /// going to, and holding a concurrency slot for the caller's full budget starves the upload pipeline.
        /// </summary>
        /// <remarks>
        /// CODESYNC: <see cref="BlobDaemon.CreateBlobClientOptions"/> sets the SDK's per-attempt network timeout
        /// from this value. The SDK default is 100 seconds and applies to each attempt, so leaving it alone would
        /// cancel and re-issue the whole copy before this ceiling was ever reached.
        /// </remarks>
        internal static readonly TimeSpan SyncUploadTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Ceiling for the local-upload fallback, and for the best-effort abort of a pending asynchronous copy.
        /// The retry policy alone allows roughly a quarter of an hour of delays per file, and neither call took
        /// a cancellation token before, so a persistently throttled account could stall one file indefinitely.
        /// </summary>
        private static readonly TimeSpan LocalUploadTimeout = TimeSpan.FromMinutes(15);

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
        public async Task<bool> TryServerSideCopyAsync(Uri sourceUri, long sourceSizeBytes, TimeSpan timeout)
        {
            // A negative size means the caller does not know it. The synchronous API requires the source to
            // report a valid Content-Length and rejects sources above MaxSyncUploadSourceBytes with a 409, so
            // both the unknown and the too-large case go to the asynchronous copy, which has no size limit.
            if (sourceSizeBytes < 0 || sourceSizeBytes > MaxSyncUploadSourceBytes)
            {
                m_logger.Verbose($"{m_logContext} Using an asynchronous server-side copy ({(sourceSizeBytes < 0 ? "source size unknown" : $"source is {sourceSizeBytes} bytes, above the {MaxSyncUploadSourceBytes} byte synchronous limit")}).");
                return await TryAsyncCopyFromUriAsync(sourceUri, timeout);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await TrySyncUploadFromUriAsync(sourceUri, timeout);
            if (result == SyncUploadResult.Succeeded)
            {
                return true;
            }

            if (result == SyncUploadResult.TimedOut)
            {
                // The synchronous ceiling is far below the caller's budget, so a source that is merely slow -
                // large, cross-region, or contending with other copies - can exceed it while still being a
                // perfectly good candidate for a server-side copy. Falling straight through to materialize plus
                // local upload would move every one of those bytes through the agent. Spend what is left of the
                // caller's budget on the asynchronous copy, which is what this path used before.
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    m_logger.Warning($"{m_logContext} Synchronous server-side copy timed out with no budget left for an asynchronous retry.");
                    return false;
                }

                m_logger.Warning($"{m_logContext} Retrying as an asynchronous server-side copy, with {remaining} of the budget left.");
                return await TryAsyncCopyFromUriAsync(sourceUri, remaining);
            }

            return false;
        }

        private enum SyncUploadResult
        {
            Succeeded,

            /// <summary>Exceeded <see cref="SyncUploadTimeout"/>; the copy may still be viable given longer.</summary>
            TimedOut,

            Failed,
        }

        /// <summary>
        /// Synchronous server-side copy. A single request that returns only on completion, so there is no
        /// long-running-operation polling, and it carries the blob HTTP headers inline.
        /// </summary>
        private async Task<SyncUploadResult> TrySyncUploadFromUriAsync(Uri sourceUri, TimeSpan timeout)
        {
            // Never exceed the caller's budget, but do not inherit the asynchronous path's generous ceiling.
            var effectiveTimeout = timeout < SyncUploadTimeout ? timeout : SyncUploadTimeout;
            using var cts = new CancellationTokenSource(effectiveTimeout);
            try
            {
                // Reuses the parent container's pipeline and credential; no new client machinery per file.
                var blockBlobClient = m_blobClient.GetParentBlobContainerClient().GetBlockBlobClient(m_blobClient.Name);

                var options = new BlobSyncUploadFromUriOptions();
                if (m_contentType != null)
                {
                    // Explicit blob HTTP headers take precedence over the source blob's copied properties.
                    options.HttpHeaders = new BlobHttpHeaders { ContentType = m_contentType };
                }

                await blockBlobClient.SyncUploadFromUriAsync(sourceUri, options, cts.Token);
                return SyncUploadResult.Succeeded;
            }
            catch (Exception e)
            {
                bool timedOut = e is OperationCanceledException && cts.IsCancellationRequested;
                var reason = timedOut
                    ? $"timed out after {effectiveTimeout}"
                    : $"failed: {e.ToStringDemystified()}";
                m_logger.Warning($"{m_logContext} Synchronous server-side copy {reason}.");
                return timedOut ? SyncUploadResult.TimedOut : SyncUploadResult.Failed;
            }
        }

        /// <summary>
        /// Asynchronous server-side copy. It has no practical size limit, but the service schedules it
        /// best-effort and it must be polled to completion, which is what made it pathologically slow for large
        /// numbers of small blobs. So it is used only where the synchronous path cannot go: sources above
        /// <see cref="MaxSyncUploadSourceBytes"/>, sources whose size is unknown, and as a second attempt when a
        /// synchronous copy exceeded <see cref="SyncUploadTimeout"/> but the caller still has budget left.
        /// </summary>
        private async Task<bool> TryAsyncCopyFromUriAsync(Uri sourceUri, TimeSpan timeout)
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
                        // Bounded: this is cleanup for an operation whose budget is already spent, so it must not
                        // be able to outlive it by another retry cycle.
                        using var abortCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                        await m_blobClient.AbortCopyFromUriAsync(copyId, cancellationToken: abortCts.Token);
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
        public async Task UploadAsync(string localFilePath)
        {
            var options = new BlobUploadOptions();
            if (m_contentType != null)
            {
                options.HttpHeaders = new BlobHttpHeaders { ContentType = m_contentType };
            }

            // Bound the fallback. With 20 retries and a 60s delay cap, an account that keeps returning 503 can
            // otherwise keep a single file - and the concurrency slot it holds - busy for the better part of an
            // hour, with no way for the caller to give up.
            using var cts = new CancellationTokenSource(LocalUploadTimeout);
            await m_blobClient.UploadAsync(localFilePath, options, cts.Token);
        }
    }
}
