// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// A factory method used for creating <see cref="IDistributedCacheServiceHost"/> and <see cref="ICacheServerGrpcHost"/>.
    /// </summary>
    public delegate (IDistributedCacheServiceHost serviceHost, ICacheServerGrpcHost grpcHost) CreateHostsDelegate(HostParameters hostParameters, DistributedCacheServiceConfiguration configuration, CancellationToken token);

    /// <summary>
    /// A facade for running the cache service.
    /// </summary>
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
            TimeSpan? pollingInterval = null,
            string overlayConfigurationPath = null)
        {
            requestTeardown ??= LifetimeManager.RequestTeardown;
            pollingInterval ??= TimeSpan.FromSeconds(5);

            var config = LoadPreprocessedConfig<TConfig>(context, configurationPath, out configHash, hostParameters, overlayConfigurationPath);
            var resultConfig = extractConfig(config);

            var resultConfigString = JsonSerializer.Serialize(resultConfig);

            List<string> paths = new List<string>() { configurationPath };
            if (overlayConfigurationPath != null)
            {
                paths.Add(overlayConfigurationPath);
            }

            DeploymentUtilities.WatchFilesAsync(
                paths,
                context.Token,
                pollingInterval.Value,
                onChanged: configIndex =>
                {
                    var newConfig = LoadPreprocessedConfig<TConfig>(context, configurationPath, out _, hostParameters, overlayConfigurationPath);
                    var newResultConfig = extractConfig(newConfig);
                    var newResultConfigString = JsonSerializer.Serialize(newResultConfig);
                    var oldResultConfigString = resultConfigString;

                    // Compare the json serialized representation of the old and new configuration
                    // in order to detect changes. NOTE: This post preprocessing and overlay
                    // to ensure that final configuration matches. We also load and deserialize
                    // to ensure the final value returned is what is compared.
                    if (newResultConfigString != oldResultConfigString)
                    {
                        resultConfigString = newResultConfigString;
                        requestTeardown(context, string.Join(Environment.NewLine, $"Configuration changed: {paths[configIndex]}",
                                "Old:",
                                oldResultConfigString,
                                "New:",
                                newResultConfigString));
                    }
                },
                onError: ex =>
                {
                    requestTeardown(context, "Error: " + ex.ToStringDemystified());
                });

            return resultConfig;
        }

        /// <summary>
        /// Loads a configuration object from preprocessed json
        /// </summary>
        public static TConfig LoadPreprocessedConfig<TConfig>(OperationContext context, string configurationPath, out string configHash, HostParameters hostParameters = null, string overlayConfigurationPath = null)
        {
            hostParameters ??= HostParameters.FromEnvironment();
            var configJson = File.ReadAllText(configurationPath);

            var preprocessor = DeploymentUtilities.GetHostJsonPreprocessor(hostParameters);
            var preprocessedConfigJson = preprocessor.Preprocess(configJson);

            configHash = HashInfoLookup.GetContentHasher(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(configJson)).ToHex(stringLength: 12);

            if (overlayConfigurationPath != null && File.Exists(overlayConfigurationPath))
            {
                var overlayConfigJson = File.ReadAllText(overlayConfigurationPath);
                var preprocessedOverlayJson = preprocessor.Preprocess(overlayConfigJson);

                configHash += HashInfoLookup.GetContentHasher(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(overlayConfigJson)).ToHex(stringLength: 12);

                preprocessedConfigJson = JsonMerger.Merge(preprocessedConfigJson, overlayJson: preprocessedOverlayJson);
            }

            var config = JsonSerializer.Deserialize<TConfig>(preprocessedConfigJson, DeploymentUtilities.ConfigurationSerializationOptions);

            context.TracingContext.Debug(JsonSerializer.Serialize(config, DeploymentUtilities.ConfigurationSerializationOptions), nameof(CacheServiceRunner));
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
            CreateHostsDelegate createHosts,
            HostParameters hostParameters = null,
            bool requireServiceInterruptable = true,
            string overlayConfigurationPath = null)
        {
            try
            {
                hostParameters ??= HostParameters.FromEnvironment();

                using var cts = new CancellationTokenSource();
                LifetimeManager.OnTeardownRequested += args =>
                {
                    cts.Cancel();
                };

                using var cancellableContext = new CancellableOperationContext(context, cts.Token);
                context = cancellableContext;

                var config = LoadAndWatchPreprocessedConfig<DistributedCacheServiceConfiguration, DistributedCacheServiceConfiguration>(
                    context,
                    configurationPath,
                    hostParameters,
                    out var configHash,
                    c => c,
                    overlayConfigurationPath: overlayConfigurationPath);

                // If the ConfigurationId was not propagated through the environment variables (for the Launcher case)
                // we hash the config file and use the hash as the ConfigurationId.
                // Otherwise we use the ConfigurationId propagated from the parent process.
                hostParameters.ConfigurationId ??= configHash;

                await ServiceLifetimeManager.RunDeployedInterruptableServiceAsync(context, async token =>
                {
                    var hostInfo = new HostInfo(hostParameters);

                    var (serviceHost, grpcHost) = createHosts(hostParameters, config, token);
                    await DistributedCacheServiceFacade.RunWithConfigurationAsync(
                        tracingContext: context,
                        host: serviceHost,
                        grpcHost: grpcHost,
                        hostInfo: hostInfo,
                        telemetryFieldsProvider: new HostTelemetryFieldsProvider(hostParameters),
                        config,
                        token: token);

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
