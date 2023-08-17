// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
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
            var config =  new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(
                        ShardingAlgorithm.SingleShard,
                        new List<BlobCacheStorageAccountName>() { accountName }),
                    Universe: "default",
                    Namespace: "default",
                    RetentionPolicyInDays: 0);

            var secretsProvider = new StaticBlobCacheSecretsProvider(new Dictionary<BlobCacheStorageAccountName, AzureStorageCredentials>()
            {
                { accountName, AzureStorageCredentials.StorageEmulator }
            });

            Assert.Throws<ContractException>(() => AzureBlobStorageCacheFactory.Create(
                config,
                secretsProvider));
        }
    }
}
