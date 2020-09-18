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
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the cache service verb.
        ///
        /// NOTE: Currently, this is highly reliant on being launched by the launcher.
        /// TODO: Add command line args with HostParameters and ServiceLifetime args so that
        /// this can be used standalone.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run distributed CAS service")]
        internal void CacheService
            (
            [Required, Description("Path to CacheServiceConfiguration file")] string configurationPath,
            [DefaultValue(false)] bool debug
            )
        {
            Initialize();

            if (debug)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                Validate();

                var hostParameters = HostParameters.FromEnvironment();

                var configJson = File.ReadAllText(configurationPath);
                var configHash = ContentHashers.Get(HashType.Murmur).GetContentHash(Encoding.UTF8.GetBytes(configJson)).ToHex();

                var preprocessor = DeploymentUtilities.GetHostJsonPreprocessor(hostParameters);

                var preprocessedConfigJson = preprocessor.Preprocess(configJson);

                var config = JsonSerializer.Deserialize<DistributedCacheServiceConfiguration>(preprocessedConfigJson, DeploymentUtilities.ConfigurationSerializationOptions);

                // TODO: Log CacheServiceConfiguration after initializing logger
                var host = new EnvironmentVariableHost();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, host.TeardownCancellationTokenSource.Token);

                var context = new OperationContext(new Context(_logger), cts.Token);
                ServiceLifetimeManager.RunDeployedInterruptableServiceAsync(context, async token =>
                {
                    var hostInfo = new HostInfo(hostParameters.Stamp, hostParameters.Ring, new List<string>());

                    await DistributedCacheServiceFacade.RunWithConfigurationAsync(
                        logger: context.TracingContext.Logger,
                        host: host,
                        hostInfo: hostInfo,
                        telemetryFieldsProvider: new HostTelemetryFieldsProvider(hostParameters) { ConfigurationId = configHash },
                        config,
                        token: token); ;

                    return BoolResult.Success;
                }).GetAwaiter().GetResult().ThrowIfFailure();
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
