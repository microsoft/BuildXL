// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.Interfaces;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.Drop.App.Core;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Microsoft.VisualStudio.Services.ItemStore.Common;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// <see cref="IDropServiceClient"/> which will retry every operation in case <see cref="Microsoft.VisualStudio.Services.Common.VssUnauthorizedException"/> is caught.
    /// </summary>
    public sealed class ReloadingDropServiceClient : ReloadingClient<IDropServiceClient>, IDropServiceClient
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="clientConstructor">Target drop service client.</param>
        /// <param name="retryIntervals">How many times to retry and how much to wait between retries.</param>
        public ReloadingDropServiceClient(IIpcLogger logger, Func<IDropServiceClient> clientConstructor, IEnumerable<TimeSpan> retryIntervals = null)
            : base(logger, clientConstructor, retryIntervals)
        {
        }

        #region IDropServiceClient Interface Methods

        /// <inheritdoc />
        public Task RepairManifestAsync(string dropName, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.RepairManifestAsync),
                (client, ct) => client.RepairManifestAsync(dropName, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<Tuple<IEnumerable<BlobIdentifier>, AssociationsStatus>> AssociateAsync(string dropName, List<FileBlobDescriptor> preComputedBlobIds, bool abortIfAlreadyExists, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.AssociateAsync),
                (client, ct) => client.AssociateAsync(dropName, preComputedBlobIds, abortIfAlreadyExists, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<DropItem> CreateAsync(string dropName, bool isAppendOnly, DateTime? expirationDate, bool chunkDedup, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.CreateAsync),
                (client, ct) => client.CreateAsync(dropName, isAppendOnly, expirationDate, chunkDedup, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(DropItem dropItem, bool synchronous, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.DeleteAsync),
                (client, ct) => client.DeleteAsync(dropItem, synchronous, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task DownloadAsync(string dropName, DropServiceClientDownloadContext downloadContext, CancellationToken cancellationToken, bool releaseLocalCache = false)
        {
            return RetryAsync(
                nameof(IDropServiceClient.DownloadAsync),
                (client, ct) => client.DownloadAsync(dropName, downloadContext, ct, releaseLocalCache),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task DownloadFilesAsync(IEnumerable<BlobToFileMapping> mappings, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.DownloadFilesAsync),
                (client, ct) => client.DownloadFilesAsync(mappings, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task FinalizeAsync(string dropName, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.FinalizeAsync),
                (client, ct) => client.FinalizeAsync(dropName, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<DropItem> GetDropAsync(string dropName, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.GetDropAsync),
                (client, ct) => client.GetDropAsync(dropName, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<Uri> GetDropUri(string dropName, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.GetDropUri),
                (client, ct) => client.GetDropUri(dropName, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task UploadAndAssociateAsync(string dropName, List<FileBlobDescriptor> preComputedBlobIds, bool abortIfAlreadyExists, AssociationsStatus firstAssociationStatus, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.UploadAndAssociateAsync),
                (client, ct) => client.UploadAndAssociateAsync(dropName, preComputedBlobIds, abortIfAlreadyExists, firstAssociationStatus, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IAsyncEnumerator<IEnumerable<BlobToFileMapping>>> ListFilePagesAsync(
            string dropName,
            bool tryToRetrieveFromLocalCache,
            CancellationToken cancellationToken,
            bool allowPartial,
            IEnumerable<string> directories,
            bool recursive,
            bool getDownloadUris)
        {
            return RetryAsync(
                nameof(IDropServiceClient.ListFilePagesAsync),
                (client, ct) => client.ListFilePagesAsync(dropName, tryToRetrieveFromLocalCache, ct, allowPartial, directories, recursive, getDownloadUris),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task PublishAsync(
            string dropName,
            string sourceDirectory,
            bool abortIfAlreadyExists,
            List<FileBlobDescriptor> preComputedBlobIds,
            Action<FileBlobDescriptor> hashCompleteCallback,
            bool includeEmptyDirectories,
            bool lowercasePaths,
            CancellationToken cancellationToken)
        {
            return RetryAsync(
                 nameof(IDropServiceClient.PublishAsync),
                 (client, ct) => client.PublishAsync(dropName, sourceDirectory, abortIfAlreadyExists, preComputedBlobIds, hashCompleteCallback, includeEmptyDirectories, lowercasePaths, ct),
                 cancellationToken);
        }

        /// <inheritdoc />
        public Task UpdateExpirationAsync(string dropName, DateTime? expirationTime, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.UpdateExpirationAsync),
                (client, ct) => client.UpdateExpirationAsync(dropName, expirationTime, ct),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<DownloadResult> DownloadManifestToFilePathAsync(string dropName, string filePath, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.DownloadManifestToFilePathAsync),
                (client, ct) => client.DownloadManifestToFilePathAsync(dropName, filePath, ct),
                cancellationToken);
        }

        #endregion

        /// <inheritdoc />
        public Task<IEnumerable<DropItem>> ListAsync(
            string dropNamePrefix, 
            PathOptions pathOptions, 
            bool includeNonFinalizedDrops, 
            CancellationToken cancellationToken, 
            RetrievalOptions retrievalOptions,
            SizeOptions sizeOptions, 
            ExpirationDateOptions expirationDateOptions, 
            IDomainId domainId,
            int pageSize = -1,
            string continueFromDropName = null)
        {
            return RetryAsync(
                nameof(IDropServiceClient.ListAsync),
                (client, ct) => client.ListAsync(dropNamePrefix, pathOptions, includeNonFinalizedDrops, ct, retrievalOptions, sizeOptions, expirationDateOptions, domainId, pageSize, continueFromDropName),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IAsyncEnumerator<IEnumerable<DropItem>>> ListStreamedAsync(
            string dropNamePrefix,
            PathOptions pathOptions,
            CancellationToken cancellationToken,
            DropItemFilterOptions filterOptions,
            DropItemPaginationOptions paginationOptions = null)
        {
            return RetryAsync(
                nameof(IDropServiceClient.ListStreamedAsync),
                (client, ct) => client.ListStreamedAsync(dropNamePrefix, pathOptions, ct, filterOptions, paginationOptions),
                cancellationToken);
        }

        /// <inheritdoc />
        public string GetVersionString() => GetCurrentVersionedValue().Value.GetVersionString();

        /// <inheritdoc />
        public Task<DropItem> CreateAsync(IDomainId domainId, string dropName, bool isAppendOnly, DateTime? expirationDate, bool chunkDedup, CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.CreateAsync),
                (client, ct) => client.CreateAsync(domainId, dropName, isAppendOnly, expirationDate, chunkDedup, cancellationToken),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<MultiDomainInfo>> GetDomainsAsync(CancellationToken cancellationToken)
        {
            return RetryAsync(
                nameof(IDropServiceClient.GetDomainsAsync),
                (client, ct) => client.GetDomainsAsync(cancellationToken),
                cancellationToken);
        }

        /// <inheritdoc />
        public uint AttemptNumber
        {
            get => GetCurrentVersionedValue().Value.AttemptNumber;
            set => GetCurrentVersionedValue().Value.AttemptNumber = value;
        }

        /// <inheritdoc />
        public bool DisposeTelemetry
        {
            get => GetCurrentVersionedValue().Value.DisposeTelemetry;
            set => GetCurrentVersionedValue().Value.DisposeTelemetry = value;
        }
    }
}
