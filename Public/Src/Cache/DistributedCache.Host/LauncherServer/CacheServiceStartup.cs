using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server.Controllers;
using BuildXL.Utilities.Collections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// This is  rather convoluted class which handles running a cache service and web host together.
    ///
    /// The main source of complexity here is tying together the two application lifetime models of
    /// the cache service and ASP.Net Core application host.
    ///
    /// The cache service is started. In its startup (IDistributedCacheServiceHostInternal.OnStartingServiceAsync(...)),
    /// it starts the web host.
    /// </summary>
    public class CacheServiceStartup : StartupBase
    {
        public CacheServiceStartup(IConfiguration configuration)
            : base(configuration)
        {
        }

        /// <summary>
        /// Indicate that only controllers surfaced by CacheService should be available on web api i.e.
        /// <see cref="DeploymentController"/>
        /// <see cref="ContentCacheController"/>
        /// </summary>
        protected override ServerMode Mode => ServerMode.CacheService;

        /// <summary>
        /// Run the deployment proxy along side the cache service
        /// </summary>
        public static Task RunWithCacheServiceAsync(string[] commandLineArgs, CancellationToken token)
        {
            var initialConfigurationHost = Host.CreateDefaultBuilder(commandLineArgs).Build();
            var configuration = initialConfigurationHost.Services.GetRequiredService<IConfiguration>();

            var consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            var logger = new Logger(consoleLog);

            var cacheConfigurationPath = configuration["CacheConfigurationPath"];
            var standalone = configuration.GetValue("standalone", true);
            var secretsProviderKind = configuration.GetValue("secretsProviderKind", CrossProcessSecretsCommunicationKind.Environment);
            
            return CacheServiceRunner.RunCacheServiceAsync(
                new OperationContext(new Context(logger), token),
                cacheConfigurationPath,
                createHost: (hostParameters, config, token) =>
                {
                    // If this process was started as a standalone cache service, we need to change the mode
                    // this time to avoid trying to start the cache service process again.
                    config.DistributedContentSettings.RunCacheOutOfProc = false;
                    if (config.DataRootPath is null)
                    {
                        // The required property is not set, so it should be passed through the command line options by the parent process.
                        config.DataRootPath = configuration.GetValue("DataRootPath", "Unknown DataRootPath");
                    }

                    var serviceHost = new ServiceHost(commandLineArgs, config, hostParameters, retrieveAllSecretsFromSingleEnvironmentVariable: secretsProviderKind == CrossProcessSecretsCommunicationKind.EnvironmentSingleEntry);
                    return serviceHost;
                },
                requireServiceInterruptable: !standalone);
        }

        private const string UseExternalServicesKey = "UseExternalServices";

        /// <summary>
        /// Configures the host builder to use the given values as services rather than creating its own on
        /// application initialization.
        /// </summary>
        private static void UseExternalServices(IHostBuilder hostBuilder, OperationContext operationContext, HostParameters hostParameters)
        {
            hostBuilder
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<CacheServiceStartup>();
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
                    services.AddSingleton<ILogger>(s => operationContext.TracingContext.Logger);
                    services.AddSingleton<BoxRef<OperationContext>>(operationContext);
                    services.AddSingleton<HostParameters>(hostParameters);
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

            // Only the launcher-based invocation would have 'ProxyConfigurationPath' and
            // the out-of-proc case would not.
            var configurationPath = Configuration.GetValue<string>("ProxyConfigurationPath", null);

            if (configurationPath is not null)
            {
                // Add ProxyServiceConfiguration as a singleton in service provider
                services.AddSingleton(sp =>
                {
                    var context = sp.GetRequiredService<BoxRef<OperationContext>>().Value;
                    var hostParameters = sp.GetService<HostParameters>();

                    return context.PerformOperation(
                        new Tracer(nameof(CacheServiceStartup)),
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
            }

            // Add DeploymentProxyService as a singleton in service provider
            services.AddSingleton(sp =>
            {
                var hostParameters = sp.GetService<HostParameters>();

                var context = sp.GetRequiredService<BoxRef<OperationContext>>().Value;
                var configuration = sp.GetRequiredService<ProxyServiceConfiguration>();

                return context.PerformOperation(
                    new Tracer(nameof(CacheServiceStartup)),
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
            private IHost WebHost { get; set; }

            private IHostBuilder WebHostBuilder { get; }

            // These references allow these variables to passed for use to the service provider/configuration
            // even though they are not immediately available when the ASP.Net core web application host
            // is built. (NOTE: They are available on Start).
            public HostParameters HostParameters { get; }
            public DistributedCacheServiceConfiguration ServiceConfiguration { get; }
            public ProxyConfigurationSource ConfigurationSource { get; } = new ProxyConfigurationSource();

            private DeploymentProxyService ProxyService { get; set; }
            private ContentCacheService ContentCacheService { get; set; }

            /// <summary>
            /// Constructs the service host and takes command line arguments because
            /// ASP.Net core application host is used to parse command line.
            /// </summary>
            public ServiceHost(string[] commandLineArgs, DistributedCacheServiceConfiguration configuration, HostParameters hostParameters, bool retrieveAllSecretsFromSingleEnvironmentVariable)
                : base(retrieveAllSecretsFromSingleEnvironmentVariable)
            {
                HostParameters = hostParameters;
                ServiceConfiguration = configuration;
                WebHostBuilder = Host.CreateDefaultBuilder(commandLineArgs)
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseStartup<CacheServiceStartup>();
                    });

                WebHostBuilder.ConfigureHostConfiguration(cb => cb.Add(ConfigurationSource));
            }

            public async Task OnStartedServiceAsync(OperationContext context, ICacheServerServices cacheServices)
            {
                CacheServiceStartup.UseExternalServices(WebHostBuilder, context, HostParameters);

                WebHostBuilder.ConfigureServices(services =>
                {
                    if (ServiceConfiguration.ContentCache != null)
                    {
                        if (cacheServices.PushFileHandler != null && cacheServices.StreamStore != null)
                        {
                            services.AddSingleton<ContentCacheService>(sp =>
                            {
                                return new ContentCacheService(
                                    ServiceConfiguration.ContentCache,
                                    cacheServices.PushFileHandler,
                                    cacheServices.StreamStore);
                            });
                        }
                    }
                });

                WebHost = WebHostBuilder.Build();
                ConfigurationSource.Configuration = WebHost.Services.GetService<ProxyServiceConfiguration>();

                // Get and start the DeploymentProxyService
                ProxyService = WebHost.Services.GetService<DeploymentProxyService>();
                ContentCacheService = WebHost.Services.GetService<ContentCacheService>();

                await ProxyService.StartupAsync(context).ThrowIfFailureAsync();

                if (ContentCacheService != null)
                {
                    await ContentCacheService.StartupAsync(context).ThrowIfFailureAsync();
                }

                await WebHost.StartAsync(context.Token);
            }

            public async Task OnStoppingServiceAsync(OperationContext context)
            {
                if (ProxyService != null)
                {
                    await ProxyService.ShutdownAsync(context).IgnoreFailure();
                }

                if (ContentCacheService != null)
                {
                    await ContentCacheService.ShutdownAsync(context).IgnoreFailure();
                }

                // Not passing cancellation token since it will already be signaled
                await WebHost.StopAsync();

                WebHost.Dispose();
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
