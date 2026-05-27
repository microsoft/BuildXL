// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using BuildXL.Cache.BuildCacheResource.Helper;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;
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
        private const string UserFacingDeviceCodeMessage = "Accessing the shared cache requires interactive user authentication.";

        internal async override Task<MemoizationStore.Interfaces.Caches.ICache> CreateInnerCacheAsync(ILogger logger, BlobCacheConfig configuration)
        {
            return (MemoizationStore.Interfaces.Caches.ICache)(await CreateCacheAsync(logger, configuration)).Cache;
        }

        internal async static Task<AzureBlobStorageCacheFactory.CreateResult> CreateCacheAsync(ILogger logger, BlobCacheConfig configuration)
        {
            var tracingContext = new Context(logger);
            var context = new OperationContext(tracingContext);

            // Case where an ADO build cache resource is configured
            var hostedPoolConfigurationFilePath = configuration.HostedPoolBuildCacheConfigurationFile;
            if (string.IsNullOrEmpty(hostedPoolConfigurationFilePath) && !string.IsNullOrEmpty(configuration.HostedPoolBuildCacheConfigurationFileEnvironmentVariableName))
            {
                hostedPoolConfigurationFilePath = Environment.GetEnvironmentVariable(configuration.HostedPoolBuildCacheConfigurationFileEnvironmentVariableName);
            }

            // First check if a hosted pool configuration file is provided. This is the indication the build is running in a hosted pool, which takes preference over other configurations.
            if (!string.IsNullOrEmpty(hostedPoolConfigurationFilePath))
            {
                var encryption = configuration.ConnectionStringFileDataProtectionEncrypted
                    ? BlobCacheCredentialsHelper.FileEncryption.Dpapi
                    : BlobCacheCredentialsHelper.FileEncryption.None;
                var credentials = BlobCacheCredentialsHelper.ReadCredentials(new AbsolutePath(hostedPoolConfigurationFilePath), encryption);

                var hostedPoolBuildCacheConfiguration = BuildCacheResourceHelper.LoadFromString(credentials);

                if (!hostedPoolBuildCacheConfiguration.TrySelectBuildCache(configuration.HostedPoolActiveBuildCacheName, out var selectedBuildCacheConfiguration))
                {
                    throw new InvalidOperationException($"Cache resource with name '{configuration.HostedPoolActiveBuildCacheName}' was selected, but none of the available caches match. " +
                        $"Available cache names are: {string.Join(",", hostedPoolBuildCacheConfiguration.AssociatedBuildCaches.Select(buildCacheConfig => buildCacheConfig.Name))}");
                }

                context.TracingContext.Info($"Selecting 1ES Build cache resource with name {selectedBuildCacheConfiguration.Name}", nameof(BlobCacheFactory));

                LogBuildCacheConfiguration(context.TracingContext, selectedBuildCacheConfiguration);

                var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(
                        ShardingAlgorithm.JumpHash,
                        selectedBuildCacheConfiguration.Shards.Select(shard => shard.GetAccountName()).ToList()),
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace,
                    RetentionPolicyInDays: selectedBuildCacheConfiguration.RetentionDays,
                    IsReadOnly: configuration.IsReadOnly)
                {
                    BuildCacheConfiguration = selectedBuildCacheConfiguration,
                    ContentHashListReplacementCheckBehavior = configuration.ContentHashListReplacementCheckBehavior,
                    EnableContentRecoveryOnPlaceFailure = configuration.EnableContentRecoveryOnPlaceFailure,
                };

                return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new AzureBuildCacheSecretsProvider(selectedBuildCacheConfiguration));
            }
            // Second, check if a developer build cache resource is configured
            else if (!string.IsNullOrEmpty(configuration.DeveloperBuildCacheResourceId))
            {
                // SAS tokens returned from the dev cache endpoint are readonly. So if the remote cache
                // is not configured as readonly, we are already bound to fail since the cache client will attempt
                // to write to it. So just fail early with a meaningful error message.
                if (!configuration.IsReadOnly)
                {
                    throw new InvalidOperationException("Developer build cache resources must be read-only. Please configure your remote cache to be read-only.");
                }

                context.TracingContext.Info($"Retrieving configuration for developer build cache {configuration.DeveloperBuildCacheResourceId}", nameof(BlobCacheFactory));

                // Authenticate and retrieve the build cache configuration.
                // If an Entra token environment variable is configured, attempt to use that token directly
                // before falling back to interactive authentication.
                TokenCredential token;
                if (TryGetEntraTokenFromEnvironment(context.TracingContext, configuration.DeveloperBuildCacheEntraTokenEnvVarName, out var entraTokenCredential))
                {
                    token = entraTokenCredential;
                }
                else
                {
                    token = UserInteractiveAuthenticate(
                        context,
                        configuration.Console,
                        configuration.DeveloperBuildCacheTenantId,
                        configuration.AllowInteractiveAuth,
                        context.Token);
                }

                var maybeBuildCacheConfiguration = await BuildCacheConfigurationProvider.TryGetBuildCacheConfigurationAsync(context.TracingContext, token, configuration.DeveloperBuildCacheResourceId, context.Token);

                if (!maybeBuildCacheConfiguration.Succeeded)
                {
                    throw maybeBuildCacheConfiguration.Failure.Throw();
                }

                var buildCacheConfiguration = maybeBuildCacheConfiguration.Result;

                LogBuildCacheConfiguration(context.TracingContext, buildCacheConfiguration);

                var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(
                        ShardingAlgorithm.JumpHash,
                        buildCacheConfiguration.Shards.Select(shard => shard.GetAccountName()).ToList()),
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace,
                    RetentionPolicyInDays: buildCacheConfiguration.RetentionDays,
                    IsReadOnly: configuration.IsReadOnly)
                {
                    BuildCacheConfiguration = buildCacheConfiguration,
                    ContentHashListReplacementCheckBehavior = configuration.ContentHashListReplacementCheckBehavior,
                    EnableContentRecoveryOnPlaceFailure = configuration.EnableContentRecoveryOnPlaceFailure,
                };

                return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new AzureBuildCacheSecretsProvider(buildCacheConfiguration));
            }
            // Third, check for the presence of other authentication methods
            else
            {
                context.TracingContext.Info($"Creating blob cache. Universe=[{configuration.Universe}] Namespace=[{configuration.Namespace}] RetentionPolicyInDays=[{configuration.RetentionPolicyInDays}]", nameof(BlobCacheFactory));

                var credentials = LoadAzureCredentials(configuration, context);

                var factoryConfiguration = new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, credentials.Keys.ToList()),
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace,
                    RetentionPolicyInDays: configuration.RetentionPolicyInDays <= 0 ? null : configuration.RetentionPolicyInDays,
                    IsReadOnly: configuration.IsReadOnly)
                {
                    EnableContentRecoveryOnPlaceFailure = configuration.EnableContentRecoveryOnPlaceFailure,
                };

                return AzureBlobStorageCacheFactory.Create(context, factoryConfiguration, new StaticBlobCacheSecretsProvider(credentials));
            }
        }

        /// <summary>
        /// Logs the build cache configuration to the cache log for debugging/traceability purposes
        /// </summary>
        /// <remarks>
        /// Logs the cache name, shard URIs, and retention days for the cache
        /// </remarks>
        private static void LogBuildCacheConfiguration(Context tracingContext, BuildCacheConfiguration buildCacheConfiguration)
        {
            tracingContext.Info($"Using build cache '{buildCacheConfiguration.Name}'. Shard count: {buildCacheConfiguration.Shards.Count}. Retention in days: {buildCacheConfiguration.RetentionDays}", nameof(BlobCacheFactory));
            foreach (var (shard, index) in buildCacheConfiguration.Shards.Select((shard, index) => (shard, index)))
            {
                tracingContext.Info($"[{buildCacheConfiguration.Name} - shard {index}] Url: '{shard.StorageUrl}'", nameof(BlobCacheFactory));
            }
        }

        /// <nodoc />
        internal static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> LoadAzureCredentials(BlobCacheConfig configuration, OperationContext context)
        {
            Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> credentials = null;
            var token = context.Token;

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

            // Check whether a CodeSpaces credential helper can be used.
            if (credentials is null && uri is not null)
            {
                if (TryCodespacesAuthentication(context, out var tokenCredential))
                {
                    context.TracingContext.Info("Authenticating with codespaces auth", nameof(BlobCacheFactory));
                    credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                    var credential = new UriAzureStorageTokenCredential(tokenCredential, uri);
                    credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
                }
            }

            // If we didn't acquire any credentials so far and the configuration allows for interactive auth, let's set up
            // an interactive credential mechanism.
            if (credentials is null && configuration.AllowInteractiveAuth && uri is not null)
            {
                Contract.Requires(!string.IsNullOrEmpty(configuration.DeveloperBuildCacheTenantId));

                context.TracingContext.Info("Authenticating with (maybe silent) interactive browser", nameof(BlobCacheFactory));
                credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
                var tokenCredential = new InteractiveClientTokenCredential(
                    debug => context.TracingContext.Info(debug, nameof(BlobCacheFactory)),
                    UserFacingDeviceCodeMessage,
                    configuration.DeveloperBuildCacheTenantId,
                    configuration.Console,
                    token);
                var credential = new UriAzureStorageTokenCredential(tokenCredential, uri);
                credentials.Add(BlobCacheStorageAccountName.Parse(credential.GetAccountName()), credential);
            }

            if (credentials is null)
            {
                throw new InvalidOperationException($"Can't find credentials to authenticate against the cache. Please see documentation for the supported authentication methods and how to configure them.");
            }

            return credentials;
        }

        private static ContentHash GetHashForTokenIdentifier(Uri uri)
        {
            return GetHashForTokenIdentifier(uri.ToString());
        }

        private static ContentHash GetHashForTokenIdentifier(string identifier)
        {
            return HashInfoLookup.GetContentHasher(HashType.SHA256).GetContentHash(Encoding.UTF8.GetBytes(identifier));
        }

        /// <inheritdoc />
        public IEnumerable<Failure> ValidateConfiguration(ICacheConfigData cacheData)
        {
            return CacheConfigDataValidator.ValidateConfiguration<BlobCacheConfig>(cacheData, cacheConfig =>
            {
                var failures = new List<Failure>();
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheLogPath, nameof(cacheConfig.CacheLogPath));
                failures.AddFailureIfNullOrWhitespace(cacheConfig.CacheId, nameof(cacheConfig.CacheId));
                return failures;
            });
        }

        private static TokenCredential UserInteractiveAuthenticate(
            Context context,
            IConsole console,
            string buildCacheTenantId,
            bool allowInteractiveAuth,
            CancellationToken cancellationToken)
        {
            // Check whether a CodeSpaces credential helper can be used first.
            // Observe that codespaces auth does not need user interaction
            if (TryCodespacesAuthentication(context, out var tokenCredential))
            {
                return tokenCredential;
            }

            // If interactive auth is not allowed, we can't proceed since we are about to launch browser/device code interactive auth
            if (!allowInteractiveAuth)
            {
                throw new InvalidOperationException("Interactive authentication is required to authenticate against the cache. " +
                    "Please see documentation for the supported authentication methods and how to configure them. If this is a developer build, consider passing /interactive+");
            }

            // Otherwise, we fallback to interactive authentication
            // The build cache resource ID is used to uniquely identify the auth token for the build cache, so it can be cached when doing interactive auth.
            return new InteractiveClientTokenCredential(
                debugMessage => context.Info(debugMessage, nameof(BlobCacheFactory)),
                UserFacingDeviceCodeMessage,
                buildCacheTenantId,
                console,
                cancellationToken);
        }

        /// <summary>
        /// Attempts to retrieve an Entra token from the environment variable specified by <paramref name="envVarName"/>.
        /// </summary>
        /// <returns>True if a non-empty token was found in the specified environment variable; false otherwise.</returns>
        private static bool TryGetEntraTokenFromEnvironment(Context tracingContext, string envVarName, out TokenCredential tokenCredential)
        {
            tokenCredential = null;

            if (string.IsNullOrEmpty(envVarName))
            {
                return false;
            }

            var tokenValue = Environment.GetEnvironmentVariable(envVarName);
            // Let's account for the fact that a user may mistakenly set as the value of the env var the Entra token itself (as opposed to the name of the env variable containing the token)
            // So trim the display name to avoid showing the full token in logs. The assumption is that 50 chars is enough to show the end of the env var name, which is the part that is more 
            // likely to contain identifying information
            var displayName = envVarName.Length > 50 ? envVarName.Substring(0, 50) + "..." : envVarName;
            
            if (string.IsNullOrEmpty(tokenValue))
            {
                tracingContext.Info($"Environment variable '{displayName}' for Entra token is configured but is not set or is empty. Falling back to other authentication methods.", nameof(BlobCacheFactory));
                return false;
            }

            tracingContext.Info($"Using Entra token from environment variable '{displayName}' for developer build cache authentication. Interactive auth is not required.", nameof(BlobCacheFactory));
            // The standard expiration for Entra tokens is 1hr. However, observe this is not really critical. This token
            // will be used right away to retrieve the 1ES Build Cache configuration, and after that it won't be needed anymore.
            // The cache configuration carries its own SAS tokens which are used for authenticating against the cache.
            // Here we are assuming that the token was obtained with a 1-hour expiration. If that's not the case, the authentication
            // will fail and we will fallback to interactive auth.
            tokenCredential = new StaticAccessTokenCredential(tokenValue, TimeSpan.FromHours(1));
            return true;
        }

        private static bool TryCodespacesAuthentication(Context context, out TokenCredential tokenCredential)
        {
            tokenCredential = null;
            // CodeSpaces authentication is only available on Linux
            if (!OperatingSystemHelper.IsLinuxOS)
            {
                context.Info("Not running on Linux, so codespaces authentication is not available", nameof(BuildCacheConfigurationProvider));
                return false;
            }

            var authHelperToolPath = AzureAuthTokenCredential.FindAuthHelperTool(out var failure);
            // If a failure is present, log it
            if (failure != null)
            {
                context.Info($"An error occurred while trying to find '{AzureAuthTokenCredential.AuthHelperToolName}'. Details: {failure}", nameof(BuildCacheConfigurationProvider));
            }

            // If the authHelperToolPath is null, that just means the helper tool is not under PATH, so we just move on
            if (authHelperToolPath != null)
            {
                context.Info("Authenticating with azure-auth-helper (codespaces)", nameof(BuildCacheConfigurationProvider));

                tokenCredential = new AzureAuthTokenCredential(authHelperToolPath);
                return true;
            }

            return false;
        }
    }
}
