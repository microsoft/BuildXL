// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Distributed.Test
{
    public class AzureBlobStorageCacheFactoryTests
    {
        [Fact]
        public void ZeroRetentionPolicyThrows()
        {
            var accountName = BlobCacheStorageAccountName.Parse("devstoreaccount1");
            var config = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(
                        ShardingAlgorithm.SingleShard,
                        new List<BlobCacheStorageAccountName>() { accountName }),
                    Universe: "default",
                    Namespace: "default",
                    RetentionPolicyInDays: 0,
                    IsReadOnly: false);

            var secretsProvider = new StaticBlobCacheSecretsProvider(new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>()
            {
                { accountName, SecretBasedAzureStorageCredentials.StorageEmulator }
            });

            Assert.Throws<ContractException>(() => AzureBlobStorageCacheFactory.Create(
                new OperationContext(new Context(NullLogger.Instance)),
                config,
                secretsProvider));
        }
    }
}
