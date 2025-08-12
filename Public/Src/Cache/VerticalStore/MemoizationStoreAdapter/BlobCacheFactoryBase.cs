// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.Host.Configuration;
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
            IConfiguration configuration = null,
            BuildXLContext buildXLContext = null)
        {
            Contract.Requires(cacheData != null);

            var possibleCacheConfig = cacheData.Create<TConfig>(activityId, configuration, buildXLContext);
            if (!possibleCacheConfig.Succeeded)
            {
                return possibleCacheConfig.Failure;
            }

            return await InitializeCacheAsync(possibleCacheConfig.Result);
        }

        /// <summary>
        /// Create cache using configuration
        /// </summary>
        public virtual async Task<Possible<ICache, Failure>> InitializeCacheAsync(TConfig configuration)
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
                var (logger, logPath) = await CreateLoggerAsync(configuration);

                // If the retention period is not set, this is not a blocker for constructing the cache, but performance can be degraded. Report it.
                var failures = new List<Failure>();

                var cache = new MemoizationStoreAdapterCache(
                    cacheId: configuration.CacheId,
                    innerCache: await CreateInnerCacheAsync(logger, configuration),
                    logger: logger,
                    statsFile: new AbsolutePath(logPath.Path + ".stats"),
                    isReadOnly: configuration.IsReadOnly,
                    implicitPin: ImplicitPin.None,
                    precedingStateDegradationFailures: failures);

                var startupResult = await cache.StartupAsync();
                if (!startupResult.Succeeded)
                {
                    cache.Dispose();
                    return startupResult.Failure;
                }

                return cache;
            }
            catch (Exception e)
            {
                return new CacheConstructionFailure(configuration.CacheId, e);
            }
        }

        internal static async Task<(ILogger logger, bool storageLogEnabled)> CreateLoggerAsync(TConfig configuration, ILogger fileLogger)
        {
            var logPath = new AbsolutePath(configuration.CacheLogPath);
            bool storageLogEnabled = false;
            ILogger logger;

            // Logging always happens to a file (and ETW)
            if (configuration.LogToKusto)
            {
                HostParameters hostParameters = HostParameters.FromEnvironment(configuration.LogParameters ?? new(), prefix: null);
                hostParameters.ServiceVersion ??= Utilities.Branding.Version;
                hostParameters.MachineFunction ??= configuration.Role;
                var telemetryFieldsProvider = new HostTelemetryFieldsProvider(hostParameters)
                {
                    ServiceName = "BuildXL",
                    BuildId = configuration.BuildId,
                };

                var workspacePath = Path.Combine(logPath.Parent?.Path ?? Path.GetTempPath(), $"{configuration.CacheId}Upload");
                Directory.CreateDirectory(workspacePath);

                AzureBlobStorageLog storageLog;
                fileLogger.Debug($"Kusto logging identity = {configuration.LogToKustoIdentityId}");
                if (!string.IsNullOrEmpty(configuration.LogToKustoIdentityId))
                {
                    storageLog = AzureBlobStorageLog.CreateWithManagedIdentity(
                        fileLogger, configuration.LogToKustoIdentityId, configuration.LogToKustoBlobUri, workspacePath, CancellationToken.None);
                }
                else
                {
                    var file = Environment.GetEnvironmentVariable(configuration.LogToKustoConnectionStringFileEnvironmentVariableName);
                    fileLogger.Debug($"Kusto logging connection string file = '{file}'");

                    var credentials = BlobCacheCredentialsHelper.Load(
                        new AbsolutePath(file),
                        configuration.ConnectionStringFileDataProtectionEncrypted ?
                            BlobCacheCredentialsHelper.FileEncryption.Dpapi
                            : BlobCacheCredentialsHelper.FileEncryption.None);

                    storageLog = AzureBlobStorageLog.CreateWithCredentials(
                        fileLogger,
                        credentials: credentials.Values.First(),
                        uploadWorkspacePath: workspacePath,
                        telemetryFieldsProvider: telemetryFieldsProvider,
                        CancellationToken.None);
                }

                await storageLog.StartupAsync().ThrowIfFailure();

                fileLogger.Debug($"Created storage logger.");

                var nLogger = await NLogAdapterHelper.CreateAdapterForCacheClientAsync(
                    fileLogger,
                    telemetryFieldsProvider,
                    configuration.Role,
                    new Dictionary<string, string>(),
                    storageLog);

                // Disable the ETW logging and just retain file logging
                storageLogEnabled = true;

                // We want the blob upload to happen in addition to the regular etw/file logging, so let's compose both
                logger = new CompositeLogger(fileLogger, nLogger);
            }
            else
            {
                logger = fileLogger;
            }

            BlobCacheAccessor.CacheLogger!.Value?.SetValue(logger);
            return (logger, storageLogEnabled);
        }


        internal static async Task<(ILogger logger, AbsolutePath logPath)> CreateLoggerAsync(TConfig configuration)
        {
            var logPath = new AbsolutePath(configuration.CacheLogPath);

            // Logging always happens to a file (and ETW)
            var etwFileLog = new EtwFileLog(logPath.Path, configuration.CacheId);
            var etwFileLogger = new DisposeLogger(etwFileLog, configuration.LogFlushIntervalSeconds);
            etwFileLogger.Debug($"Start logging at '{logPath}', Kusto logging enabled = {configuration.LogToKusto}");

            var (logger, storageLogEnabled) = await CreateLoggerAsync(configuration, etwFileLogger);

            if (storageLogEnabled)
            {
                // Disable the ETW logging and just retain file logging
                etwFileLog.DisableEtwLogging = true;
            }

            return (logger, logPath);
        }

        internal abstract Task<MemoizationStore.Interfaces.Caches.ICache> CreateInnerCacheAsync(ILogger logger, TConfig configuration);
    }
}
