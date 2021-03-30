// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.Host.Service
{
    public static class CacheServiceRunner
    {
        /// <summary>
        /// Loads a configuration object from preprocessed json and watches files for changes.
        /// When result config value changes, teardown will be requested.
        /// </summary>
        public static TResultConfig LoadAndWatchPreprocessedConfig<TConfig, TResultConfig>(
            OperationContext context,
            string configurationPath,
            HostParameters hostParameters,
            out string configHash,
            Func<TConfig, TResultConfig> extractConfig,
            Action<Context, string> requestTeardown = null,
            TimeSpan? pollingInterval = null)
        {
            requestTeardown ??= (context, reason) => LifetimeManager.RequestTeardown(context, reason);
            pollingInterval ??= TimeSpan.FromSeconds(5);

               var config = LoadPreprocessedConfig<TConfig>(configurationPath, out configHash, hostParameters);
            var resultConfig = extractConfig(config);

            var resultConfigString = JsonSerializer.Serialize(resultConfig);

            DeploymentUtilities.WatchFileAsync(
                configurationPath,
                context.Token,
                pollingInterval.Value,
                onChanged: () =>
                {
                    var newConfig = LoadPreprocessedConfig<TConfig>(configurationPath, out _, hostParameters);
                    var newResultConfig = extractConfig(newConfig);
                    var newResultConfigString = JsonSerializer.Serialize(resultConfig);
                    if (newResultConfigString != resultConfigString)
                    {
                        resultConfigString = newResultConfigString;
                        requestTeardown(context, "Configuration changed: " + configurationPath);
                    }
                },
                onError: ex =>
                {
                    requestTeardown(context, "Error: " + ex.ToString());
                });

            return resultConfig;
        }

        /// <summary>
        /// Loads a configuration object from preprocessed json
        /// </summary>
        public static TConfig LoadPreprocessedConfig<TConfig>(string configurationPath, out string configHash, HostParameters hostParameters = null)
        {
            hostParameters ??= HostParameters.FromEnvironment();
            var configJson = File.ReadAllText(configurationPath);
            configHash = HashInfoLookup.GetContentHasher(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(configJson)).ToHex();

            var preprocessor = DeploymentUtilities.GetHostJsonPreprocessor(hostParameters);

            var preprocessedConfigJson = preprocessor.Preprocess(configJson);

            var config = JsonSerializer.Deserialize<TConfig>(preprocessedConfigJson, DeploymentUtilities.ConfigurationSerializationOptions);
            return config;
        }

        /// <summary>
        /// Run the cache service verb.
        ///
        /// NOTE: Currently, this is highly reliant on being launched by the launcher.
        /// TODO: Add command line args with HostParameters and ServiceLifetime args so that
        /// this can be used standalone.
        /// </summary>
        public static async Task RunCacheServiceAsync(
            OperationContext context,
            string configurationPath,
            Func<HostParameters, DistributedCacheServiceConfiguration, CancellationToken, IDistributedCacheServiceHost> createhost,
            HostParameters hostParameters = null,
            bool requireServiceInterruptable = true)
        {
            try
            {
                hostParameters ??= HostParameters.FromEnvironment();

                using var cancellableContext = new CancellableOperationContext(context, default(CancellationToken));
                context = cancellableContext;

                var config = LoadAndWatchPreprocessedConfig<DistributedCacheServiceConfiguration, DistributedCacheServiceConfiguration>(
                    context,
                    configurationPath,
                    hostParameters,
                    out var configHash,
                    c => c);

                await ServiceLifetimeManager.RunDeployedInterruptableServiceAsync(context, async token =>
                {
                    var hostInfo = new HostInfo(hostParameters.Stamp, hostParameters.Ring, new List<string>());

                    var host = createhost(hostParameters, config, token);

                    await DistributedCacheServiceFacade.RunWithConfigurationAsync(
                        logger: context.TracingContext.Logger,
                        host: host,
                        hostInfo: hostInfo,
                        telemetryFieldsProvider: new HostTelemetryFieldsProvider(hostParameters) { ConfigurationId = configHash },
                        config,
                        token: token); ;

                    return BoolResult.Success;
                },
                requireServiceInterruptionEnabled: requireServiceInterruptable).ThrowIfFailure();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
