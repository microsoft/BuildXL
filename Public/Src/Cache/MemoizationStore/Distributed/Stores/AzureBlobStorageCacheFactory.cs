// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public static class AzureBlobStorageCacheFactory
    {
        /// <nodoc />
        public record Configuration(
                AzureBlobStorageCredentials Credentials,
                string Universe,
                TimeSpan StorageInteractionTimeout,
                TimeSpan MetadataPinElisionDuration,
                string Namespace = "default",
                bool CoerceNames = true
            );

        /// <nodoc />
        public static ICache Create(Configuration configuration)
        {
            string universe = configuration.Universe;
            string namespce = configuration.Namespace;

            if (!IsValidAzureBlobStorageContainerName(universe))
            {
                if (!configuration.CoerceNames)
                {
                    throw new ArgumentException($"Invalid cache universe specified. Cache universe names must conform to Azure Storage Container naming restrictions", nameof(universe));
                }

                universe = CoerceToValidContainerName(universe);
            }

            if (!IsValidAzureBlobStorageContainerName(namespce))
            {
                if (!configuration.CoerceNames)
                {
                    throw new ArgumentException($"Invalid cache namespace specified. Cache universe names must conform to Azure Storage Container naming restrictions", nameof(namespce));
                }

                namespce = CoerceToValidContainerName(namespce);
            }

            var metadataStoreConfiguration = new BlobMetadataStoreConfiguration()
            {
                Credentials = configuration.Credentials,
                ContainerName = namespce,
                FolderName = $"metadata/{universe}",
                StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                RetryPolicy = BlobFolderStorage.DefaultRetryPolicy,
            };
            var metadataStore = new AzureBlobStorageMetadataStore(metadataStoreConfiguration);
            var memoizationDatabase = new MetadataStoreMemoizationDatabase(metadataStore);
            var memoizationStore = new DatabaseMemoizationStore(memoizationDatabase)
            {
                OptimizeWrites = true
            };

            var contentStore = new AzureBlobStorageContentStore(new AzureBlobStorageContentStoreConfiguration()
            {
                Credentials = configuration.Credentials,
                ContainerName = namespce,
                FolderName = $"content/{universe}",
                StorageInteractionTimeout = configuration.StorageInteractionTimeout,
            });

            var cache = new OneLevelCache(
                contentStoreFunc: () => contentStore,
                memoizationStoreFunc: () => memoizationStore,
                configuration: new OneLevelCacheBaseConfiguration(
                    Id: Guid.NewGuid(),
                    PassContentToMemoization: false,
                    MetadataPinElisionDuration: configuration.MetadataPinElisionDuration
                ));

            return cache;
        }

        private static string CoerceToValidContainerName(string name)
        {
            name = name.ToLowerInvariant();

            // Remove invalid characters
            name = Regex.Replace(name, "[^a-z0-9-]", "");

            // Remove invalid sequences of characters
            name = Regex.Replace(name, "-+", "-");

            // Ensure it starts with number/letter
            if (!Regex.IsMatch(name, "^[a-z0-9].*"))
            {
                name = $"c{name}";
            }

            // Ensure it ends with number/letter
            if (!Regex.IsMatch(name, ".*[a-z0-9]$"))
            {
                name = $"{name}c";
            }

            // Ensure length requirements are met
            if (name.Length < 3)
            {
                name = $"ccc{name}";
            }

            if (name.Length > 63)
            {
                name = name.Substring(0, 63);
            }

            return name;
        }

        private static bool IsValidAzureBlobStorageContainerName(string name)
        {
            // See: https://social.msdn.microsoft.com/Forums/en-US/d364761b-6d9d-4c15-8353-46c6719a3392/what-regular-expression-could-i-use-to-validate-a-blob-container-name?forum=windowsazuredata
            if (name.Equals("$root"))
            {
                return true;
            }

            if (!Regex.IsMatch(name, @"^[a-z0-9](([a-z0-9\-[^\-])){1,61}[a-z0-9]$"))
            {
                return false;
            }

            return true;
        }
    }
}
