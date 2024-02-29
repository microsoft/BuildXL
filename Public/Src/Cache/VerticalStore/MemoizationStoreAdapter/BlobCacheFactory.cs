// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache Factory for BuildXL as a cache user for dev machines using Azure blob
    /// </summary>
    /// <remarks>
    /// This is the class responsible for creating the BuildXL to Cache adapter in the Selfhost builds. It configures
    /// the cache on the BuildXL process.
    ///
    /// Current limitations while we flesh things out:
    /// 1) APIs around tracking named sessions are not implemented
    /// </remarks>
    public class BlobCacheFactory : BlobCacheFactoryBase<BlobCacheConfig>, ICacheFactory
    {
        internal override Task<MemoizationStore.Interfaces.Caches.ICache> CreateCacheAsync(ILogger logger, BlobCacheConfig configuration)
        {
            return Task.FromResult((MemoizationStore.Interfaces.Caches.ICache)CreateCache(logger, configuration).Cache);
        }

        internal static AzureBlobStorageCacheFactory.CreateResult CreateCache(ILogger logger, BlobCacheConfig configuration)
        {
            var tracingContext = new Context(logger);
            var context = new OperationContext(tracingContext);

            context.TracingContext.Info($"Creating blob cache. Universe=[{configuration.Universe}] Namespace=[{configuration.Namespace}] RetentionPolicyInDays=[{configuration.RetentionPolicyInDays}]", nameof(EphemeralCacheFactory));
            
            var credentials = LoadAzureCredentials(configuration, context.Token);

            var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, credentials.Keys.ToList()),
                Universe: configuration.Universe,
                Namespace: configuration.Namespace,
                RetentionPolicyInDays: configuration.RetentionPolicyInDays <= 0 ? null : configuration.RetentionPolicyInDays);

            return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new StaticBlobCacheSecretsProvider(credentials));
        }

        /// <nodoc />
        internal static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> LoadAzureCredentials(BlobCacheConfig configuration, CancellationToken token)
        {
            Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> credentials = null;

            var connectionStringFile = Environment.GetEnvironmentVariable(configuration.ConnectionStringFileEnvironmentVariableName);
            if (!string.IsNullOrEmpty(connectionStringFile))
            {
                var encryption = configuration.ConnectionStringFileDataProtectionEncrypted
                    ? BlobCacheCredentialsHelper.FileEncryption.Dpapi
                    : BlobCacheCredentialsHelper.FileEncryption.None;
                credentials = BlobCacheCredentialsHelper.Load(new AbsolutePath(connectionStringFile), encryption);
            }

            var connectionString = Environment.GetEnvironmentVariable(configuration.ConnectionStringEnvironmentVariableName);
            if (credentials is null && !string.IsNullOrEmpty(connectionString))
            {
                credentials = BlobCacheCredentialsHelper.ParseFromEnvironmentFormat(connectionString);
            }


            if (credentials is null && configuration.ManagedIdentityId is not null && configuration.StorageAccountEndpoint is not null)
            {
                Contract.Requires(!string.IsNullOrEmpty(configuration.ManagedIdentityId));
                Contract.Requires(!string.IsNullOrEmpty(configuration.StorageAccountEndpoint));

                if (!Uri.TryCreate(configuration.StorageAccountEndpoint, UriKind.Absolute, out Uri uri))
                {
                    throw new InvalidOperationException($"'{configuration.StorageAccountEndpoint}' does not represent a valid URI.");
                }

                credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                var credential = new ManagedIdentityAzureStorageCredentials(configuration.ManagedIdentityId, uri);
                credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
            }

            // If we didn't acquire any credentials so far and the configuration allows for interactive auth, let's set up
            // an interactive credential mechanism.
            if (credentials is null && configuration.AllowInteractiveAuth)
            {
                Contract.Requires(!string.IsNullOrEmpty(configuration.StorageAccountEndpoint));
                Contract.Requires(!string.IsNullOrEmpty(configuration.InteractiveAuthTokenDirectory));

                if (!Uri.TryCreate(configuration.StorageAccountEndpoint, UriKind.Absolute, out Uri uri))
                {
                    throw new InvalidOperationException($"'{configuration.StorageAccountEndpoint}' does not represent a valid URI.");
                }
                
                credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                var credential = new InteractiveClientStorageCredentials(configuration.InteractiveAuthTokenDirectory, uri, token);
                credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
            }

            if (credentials is null)
            {
                throw new InvalidOperationException($"Can't find credentials to authenticate against the Blob Cache. Please see documentation for the supported authentication methods and how to configure them.");
            }

            return credentials;
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<BlobCacheConfig>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.ConnectionStringEnvironmentVariableName, nameof(cacheConfig.ConnectionStringEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.ConnectionStringFileEnvironmentVariableName, nameof(cacheConfig.ConnectionStringFileEnvironmentVariableName));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Universe, nameof(cacheConfig.Universe));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.Namespace, nameof(cacheConfig.Namespace));
                return failures;
            });
        }
    }
}
