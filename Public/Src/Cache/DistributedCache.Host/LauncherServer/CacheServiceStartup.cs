// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Launcher.Server.Controllers;
using BuildXL.Utilities.Collections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;
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
            var overlayConfigurationPath = configuration["OverlayConfigurationPath"];
            var standalone = configuration.GetValue<bool>("standalone", true);
            var secretsProviderKind = configuration.GetValue("secretsProviderKind", CrossProcessSecretsCommunicationKind.Environment);
            var context = new Context(logger);

            if (!standalone)
            {
                // When the Ctrl+C is sent to a process in Windows, its actually being sent to a process group, i.e. to the process subtree.
                // In a non-standalone mode the lifetime of the child process is handled via the custom lifetime management logic
                // and the process should ignore the Ctrl+C event to avoid unexpected exits.
                
                // Using 'CancelKeyPress' instead of TreatControlCAsInput = true because the event handler disables both 'Ctrl+C' and 'Ctrl+Break',
                // and TreatControlCAsInput only disables 'Ctrl+C' case.
                System.Console.CancelKeyPress += (_, e) => e.Cancel = true;
            }

            return CacheServiceRunner.RunCacheServiceAsync(
                new OperationContext(context, token),
                cacheConfigurationPath,
                createHost: (hostParameters, config, token) =>
                {
                    // If this process was started as a standalone cache service, we need to change the mode
                    // this time to avoid trying to start the cache service process again.
                    config.DistributedContentSettings.RunCacheOutOfProc = false;
                    config.DataRootPath = configuration.GetValue("DataRootPath", config.DataRootPath);
                    Contract.Assert(config.DataRootPath != null,
                        "The required property (DataRootPath) is not set, so it should be passed through the command line options by the parent process.");

                    var serviceHost = new ServiceHost(commandLineArgs, config, hostParameters, context, secretsProviderKind);
                    return serviceHost;
                },
                requireServiceInterruptable: !standalone,
                overlayConfigurationPath: overlayConfigurationPath);
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

            var proxyConfigurationPath = Configuration["ProxyConfigurationPath"];
            if (!string.IsNullOrEmpty(proxyConfigurationPath))
            {
                // Add ProxyServiceConfiguration as a singleton in service provider
                services.AddSingleton<ProxyServiceConfiguration>(sp =>
                {
                    var context = sp.GetRequiredService<BoxRef<OperationContext>>().Value;
                    var hostParameters = sp.GetService<HostParameters>();

                    return context.PerformOperation(
                        new Tracer(nameof(CacheServiceStartup)),
                        () =>
                        {
                            var proxyConfiguration = CacheServiceRunner.LoadAndWatchPreprocessedConfig<DeploymentConfiguration, ProxyServiceConfiguration>(
                                context,
                                proxyConfigurationPath,
                                configHash: out _,
                                hostParameters: hostParameters,
                                extractConfig: c => c.Proxy.ServiceConfiguration);

                            return Result.Success(proxyConfiguration);
                        },
                        messageFactory: r => $"ConfigurationPath=[{proxyConfigurationPath}], Port={r.GetValueOrDefault()?.Port}",
                        caller: "LoadConfiguration").ThrowIfFailure();
                });

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
        }

        public class ServiceHost : EnvironmentVariableHost, IDistributedCacheServiceHostInternal
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

            private DeploymentProxyService ProxyService { get; set; }
            private ContentCacheService ContentCacheService { get; set; }

            private bool UseGrpc => ServiceConfiguration.DistributedContentSettings.EnableAspNetCoreGrpc;

            /// <summary>
            /// Constructs the service host and takes command line arguments because
            /// ASP.Net core application host is used to parse command line.
            /// </summary>
            public ServiceHost(string[] commandLineArgs, DistributedCacheServiceConfiguration configuration, HostParameters hostParameters, Context context, CrossProcessSecretsCommunicationKind secretsCommunicationKind = CrossProcessSecretsCommunicationKind.Environment)
                : base(context, secretsCommunicationKind, configuration.DistributedContentSettings?.OutOfProcCacheSettings?.InterProcessSecretsCommunicationFileName)
            {
                HostParameters = hostParameters;
                ServiceConfiguration = configuration;
                WebHostBuilder = Host.CreateDefaultBuilder(commandLineArgs)
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        if (UseGrpc)
                        {
                            webBuilder.ConfigureLogging(l =>
                            {
                                l.ClearProviders();

                                if (configuration.DistributedContentSettings?.EnableAspNetCoreLogging == true)
                                {
                                    l.AddProvider(new LoggingAdapter("ASPNET", context));
                                }
                            });

                            webBuilder.ConfigureKestrel(o =>
                            {
                                int? port = null;
                                var proxyConfiguration = WebHost.Services.GetService<ProxyServiceConfiguration>();
                                if (UseGrpc)
                                {
                                    port = (int)ServiceConfiguration.LocalCasSettings.ServiceSettings.GrpcPort;
                                }
                                else if (proxyConfiguration != null)
                                {
                                    port = proxyConfiguration.Port;
                                }

                                o.ConfigureEndpointDefaults(listenOptions =>
                                {
                                    listenOptions.Protocols = HttpProtocols.Http2;
                                });

                                if (port.HasValue)
                                {
                                    o.ListenAnyIP(port.Value);
                                }
                            });
                        }

                        webBuilder.UseStartup<CacheServiceStartup>();
                    });
            }

            public async Task OnStartedServiceAsync(OperationContext context, ICacheServerServices cacheServices)
            {
                CacheServiceStartup.UseExternalServices(WebHostBuilder, context, HostParameters);

                WebHostBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<DistributedCacheServiceConfiguration>(ServiceConfiguration);

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

                    if (UseGrpc)
                    {
                        services.AddSingleton(cacheServices);

                        var grpcServiceCollection = new ServiceCollectionWrapper(services);

                        foreach (var grpcEndpoint in cacheServices.GrpcEndpoints)
                        {
                            grpcEndpoint.AddServices(grpcServiceCollection);
                        }

                        services.AddSingleton<BinderConfiguration>(MetadataServiceSerializer.BinderConfiguration);
                        services.AddCodeFirstGrpc();
                    }
                });

                WebHost = WebHostBuilder.Build();

                // Get and start the DeploymentProxyService
                ProxyService = WebHost.Services.GetService<DeploymentProxyService>();
                ContentCacheService = WebHost.Services.GetService<ContentCacheService>();

                if (ProxyService != null)
                {
                    await ProxyService.StartupAsync(context).ThrowIfFailureAsync();
                }

                if (ContentCacheService != null)
                {
                    await ContentCacheService.StartupAsync(context).ThrowIfFailureAsync();
                }

                await WebHost.StartAsync(context.Token);
            }

            private record ServiceCollectionWrapper(IServiceCollection Services) : IGrpcServiceCollection
            {
                public void AddService<TService>(TService service) where TService : class
                {
                    Services.AddSingleton<TService>(service);
                }
            }

            public async Task OnStoppingServiceAsync(OperationContext context)
            {
                // Not passing cancellation token since it will already be signaled
                
                // WebHost is null for out-of-proc casaas case.
                if (WebHost != null)
                {
                    await WebHost.StopAsync();
                }

                if (ProxyService != null)
                {
                    await ProxyService.ShutdownAsync(context).IgnoreFailure();
                }

                if (ContentCacheService != null)
                {
                    await ContentCacheService.ShutdownAsync(context).IgnoreFailure();
                }

                WebHost?.Dispose();
            }
        }
    }
}
