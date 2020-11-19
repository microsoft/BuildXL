using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    public class DeploymentServiceStartup : StartupBase
    {
        public DeploymentServiceStartup(IConfiguration configuration)
            : base(configuration)
        {
        }

        protected override ServerMode Mode => ServerMode.DeploymentService;

        // This method gets called by the runtime. Use this method to add services to the container.
        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            var configuration = GetConfiguration();

            var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            var arguments = new LoggerFactoryArguments(new Logger(consoleLog), new EnvironmentVariableHost(), configuration.LoggingSettings)
            {
                TelemetryFieldsProvider = new HostTelemetryFieldsProvider(HostParameters.FromEnvironment())
            };

            var deploymentService = new DeploymentService(
                configuration: configuration,
                deploymentRoot: new AbsolutePath(Configuration["DeploymentRoot"]),
                secretsProviderFactory: keyVaultUri => new KeyVaultSecretsProvider(keyVaultUri),
                clock: SystemClock.Instance,
                uploadConcurrency: Environment.ProcessorCount);

            services.AddSingleton(deploymentService);
            services.AddSingleton<ILogger>(sp =>
            {
                var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
                var replacementLogger = LoggerFactory.CreateReplacementLogger(arguments);
                lifetime.ApplicationStopped.Register(() =>
                {
                    replacementLogger.DisposableToken?.Dispose();
                });
                return replacementLogger.Logger;
            });
        }

        private DeploymentServiceConfiguration GetConfiguration()
        {
            var configurationFile = Configuration["ConfigurationPath"];
            if (configurationFile == null)
            {
                return new DeploymentServiceConfiguration();
            }

            var configJson = File.ReadAllText(configurationFile);

            var configuration = JsonSerializer.Deserialize<DeploymentServiceConfiguration>(configJson, DeploymentUtilities.ConfigurationSerializationOptions);
            return configuration;
        }
    }
}
