using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server.Controllers;
using BuildXL.Utilities.Collections;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// This is  rather convoluted class which handles running a cache service and deployment proxy
    /// together.
    ///
    /// The main source of complexity here is tying together the two application lifetime models of
    /// the cache service and ASP.Net Core application host.
    ///
    /// The cache service is started. In its startup (IDistributedCacheServiceHostInternal.OnStartingServiceAsync(...)),
    /// it starts the web host.
    /// </summary>
    public class DeploymentProxyStartup : StartupBase
    {
        public DeploymentProxyStartup(IConfiguration configuration)
            : base(configuration)
        {
        }

        /// <summary>
        /// Indicate that only <see cref="DeploymentProxyController"/> should be surfaced on web api.
        /// </summary>
        protected override ServerMode Mode => ServerMode.DeploymentProxy;

        /// <summary>
        /// Run the deployment proxy along side the cache service
        /// </summary>
        public static Task RunWithCacheServiceAsync(string[] commandLineArgs, CancellationToken token)
        {
            var serviceHost = new ServiceHost(commandLineArgs);
            var configuration = serviceHost.ProxyHost.Services.GetRequiredService<IConfiguration>();

            var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            var logger = new Logger(consoleLog);

            var cacheConfigurationPath = configuration["CacheConfigurationPath"];
            var standalone = configuration.GetValue<bool>("standalone", true);
            return CacheServiceRunner.RunCacheServiceAsync(
                new OperationContext(new Context(logger), token),
                cacheConfigurationPath,
                (hostParameters, config, token) =>
                {
                    serviceHost.HostParameters.Value = hostParameters;
                    return serviceHost;
                },
                requireServiceInterruptable: !standalone);
        }

        private const string UseExternalServicesKey = "UseExternalServices";

        /// <summary>
        /// Configures the host builder to use the given values as services rather than creating its own on
        /// application initialization.
        /// </summary>
        private static void UseExternalServices(IHostBuilder hostBuilder, BoxRef<OperationContext> operationContext, BoxRef<HostParameters> hostParameters)
        {
            hostBuilder
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<DeploymentProxyStartup>();
                })
                .ConfigureHostConfiguration(configBuilder =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string>()
                    {
                        { UseExternalServicesKey, bool.TrueString }
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ILogger>(s => operationContext.Value.TracingContext.Logger);
                    services.AddSingleton<BoxRef<OperationContext>>(s => operationContext);
                    services.AddSingleton<HostParameters>(s => hostParameters.Value);
                });
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            bool useExternalServices = Configuration.GetValue<bool>(UseExternalServicesKey, false);

            if (!useExternalServices)
            {
                var hostParameters = HostParameters.FromEnvironment();
                var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
                var logger = new Logger(consoleLog);
                services.AddSingleton<ILogger>(logger);
                services.AddSingleton<BoxRef<OperationContext>>(new OperationContext(new Context(logger)));
                services.AddSingleton<HostParameters>(hostParameters);
            }

            // Add ProxyServiceConfiguration as a singleton in service provider
            services.AddSingleton(sp =>
            {
                var context = sp.GetRequiredService<BoxRef<OperationContext>>().Value;
                var configurationPath = Configuration["ProxyConfigurationPath"];
                var hostParameters = sp.GetService<HostParameters>();

                return context.PerformOperation(
                    new Tracer(nameof(DeploymentProxyStartup)),
                    () =>
                    {
                        var proxyConfiguration = CacheServiceRunner.LoadAndWatchPreprocessedConfig<DeploymentConfiguration, ProxyServiceConfiguration>(
                            context,
                            configurationPath,
                            configHash: out _,
                            hostParameters: hostParameters,
                            extractConfig: c => c.Proxy.ServiceConfiguration);

                        return Result.Success(proxyConfiguration);
                    },
                    messageFactory: r => $"ConfigurationPath=[{configurationPath}], Port={r.GetValueOrDefault()?.Port}",
                    caller: "LoadConfiguration").ThrowIfFailure();
            });

            // Add DeploymentProxyService as a singleton in service provider
            services.AddSingleton(sp =>
            {
                var hostParameters = sp.GetService<HostParameters>();

                var context = sp.GetRequiredService<BoxRef<OperationContext>>().Value;
                var configuration = sp.GetRequiredService<ProxyServiceConfiguration>();

                return context.PerformOperation(
                    new Tracer(nameof(DeploymentProxyStartup)),
                    () =>
                    {
                        return Result.Success(new DeploymentProxyService(
                            configuration,
                            hostParameters));
                    },
                    caller: "CreateProxyService").ThrowIfFailure();
            });
        }

        private class ServiceHost : EnvironmentVariableHost, IDistributedCacheServiceHostInternal
        {
            /// <summary>
            /// The web application host. This is surfaced to allow access to services (namely configuration i.e. command
            /// line args) from the host.
            /// </summary>
            public IHost ProxyHost { get; }

            // These references allow these variables to passed for use to the service provider/configuration
            // even though they are not immediately available when the ASP.Net core web application host
            // is built. (NOTE: They are available on Start).
            private BoxRef<OperationContext> OperationContext { get; } = new BoxRef<OperationContext>();
            public BoxRef<HostParameters> HostParameters { get; } = new BoxRef<HostParameters>();
            public ProxyConfigurationSource ConfigurationSource { get; } = new ProxyConfigurationSource();

            private DeploymentProxyService ProxyService { get; set; }

            /// <summary>
            /// Constructs the service host and takes command line arguments because
            /// ASP.Net core application host is used to parse command line.
            /// </summary>
            public ServiceHost(string[] commandLineArgs)
            {
                var hostBuilder = Host.CreateDefaultBuilder(commandLineArgs)
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseStartup<DeploymentProxyStartup>();
                    });

                hostBuilder.ConfigureHostConfiguration(cb => cb.Add(ConfigurationSource));

                DeploymentProxyStartup.UseExternalServices(hostBuilder, OperationContext, HostParameters);
                ProxyHost = hostBuilder.Build();
            }

            public async Task OnStartingServiceAsync(OperationContext context)
            {
                OperationContext.Value = context;
                ConfigurationSource.Configuration = ProxyHost.Services.GetService<ProxyServiceConfiguration>();

                // Get and start the DeploymentProxyService
                ProxyService = ProxyHost.Services.GetService<DeploymentProxyService>();

                await ProxyService.StartupAsync(context).IgnoreFailure();

                await ProxyHost.StartAsync(context.Token);
            }

            public async Task OnStoppingServiceAsync(OperationContext context)
            {
                if (ProxyService != null)
                {
                    await ProxyService.ShutdownAsync(context).IgnoreFailure();
                }
                // Not passing cancellation token since it will already be signaled
                await ProxyHost.StopAsync();

                ProxyHost.Dispose();
            }
        }

        /// <summary>
        /// Handles propagating the port configuration from the loaded <see cref="ProxyServiceConfiguration"/>
        /// to the web host so it will actually listen on that port.
        /// </summary>
        private class ProxyConfigurationSource : ConfigurationProvider, IConfigurationSource
        {
            public ProxyServiceConfiguration Configuration { get; set; }

            public ProxyConfigurationSource()
            {
            }

            public IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                return this;
            }

            public override bool TryGet(string key, out string value)
            {
                if (key == WebHostDefaults.ServerUrlsKey)
                {
                    Contract.Assert(Configuration != null);
                    value = $"http://*:{Configuration.Port}";
                    return true;
                }

                return base.TryGet(key, out value);
            }

        }
    }
}
