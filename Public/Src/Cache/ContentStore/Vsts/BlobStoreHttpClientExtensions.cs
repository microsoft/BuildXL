// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Extension methods for BlobStoreHttpClients.
    /// </summary>
    public static class BlobStoreHttpClientExtensions
    {
        /// <summary>
        ///     Alternative for UploadAndReferenceBlobAsync that uses WalkBlocksAsync instead of the synchronous WalkBlocks.
        ///     Also utilizes the AsyncHttpRetryHelper to mitigate transient exceptions.
        /// </summary>
        public static async Task UploadAndReferenceBlobWithRetriesAsync(
            this IBlobStoreHttpClient client,
            BlobIdentifier blobId,
            Stream stream,
            BlobReference reference,
            Context context,
            CancellationToken cts)
        {
            Contract.Requires(stream != null);

            var attempt = 0;
            await AsyncHttpRetryHelper.InvokeVoidAsync(
                async () =>
                {
                    bool blobUploaded = false;

                    stream.Position = 0;
                    attempt++;

                    await VsoHash.WalkBlocksAsync(
                        stream,
                        blockActionSemaphore: null,
                        multiBlocksInParallel: true,
                        singleBlockCallback:
                        async (block, blockLength, blockHash) =>
                        {
                            await client.PutSingleBlockBlobAndReferenceAsync(
                                blobId, block, blockLength, reference, cts).ConfigureAwait(false);
                            blobUploaded = true;
                        },
                        multiBlockCallback:
                        (block, blockLength, blockHash, isFinalBlock) =>
                            client.PutBlobBlockAsync(blobId, block, blockLength, cts),
                        multiBlockSealCallback: async blobIdWithBlocks =>
                        {
                            var failedRefs = await client.TryReferenceWithBlocksAsync(
                                new Dictionary<BlobIdentifierWithBlocks, IEnumerable<BlobReference>>
                                {
                                        {blobIdWithBlocks, new[] {reference}}
                                },
                                cancellationToken: cts).ConfigureAwait(false);
                            blobUploaded = !failedRefs.Any();
                        }
                    );

                    if (!blobUploaded)
                    {
                        throw new AsyncHttpRetryHelper.RetryableException($"Could not upload blob on attempt {attempt}.");
                    }
                },
                maxRetries: 5,
                tracer: new AppTraceSourceContextAdapter(context, "BlobStoreHttpClient", SourceLevels.All),
                canRetryDelegate: exception =>
                {
                    // HACK HACK: This is an additional layer of retries specifically to catch the SSL exceptions seen in DM.
                    // Newer versions of Artifact packages have this retry automatically, but the packages deployed with the M119.0 release do not.
                    // Once the next release is completed, these retries can be removed.
                    if (exception is HttpRequestException && exception.InnerException is WebException)
                    {
                        return true;
                    }

                    while (exception != null)
                    {
                        if (exception is TimeoutException)
                        {
                            return true;
                        }
                        exception = exception.InnerException;
                    }
                    return false;
                },
                cancellationToken: cts,
                continueOnCapturedContext: false,
                context: context.Id.ToString());
        }
    }
}
