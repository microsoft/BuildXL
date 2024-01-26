// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.Logging;
using BuildXL.Cache.Logging.External;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Base class for cache factories that interact with Azure blob
    /// </summary>
    /// <remarks>
    /// Handles cache initialization logic, including setting up Kusto uploads
    /// </remarks>
    public abstract class BlobCacheFactoryBase<TConfig> where TConfig : BlobCacheConfig, new()
    {
        /// <inheritdoc />
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(
            ICacheConfigData cacheData,
            Guid activityId,
            IConfiguration configuration = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<TConfig>(activityId, configuration);
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            return await InitializeCacheAsync(possibleCacheConfig.Result);
        }

        /// <summary>
        /// Create cache using configuration
        /// </summary>
        public async Task<Possible<ICache, Failure>> InitializeCacheAsync(TConfig configuration)
        {
            Contract.Requires(configuration != null);
            if (string.IsNullOrEmpty(configuration.Universe))
            {
                configuration.Universe = "default";
            }

            if (string.IsNullOrEmpty(configuration.Namespace))
            {
                configuration.Namespace = "default";
            }

            try
            {
                var logPath = new AbsolutePath(configuration.CacheLogPath);

                // Logging always happens to a file (and ETW)
                var etwFileLogger = new DisposeLogger(() => new EtwFileLog(logPath.Path, configuration.CacheId), configuration.LogFlushIntervalSeconds);

                // If Kusto uploading is specified, in addition to the file/etw logging we want to setup an additional logger to take care of this
                ILogger logger;
                if (configuration.LogToKusto)
                {
                    var workspacePath = Path.Combine(Path.GetTempPath(), $"{configuration.CacheId}Upload");
                    Directory.CreateDirectory(workspacePath);

                    var storageLog = AzureBlobStorageLog.CreateWithManagedIdentity(
                        etwFileLogger, configuration.LogToKustoIdentityId, configuration.LogToKustoBlobUri, workspacePath, CancellationToken.None);

                    await storageLog.StartupAsync().ThrowIfFailure();

                    var nLogger = await NLogAdapterHelper.CreateAdapterForCacheClientAsync(
                        etwFileLogger,
                        new BasicTelemetryFieldsProvider(configuration.BuildId),
                        configuration.Role,
                        new Dictionary<string, string>(),
                        storageLog);

                    // We want the blob upload to happen in addition to the regular etw/file logging, so let's compose both
                    logger = new CompositeLogger(etwFileLogger, nLogger);
                }
                else
                {
                    logger = etwFileLogger;
                }

                // If the retention period is not set, this is not a blocker for constructing the cache, but performance can be degraded. Report it.
                var failures = new List<Failure>();

                var cache = new MemoizationStoreAdapterCache(
                    cacheId: configuration.CacheId,
                    innerCache: await CreateCacheAsync(logger, configuration),
                    logger: logger,
                    statsFile: new AbsolutePath(logPath.Path + ".stats"),
                    implicitPin: ImplicitPin.None,
                    precedingStateDegradationFailures: failures);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(configuration.CacheId, e);
            }
        }

        internal abstract Task<MemoizationStore.Interfaces.Caches.ICache> CreateCacheAsync(ILogger logger, TConfig configuration);
    }
}
