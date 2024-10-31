// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Inheritable configuration settings for cache factories that wish to configure a connection to a blob cache
    /// </summary>
    public class BlobCacheConfig : IEngineDependentSettingsConfiguration
    {
        /// <nodoc />
        public BlobCacheConfig()
        {
            CacheId = new CacheId("BlobCache");
        }

        /// <summary>
        /// The Id of the cache instance
        /// </summary>
        [DefaultValue(typeof(CacheId))]
        public CacheId CacheId { get; set; }

        /// <summary>
        /// Path to the log file for the cache.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string CacheLogPath { get; set; }

        /// <summary>
        /// Duration to wait for exclusive access to the cache directory before timing out.
        /// </summary>
        [DefaultValue(0)]
        public uint LogFlushIntervalSeconds { get; set; }

        /// <summary>
        /// <see cref="MemoizationStore.Stores.ContentHashListReplacementCheckBehavior"/>
        /// </summary>
        [DefaultValue(ContentHashListReplacementCheckBehavior.AllowPinElision)]
        public ContentHashListReplacementCheckBehavior ContentHashListReplacementCheckBehavior { get; set; } = ContentHashListReplacementCheckBehavior.AllowPinElision;

        /// <summary>
        /// Authenticate by using a single or an array of connection strings inside of an environment variable.
        /// </summary>
        /// <remarks>
        /// This is not a good authentication method because in many cases environment variables are logged and not
        /// encrypted.
        ///
        /// The preferred authentication method is to use a managed identity (<see cref="StorageAccountEndpoint"/>
        /// and <see cref="ManagedIdentityId"/>). However, this is unsupported for sharded scenarios and isn't
        /// available outside of Azure. Use <see cref="ConnectionStringFileEnvironmentVariableName"/> if that's
        /// your use-case.
        /// </remarks>
        [DefaultValue("BlobCacheFactoryConnectionString")]
        public string ConnectionStringEnvironmentVariableName { get; set; }

        /// <summary>
        /// Authenticate by using a file that contains a single or an array of connection strings.
        /// </summary>
        /// <remarks>
        /// The preferred authentication method is to use a managed identity (<see cref="StorageAccountEndpoint"/>
        /// and <see cref="ManagedIdentityId"/>). However, this is unsupported for sharded scenarios and isn't
        /// available outside of Azure. Use <see cref="ConnectionStringFileEnvironmentVariableName"/> if that's
        /// your use-case.
        /// </remarks>
        [DefaultValue("BlobCacheFactoryConnectionStringFile")]
        public string ConnectionStringFileEnvironmentVariableName { get; set; }

        /// <summary>
        /// Whether the connection string file should be considered to be DPAPI encrypted.
        /// </summary>
        [DefaultValue(true)]
        public bool ConnectionStringFileDataProtectionEncrypted { get; set; } = true;

        /// <summary>
        /// URI of the storage account endpoint to be used for this cache when authenticating using managed
        /// identities (e.g: https://mystorageaccount.blob.core.windows.net).
        /// </summary>
        [DefaultValue(null)]
        public string StorageAccountEndpoint { get; set; }

        /// <summary>
        /// The client id for the managed identity that will be used to authenticate against the storage account
        /// specified in <see cref="StorageAccountEndpoint"/>.
        /// </summary>
        [DefaultValue(null)]
        public string ManagedIdentityId { get; set; }

        /// <summary>
        /// Whether to allow interactive user authentication against the storage account. This should only be
        /// turned on for local builds.
        /// </summary>
        /// <remarks>
        /// Provided by the BuildXL main configuration object.
        /// </remarks>
        [DefaultValue(false)]
        public bool AllowInteractiveAuth { get; set; }

        /// <summary>
        /// The console window
        /// </summary>
        /// <remarks>
        /// Used for interactive auth purposes.
        /// </remarks>
        [DefaultValue(null)]
        public IConsole Console{ get; set; }

        /// <summary>
        /// The directory where interactive tokens should be stored and retrieved as a way to provide silent authentication (when possible)
        /// across BuildXL invocations.
        /// </summary>
        /// <remarks>
        /// Provided by the BuildXL main configuration object.
        /// </remarks>
        [DefaultValue(null)]
        public string InteractiveAuthTokenDirectory { get; set; }

        /// <summary>
        /// The configured number of days the storage account will retain blobs before deleting (or soft deleting)
        /// them based on last access time. If content and metadata have different retention policies, the shortest
        /// retention period is expected here.
        /// </summary>
        /// <remarks>
        /// This setting should only be used when utilizing service-less GC (i.e., GC is performed via Azure
        /// Storage's lifecycle management feature).
        /// 
        /// By setting this value to reflect the storage account life management configuration policy, pin
        /// operations can be elided if we know a fingerprint got a cache hit within the retention policy period.
        /// 
        /// When enabled (a positive value), every time that a content hash list is stored, a last upload time is
        /// associated to it and stored as well.
        /// This last upload time is deemed very close to the one used for storing all the corresponding content
        /// for that content hash (since typically that's the immediate step prior to storing the fingerprint).
        /// Whenever a content hash list is retrieved and has a last upload time associated to it, the metadata
        /// store notifies the cache of it. The cache then uses that information to determine whether the content
        /// associated to that fingerprint can be elided, based on the provided configured blob retention policy of
        /// the blob storage account.
        /// </remarks>
        [DefaultValue(-1)]
        public int RetentionPolicyInDays { get; set; } = -1;

        /// <nodoc />
        [DefaultValue("default")]
        public string Universe { get; set; }

        /// <nodoc />
        [DefaultValue("default")]
        public string Namespace { get; set; }

        /// <summary>
        /// The endpoint URI to use when uploading cache logs to a storage account
        /// </summary>
        /// <remarks>
        /// Null when no upload has to be performed
        /// </remarks>
        [DefaultValue(null)]
        public Uri LogToKustoBlobUri { get; set; }

        /// <summary>
        /// The managed identity to use when authenticating against the URI specified in <see cref="LogToKustoBlobUri"/>
        /// </summary>
        [DefaultValue(null)]
        public string LogToKustoIdentityId { get; set; }

        /// <summary>
        /// Authenticate to log storage by using a file that contains a single or an array of connection strings.
        /// </summary>
        /// <remarks>
        /// The preferred authentication method is to use a managed identity (<see cref="StorageAccountEndpoint"/>
        /// and <see cref="ManagedIdentityId"/>). However, this is unsupported for sharded scenarios and isn't
        /// available outside of Azure. Use <see cref="LogToKustoConnectionStringFileEnvironmentVariableName"/> if that's
        /// your use-case.
        /// </remarks>
        [DefaultValue("CacheLogConnectionStringFile")]
        public string LogToKustoConnectionStringFileEnvironmentVariableName { get; set; }

        /// <summary>
        /// Whether to upload cache logs to Kusto
        /// </summary>
        [DefaultValue(false)]
        public bool LogToKusto { get; set; }

        /// <summary>
        /// Host parameters for logging
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> LogParameters { get; set; }

        /// <summary>
        /// The role this machine has in the build (Coordinator/Worker)
        /// </summary>
        [DefaultValue(null)]
        public string Role { get; set; }

        /// <summary>
        /// Unique identifier of the build
        /// </summary>
        [DefaultValue(null)]
        public string BuildId { get; set; }

        /// <summary>
        /// Treat the blob cache as read only. Still pull from it.
        /// </summary>
        [DefaultValue(false)]
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Authenticate by using a file that contains a <see cref="HostedPoolBuildCacheConfigurationFile"/>
        /// </summary>
        /// <remarks>
        /// The preferred authentication method is to use a managed identity (<see cref="StorageAccountEndpoint"/>
        /// and <see cref="ManagedIdentityId"/>). However, this is unsupported for sharded scenarios and isn't
        /// available outside of Azure. Use <see cref="ConnectionStringFileEnvironmentVariableName"/> if that's
        /// your use-case.
        /// </remarks>
        [DefaultValue("BlobCacheFactoryHostedPoolConfigurationFile")]
        public string HostedPoolBuildCacheConfigurationFileEnvironmentVariableName { get; set; }

        /// <summary>
        /// When not null, we are running on the context of 1ESHP and a set of cache resources are associated to the pool. The value of this string points
        /// to the JSON file describing this topology.
        /// </summary>
        [DefaultValue(null)]
        public string HostedPoolBuildCacheConfigurationFile { get; set; }

        /// <summary>
        /// Only relevant when <see cref="HostedPoolBuildCacheConfigurationFile"/> is provided. When not null, the cache name (from the set of cache resources associated to the running pool)
        /// to use for this build
        /// </summary>
        [DefaultValue(null)]
        public string HostedPoolActiveBuildCacheName { get; set; }

        /// <summary>
        /// This configuration needs the role, activity id and the kusto logging info coming from the engine configuration object
        /// </summary>
        public bool TryPopulateFrom(Guid activityId, IConfiguration configuration, BuildXLContext buildXLContext, out Failure failure)
        {
            var logToKusto = configuration.Logging.LogToKusto;
            // For legacy reasons, cache logs require 'Master' when the build role is orchestrator
            Role = configuration.Distribution.BuildRole.IsOrchestrator() ? "Master" : configuration.Distribution.BuildRole.ToString();
            BuildId = configuration.Logging.RelatedActivityId ?? activityId.ToString();

            AllowInteractiveAuth = configuration.Interactive;
            // Let's use the engine cache as the target directory for storing the token
            // This should be enough to offer persistence/silent authentication for local builds
            InteractiveAuthTokenDirectory = configuration.Layout.EngineCacheDirectory.ToString(buildXLContext.PathTable);

            Console = buildXLContext.Console;

            if (!logToKusto)
            {
                failure = null;
                return true;
            }

            if (!Uri.TryCreate(configuration.Logging.LogToKustoBlobUri, UriKind.Absolute, out var uri))
            {
                failure = new CacheFailure($"Log upload endpoint '{configuration.Logging.LogToKustoBlobUri}' is not a valid URI");
                return false;
            }

            if (uri.Segments.Length != 2)
            {
                failure = new CacheFailure($"Uri expected format is 'https://<storage-account-name>.blob.core.windows.net/<container-name>'.");
                return false;
            }

            // The contract is that the user-specified container name is appended a 'cache' suffix to represent the container where cache logs go to
            var containerName = $"{uri.Segments[1]}cache";

            LogToKustoBlobUri = new Uri($"https://{uri.Host}/{containerName}");
            LogToKustoIdentityId = configuration.Logging.LogToKustoIdentityId;
            LogToKusto = true;

            failure = null;

            return true;
        }
    }
}
