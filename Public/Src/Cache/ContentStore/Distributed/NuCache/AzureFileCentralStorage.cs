// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using Microsoft.WindowsAzure.Storage.File;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// An <see cref="CentralStorage"/> backed by Azure file shares.
    /// NOTE: Currently only implements sas url support since this is only used by DeploymentService
    /// which should not need other functionality since the assumption is that the file share's contents
    /// are maintained by the DeploymentIngester.
    /// </summary>
    public class AzureFilesCentralStorage : CentralStorage
    {
        private readonly (CloudFileDirectory container, int shardId)[] _containers;

        private readonly BlobCentralStoreConfiguration _configuration;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureFilesCentralStorage));

        /// <nodoc />
        public AzureFilesCentralStorage(BlobCentralStoreConfiguration configuration)
        {
            _configuration = configuration;

            _containers = _configuration.Credentials.Select(
                (credentials, index) =>
                {
                    Contract.Requires(credentials != null);
                    var cloudFileClient = credentials.CreateCloudStorageAccount().CreateCloudFileClient();
                    return (cloudFileClient.GetShareReference(configuration.ContainerName).GetRootDirectoryReference(), shardId: index);
                }).ToArray();
        }
        /// <inheritdoc />
        public override bool SupportsSasUrls => true;

        /// <inheritdoc />
        protected override async Task<Result<string>> TryGetSasUrlCore(OperationContext context, string storageId, DateTime expiry)
        {
            foreach (var (container, shardId) in _containers)
            {
                var blob = container.GetFileReference(storageId);
                var exists = await blob.ExistsAsync(null, null, context.Token);

                if (exists)
                {
                    var policy = new SharedAccessFilePolicy()
                    {
                        Permissions = SharedAccessFilePermissions.Read,
                        SharedAccessExpiryTime = expiry
                    };

                    var sasUrlQuery = blob.GetSharedAccessSignature(policy);
                    return blob.Uri.AbsoluteUri + sasUrlQuery;
                }
                else
                {
                    Tracer.Debug(context, $@"Could not find '{_configuration.ContainerName}\{storageId}' from shard #{shardId}.");
                }
            }

            return new ErrorResult($@"Could not find '{_configuration.ContainerName}\{storageId}'");
        }

        /// <inheritdoc />
        protected override Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            throw new NotImplementedException();
        }
    }
}
