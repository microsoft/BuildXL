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
        /// Loads a configuration object from preprocessed json
        /// </summary>
        public static TConfig LoadPreprocessedConfig<TConfig>(string configurationPath, out string configHash, HostParameters hostParameters = null)
        {
            hostParameters ??= HostParameters.FromEnvironment();
            var configJson = File.ReadAllText(configurationPath);
            configHash = ContentHashers.Get(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(configJson)).ToHex();

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
                var config = LoadPreprocessedConfig<DistributedCacheServiceConfiguration>(configurationPath, out var configHash, hostParameters);

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

        private class HostTelemetryFieldsProvider : ITelemetryFieldsProvider
        {
            private readonly HostParameters _hostParmeters;

            public string BuildId => "Unknown";

            public string ServiceName { get; } = "CacheService";

            public string APEnvironment => "Unknown";

            public string APCluster => "None";

            public string APMachineFunction => _hostParmeters.MachineFunction;

            public string MachineName => Environment.MachineName;

            public string ServiceVersion => "None";

            public string Stamp => _hostParmeters.Stamp;

            public string Ring => _hostParmeters.Ring;

            public string ConfigurationId { get; set; } = "None";

            public HostTelemetryFieldsProvider(HostParameters hostParameters)
            {
                _hostParmeters = hostParameters;
            }
        }
    }
}
