// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.BuildCacheResource.Helper;
using BuildXL.Cache.BuildCacheResource.Model;
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

            // Case where an ADO build cache resource is configured
            if (configuration.HostedPoolBuildCacheConfigurationFile != null)
            {
                var hostedPoolBuildCacheConfiguration = BuildCacheResourceHelper.LoadFromJSONAsync(configuration.HostedPoolBuildCacheConfigurationFile).GetAwaiter().GetResult();

                if (!hostedPoolBuildCacheConfiguration.TrySelectBuildCache(configuration.HostedPoolActiveBuildCacheName, out var selectedBuildCacheConfiguration))
                {
                    throw new InvalidOperationException($"Cache resource with name '{configuration.HostedPoolActiveBuildCacheName}' was selected, but none of the available caches match. " +
                        $"Available cache names are: {string.Join(",", hostedPoolBuildCacheConfiguration.AssociatedBuildCaches.Select(buildCacheConfig => buildCacheConfig.Name))}");
                }

                context.TracingContext.Info($"Selecting 1ES Build cache resource with name {selectedBuildCacheConfiguration.Name}", nameof(BlobCacheFactory));

                var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(
                        ShardingAlgorithm.JumpHash,
                        selectedBuildCacheConfiguration.Shards.Select(shard => shard.GetAccountName()).ToList()),
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace,
                    RetentionPolicyInDays: selectedBuildCacheConfiguration.RetentionPolicyInDays,
                    IsReadOnly: configuration.IsReadOnly)
                {
                    BuildCacheConfiguration = selectedBuildCacheConfiguration
                };

                return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new AzureBuildCacheSecretsProvider(selectedBuildCacheConfiguration));
            }
            else
            {
                context.TracingContext.Info($"Creating blob cache. Universe=[{configuration.Universe}] Namespace=[{configuration.Namespace}] RetentionPolicyInDays=[{configuration.RetentionPolicyInDays}]", nameof(BlobCacheFactory));

                var credentials = LoadAzureCredentials(configuration, context);

                var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, credentials.Keys.ToList()),
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace,
                    RetentionPolicyInDays: configuration.RetentionPolicyInDays <= 0 ? null : configuration.RetentionPolicyInDays,
                    IsReadOnly: configuration.IsReadOnly);

                return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new StaticBlobCacheSecretsProvider(credentials));
            }
        }

        /// <nodoc />
        internal static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> LoadAzureCredentials(BlobCacheConfig configuration, OperationContext context)
        {
            Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> credentials = null;
            var token = context.Token;

            // Search for a connection string provided via a file (via an environment variable)
            var connectionStringFile = Environment.GetEnvironmentVariable(configuration.ConnectionStringFileEnvironmentVariableName);
            if (!string.IsNullOrEmpty(connectionStringFile))
            {
                context.TracingContext.Info("Authenticating with a connection string (file)", nameof(BlobCacheFactory));

                var encryption = configuration.ConnectionStringFileDataProtectionEncrypted
                    ? BlobCacheCredentialsHelper.FileEncryption.Dpapi
                    : BlobCacheCredentialsHelper.FileEncryption.None;
                credentials = BlobCacheCredentialsHelper.Load(new AbsolutePath(connectionStringFile), encryption);
            }

            // Search for a connection string provided via an environment variable
            var connectionString = Environment.GetEnvironmentVariable(configuration.ConnectionStringEnvironmentVariableName);
            if (credentials is null && !string.IsNullOrEmpty(connectionString))
            {
                context.TracingContext.Info("Authenticating with a connection string (env var)", nameof(BlobCacheFactory));

                credentials = BlobCacheCredentialsHelper.ParseFromEnvironmentFormat(connectionString);
            }

            // All the following auth methods work with a valid URI under configuration.StorageAccountEndpoint
            Uri uri = null;
            // The storage account endpoint can be null, but if it is not, it has to be valid
            if (!string.IsNullOrEmpty(configuration.StorageAccountEndpoint))
            {
                if (!Uri.TryCreate(configuration.StorageAccountEndpoint, UriKind.Absolute, out uri))
                {
                    throw new InvalidOperationException($"'{configuration.StorageAccountEndpoint}' does not represent a valid URI.");
                }
            }

            // Search for a provided managed identity
            if (credentials is null && configuration.ManagedIdentityId is not null && uri is not null)
            {
                Contract.Requires(!string.IsNullOrEmpty(configuration.ManagedIdentityId));

                context.TracingContext.Info("Authenticating with a managed identity", nameof(BlobCacheFactory));

                credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                var credential = new ManagedIdentityAzureStorageCredentials(configuration.ManagedIdentityId, uri);
                credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
            }

            // For Linux, check whether a codespaces credential helper can be used.
            if (OperatingSystemHelper.IsLinuxOS && uri is not null)
            {
                var authHelperToolPath = CodespacesCredentials.FindAuthHelperTool(out var failure);
                // If a failure is present, log it
                if (failure != null)
                {
                    context.TracingContext.Info($"An error occurred while trying to find '{CodespacesCredentials.AuthHelperToolName}'. Details: {failure}", nameof(BlobCacheFactory));
                }
                // If the authHelperToolPath is null, that just means the helper tool is not under PATH, so we just move on
                if (credentials is null && authHelperToolPath != null)
                {
                    context.TracingContext.Info("Authenticating with azure-auth-helper (codespaces)", nameof(BlobCacheFactory));

                    credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                    var credential = new CodespacesCredentials(authHelperToolPath, uri);
                    credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
                }
            }

            // If we didn't acquire any credentials so far and the configuration allows for interactive auth, let's set up
            // an interactive credential mechanism.
            if (credentials is null && configuration.AllowInteractiveAuth && uri is not null)
            {
                Contract.Requires(!string.IsNullOrEmpty(configuration.InteractiveAuthTokenDirectory));

                context.TracingContext.Info("Authenticating with (maybe silent) interactive browser", nameof(BlobCacheFactory));
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
