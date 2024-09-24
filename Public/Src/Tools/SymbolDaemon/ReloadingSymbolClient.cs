// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Symbol.App.Core;
using Microsoft.VisualStudio.Services.Symbol.WebApi;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// <see cref="ISymbolServiceClient"/> which will retry every operation in case of any issues.
    /// </summary>
    public sealed class ReloadingSymbolClient : ReloadingClient<ISymbolServiceClient>, ISymbolServiceClient
    {
        /// <nodoc/>
        public ReloadingSymbolClient(IIpcLogger logger, Func<ISymbolServiceClient> clientConstructor, TimeSpan operationTimeout, int maxRetryCount)
            : base(logger, clientConstructor, operationTimeout: operationTimeout, maxRetryCount, new[] { typeof(DebugEntryExistsException) })
        {
        }

        #region ISymbolServiceClient Interface Methods

        /// <inheritdoc />
        public Task<Request> CreateRequestAsync(IDomainId domainId, string requestName, bool isChunked, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.CreateRequestAsync),
                (client, ct) => client.CreateRequestAsync(domainId, requestName, isChunked, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<List<DebugEntry>> CreateRequestDebugEntriesAsync(
            string requestId,
            IEnumerable<DebugEntry> entries,
            DebugEntryCreateBehavior createBehavior,
            CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.CreateRequestDebugEntriesAsync),
                (client, ct) => client.CreateRequestDebugEntriesAsync(requestId, entries, createBehavior, ct),
                cancellationToken,
                callbackOnFirstRetriableFailure: (exception, guid) =>
                {
                    // DebugEntryExistsException is essentially a user error, so we can skip the logging.
                    if (exception is not DebugEntryExistsException)
                    {
                        if (!entries.TryGetNonEnumeratedCount(out var cnt))
                        {
                            // This should never happen since we always pass a real collection from the client.
                            cnt = entries.Count();
                        }

                        m_logger.Verbose($"[operation:{guid}] Arguments of a failed '{nameof(ISymbolServiceClient.CreateRequestDebugEntriesAsync)}' call -- createBehavior: {createBehavior}, entries.Count: {cnt}{Environment.NewLine}"
                            + string.Join(Environment.NewLine, entries.Select(e => $"BlobIdentifier: {e.BlobIdentifier}, Size: {e.Size}, ClientKey: {e.ClientKey}, InformationLevel: {e.InformationLevel}")));
                    }
                });
        }

        /// <inheritdoc />
        public Task<DebugEntry> CreateRequestDebugEntryAsync(string requestId, DebugEntry entry, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.CreateRequestDebugEntryAsync),
                (client, ct) => client.CreateRequestDebugEntryAsync(requestId, entry, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<DebugEntry> CreateRequestDebugEntryAsync(string requestId, DebugEntry entry, string filename, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.CreateRequestDebugEntryAsync),
                (client, ct) => client.CreateRequestDebugEntryAsync(requestId, entry, filename, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> DeleteRequestAsync(string requestId, CancellationToken cancellationToken, bool synchronous = false)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.DeleteRequestAsync),
                (client, ct) => client.DeleteRequestAsync(requestId, ct, synchronous),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<Request> FinalizeRequestAsync(string requestId, DateTime? expirationDate, bool isUpdateOperation, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.FinalizeRequestAsync),
                (client, ct) => client.FinalizeRequestAsync(requestId, expirationDate, isUpdateOperation, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Request>> GetAllRequestsAsync(CancellationToken cancellationToken,
            SizeOptions sizeOptions = null,
            ExpirationDateOptions expirationDateOptions = null,
            IDomainId domainIdOption = null,
            RetrievalOptions retrievalOptions = RetrievalOptions.ExcludeSoftDeleted,
            RequestStatus? requestStatus = null)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.GetAllRequestsAsync),
                (client, ct) => client.GetAllRequestsAsync(ct, sizeOptions, expirationDateOptions, domainIdOption, retrievalOptions, requestStatus),
                cancellationToken);
        }

        /// <inheritdoc />
        public BlobIdentifier GetBlobIdentifier(string filename, bool useChunkDedup)
        {
            var instance = GetCurrentVersionedValue();

            // not retrying this since it does not perform any calls over the network 
            return instance.Value.GetBlobIdentifier(filename, useChunkDedup);
        }

        /// <inheritdoc />
        public Task<IEnumerable<DebugEntry>> GetDebugEntriesAsync(
            string debugEntryClientKey,
            int? startEntry,
            int? maxEntries,
            DebugEntrySortOrder sortOrder,
            CancellationToken cancellationToken)
        {
            return RetryAsync(
               nameof(ISymbolServiceClient.GetDebugEntriesAsync),
               (client, ct) => client.GetDebugEntriesAsync(debugEntryClientKey, startEntry, maxEntries, sortOrder, ct),
               cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<MultiDomainInfo>> GetDomainsAsync(CancellationToken cancellationToken)
        {
            return RetryAsync(
               nameof(ISymbolServiceClient.GetDomainsAsync),
               (client, ct) => client.GetDomainsAsync(ct),
               cancellationToken);
        }

        /// <inheritdoc />
        public Task<Request> GetRequestAsync(string requestId, CancellationToken cancellationToken)
        {
            return RetryAsync(
               nameof(ISymbolServiceClient.GetRequestAsync),
               (client, ct) => client.GetRequestAsync(requestId, ct),
               cancellationToken);
        }

        /// <inheritdoc />
        public Task<Request> GetRequestByNameAsync(string requestName, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.GetRequestByNameAsync),
                (client, ct) => client.GetRequestByNameAsync(requestName, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<DebugEntry>> GetRequestDebugEntryAsync(string requestId, string debugEntryId, CancellationToken cancellationToken)
        {
            return RetryAsync(
                 nameof(ISymbolServiceClient.GetRequestDebugEntryAsync),
                 (client, ct) => client.GetRequestDebugEntryAsync(requestId, debugEntryId, ct),
                 cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Request>> GetRequestPaginatedAsync(
            string continueFromRequestId,
            int pageSize,
            CancellationToken cancellationToken,
            SizeOptions sizeOptions = null,
            ExpirationDateOptions expirationDateOptions = null,
            IDomainId domainIdOption = null,
            RetrievalOptions retrievalOptions = RetrievalOptions.ExcludeSoftDeleted,
            RequestStatus? requestStatus = null)
        {
            return RetryAsync(
                 nameof(ISymbolServiceClient.GetRequestPaginatedAsync),
                 (client, ct) => client.GetRequestPaginatedAsync(continueFromRequestId, pageSize, ct, sizeOptions, expirationDateOptions, domainIdOption, retrievalOptions, requestStatus),
                 cancellationToken);
        }

        /// <inheritdoc />
        public Task<System.Net.Http.HttpResponseMessage> GetSymSrvItemAsync(string path, CancellationToken cancellationToken)
        {
            return RetryAsync(
                 nameof(ISymbolServiceClient.GetSymSrvItemAsync),
                 (client, ct) => client.GetSymSrvItemAsync(path, ct),
                 cancellationToken);
        }

        /// <inheritdoc />
        public Task<DebugEntry> UploadAndCreateRequestDebugEntryAsync(string requestId, DebugEntry entry, string filename, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(ISymbolServiceClient.UploadAndCreateRequestDebugEntryAsync),
                (client, ct) => client.UploadAndCreateRequestDebugEntryAsync(requestId, entry, filename, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<SymbolBlobIdentifier> UploadFileAsync(IDomainId domainId, Uri blobStoreUri, string requestId, string filename, BlobIdentifier blobIdentifier, CancellationToken cancellationToken)
        {
            return RetryAsync(
               nameof(ISymbolServiceClient.UploadFileAsync),
               (client, ct) => client.UploadFileAsync(domainId, blobStoreUri, requestId, filename, blobIdentifier, ct),
               cancellationToken,
               callbackOnFirstRetriableFailure: (exception, guid) =>
               {
                   m_logger.Verbose($"[operation:{guid}] Arguments of a failed '{nameof(ISymbolServiceClient.UploadFileAsync)}' call -- filename: '{filename}', blobStoreUri: {blobStoreUri}, blobIdentifier: {blobIdentifier}, domainId: {domainId}");
               });
        }

        #endregion
    }
}
