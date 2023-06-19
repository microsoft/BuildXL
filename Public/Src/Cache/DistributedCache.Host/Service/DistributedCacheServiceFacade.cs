// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Core.Tasks;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Top level entry point for creating and running a distributed cache service
    /// </summary>
    public static class DistributedCacheServiceFacade
    {
        private static Tracer _tracer = new Tracer(nameof(DistributedCacheServiceFacade));

        /// <summary>
        /// Creates and runs a distributed cache service
        /// </summary>
        /// <exception cref="CacheException">thrown when cache startup fails</exception>
        public static Task RunWithConfigurationAsync(
            Context tracingContext,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            DistributedCacheServiceConfiguration config,
            CancellationToken token,
            string? keySpace = null)
        {
            return RunWithConfigurationAsync(tracingContext, host, grpcHost: null, hostInfo, telemetryFieldsProvider, config, token, keySpace);
        }

        /// <summary>
        /// Creates and runs a distributed cache service
        /// </summary>
        /// <exception cref="CacheException">thrown when cache startup fails</exception>
        public static Task RunWithConfigurationAsync(
            Context tracingContext,
            IDistributedCacheServiceHost host,
            IGrpcServerHost<LocalServerConfiguration>? grpcHost,
            HostInfo hostInfo,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            DistributedCacheServiceConfiguration config,
            CancellationToken token,
            string? keySpace = null)
        {
            tracingContext.Logger.Info($"CAS log severity set to {config.MinimumLogSeverity}");
           
            var arguments = new DistributedCacheServiceArguments(
                tracingContext: tracingContext,
                telemetryFieldsProvider,
                copier: null,
                copyRequester: null,
                host: host,
                hostInfo: hostInfo,
                cancellation: token,
                dataRootPath: config.DataRootPath,
                configuration: config,
                keyspace: keySpace)
            {
                BuildCopyInfrastructure = logger => BuildCopyInfrastructure(logger, config),
                GrpcHost = grpcHost,
            };

            return RunAsync(arguments);
        }

        private static (IRemoteFileCopier copier, IContentCommunicationManager copyRequester) BuildCopyInfrastructure(ILogger logger, DistributedCacheServiceConfiguration config)
        {
            var grpcFileCopierConfiguration = GrpcFileCopierConfiguration.FromDistributedContentSettings(
                config.DistributedContentSettings, (int)config.LocalCasSettings.ServiceSettings.GrpcPort);

            var grpcFileCopier = new GrpcFileCopier(new Context(logger), grpcFileCopierConfiguration);

            return (copier: grpcFileCopier, copyRequester: grpcFileCopier);
        }

        /// <summary>
        /// Creates and runs a distributed cache service
        /// </summary>
        /// <exception cref="CacheException">thrown when cache startup fails</exception>
        public static async Task RunAsync(DistributedCacheServiceArguments arguments)
        {
            // Switching to another thread.
            await Task.Yield();

            var host = arguments.Host;
            var timer = Stopwatch.StartNew();

            InitializeLifetimeTracker(host);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(arguments.Cancellation);
            arguments.Cancellation = cts.Token;

            if (arguments.Configuration.RespectRequestTeardown)
            {
                LifetimeManager.OnTeardownRequested += _ =>
                {
                    cts.Cancel();
                };
            }
            
            // NOTE(jubayard): this is the entry point for running CASaaS. At this point, the Logger inside the
            // arguments holds the client's implementation of our logging interface ILogger. Here, we may override the
            // client's decision with our own.
            // The disposableToken helps ensure that we shutdown properly and all logs are sent to their final
            // destination.
            var loggerReplacement = await LoggerFactory.ReplaceLoggerAsync(AdjustLoggingConfigurationIfNeeded(arguments));

            using var disposableToken = loggerReplacement.DisposableToken;

            var context = new Context(arguments.Logger);
            var operationContext = new OperationContext(context, arguments.Cancellation);

            var distributedSettings = arguments.Configuration.DistributedContentSettings;
            InitializeActivityTrackerIfNeeded(context, distributedSettings);

            AdjustCopyInfrastructure(arguments);

            await ReportStartingServiceAsync(operationContext, host, arguments);

            var factory = new CacheServerFactory(arguments);

            // Technically, this method doesn't own the file copier, but no one actually owns it.
            // So to clean up the resources (and print some stats) we dispose it here.
            using (arguments.Copier as IDisposable)
            using (var server = await factory.CreateAsync(operationContext))
            {
                try
                {
                    var startupResult = await server.StartupAsync(context);
                    if (!startupResult)
                    {
                        throw new CacheException(startupResult.ToString());
                    }

                    await ReportServiceStartedAsync(operationContext, server, host, distributedSettings);
                    using var cancellationAwaiter = arguments.Cancellation.ToAwaitable();
                    await cancellationAwaiter.CompletionTask;
                    await ReportShuttingDownServiceAsync(operationContext, host);
                }
                catch (Exception exception)
                {
                    ReportServiceStartupFailed(context, exception, timer.Elapsed);
                    throw;
                }
                finally
                {
                    var timeoutInMinutes = distributedSettings?.MaxShutdownDurationInMinutes ?? 5;
                    var result = await server
                        .ShutdownAsync(context)
                        .WithTimeoutAsync("Server shutdown", TimeSpan.FromMinutes(timeoutInMinutes));
                    ReportServiceStopped(context, host, result);
                }
            }
        }

        /// <summary>
        /// For out-of-proc case the logging settings should be adjusted to avoid writing the logs for the parent and the child processes into the same file.
        /// </summary>
        /// <remarks>
        /// This is a temporary work-around. The work-item for fixing it: 1939847
        /// </remarks>
        private static DistributedCacheServiceArguments AdjustLoggingConfigurationIfNeeded(DistributedCacheServiceArguments arguments)
        {
            bool isLauncher = arguments.Configuration.DistributedContentSettings?.OutOfProcChildProcess != true;
            var logFileReplacements = arguments.Configuration.DistributedContentSettings?.OutOfProcCacheSettings?.NLogConfigurationReplacements;
            var count = arguments.LoggingSettings?.NLogConfigurationReplacements.Count;

            if (isLauncher
                && logFileReplacements?.Count > 0
                && count is null or 0)
            {
                var replacementsAsString = string.Join(", ", logFileReplacements.Select(kvp => $"[{kvp.Key}]: {kvp.Value}"));
                arguments.Logger.Debug($"Adjusting logging configuration. LogFileReplacements: {replacementsAsString}");

                // Adding the replacement configuration for the log file name if the launcher is enabled, and
                // only if the replacement configuration is not set.
                arguments.LoggingSettings!.NLogConfigurationReplacements = logFileReplacements;
            }

            return arguments;
        }

        private static void AdjustCopyInfrastructure(DistributedCacheServiceArguments arguments)
        {
            if (arguments.BuildCopyInfrastructure != null)
            {
                var (copier, copyRequester) = arguments.BuildCopyInfrastructure(arguments.Logger);
                arguments.Copier = copier;
                arguments.CopyRequester = copyRequester;
            }
        }

        private static void ReportServiceStartupFailed(Context context, Exception exception, TimeSpan startupDuration)
        {
            LifetimeTracker.ServiceStartupFailed(context, exception, startupDuration);
        }

        private static Task ReportStartingServiceAsync(
            OperationContext context,
            IDistributedCacheServiceHost host,
            DistributedCacheServiceArguments arguments)
        {
            var configuration = arguments.Configuration;
            var logIntervalSeconds = configuration.DistributedContentSettings.ServiceRunningLogInSeconds;
            var logInterval = logIntervalSeconds != null ? (TimeSpan?)TimeSpan.FromSeconds(logIntervalSeconds.Value) : null;

            if (arguments.TraceConfiguration)
            {
                context.TracingContext.Debug(
                    JsonUtilities.JsonSerialize(new ConfigurationDescriptor(arguments.HostInfo.Parameters, arguments.Configuration), indent: true),
                    nameof(DistributedCacheServiceFacade),
                    operation: "StartupConfiguration");
            }
            
            var logFilePath = GetPathForLifetimeTracking(configuration);
            
            LifetimeTracker.ServiceStarting(context, logInterval, logFilePath, arguments.TelemetryFieldsProvider.ServiceName);

            return host.OnStartingServiceAsync();
        }

        private record ConfigurationDescriptor(HostParameters Parameters, DistributedCacheServiceConfiguration Configuration);

        private static AbsolutePath GetPathForLifetimeTracking(DistributedCacheServiceConfiguration configuration) => configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName);

        private static async Task ReportServiceStartedAsync(
            OperationContext context,
            ICacheServer server,
            IDistributedCacheServiceHost host,
            DistributedContentSettings distributedContentSettings)
        {
            LifetimeTracker.ServiceStarted(context);
            host.OnStartedService();

            // Calling the internal host callback for the launcher case only
            // in order to initialize the gRPC.NET services and controllers.
            if (distributedContentSettings.OutOfProcCacheSettings is null
                && host is IDistributedCacheServiceHostInternal hostInternal)
            {
                _tracer.Debug(context, $"Reporting OnStartedServiceAsync on {hostInternal.GetType().Name}");
                await hostInternal.OnStartedServiceAsync(context, server);
            }
        }

        private static async Task ReportShuttingDownServiceAsync(OperationContext context, IDistributedCacheServiceHost host)
        {
            LifetimeTracker.ShuttingDownService(context);

            if (host is IDistributedCacheServiceHostInternal hostInternal)
            {
                await hostInternal.OnStoppingServiceAsync(context);
            }
        }

        private static void ReportServiceStopped(Context context, IDistributedCacheServiceHost host, BoolResult result)
        {
            CacheActivityTracker.Stop();
            LifetimeTracker.ServiceStopped(context, result);
            host.OnTeardownCompleted();
        }

        private static void InitializeLifetimeTracker(IDistributedCacheServiceHost host)
        {
            LifetimeManager.SetLifetimeManager(new DistributedCacheServiceHostBasedLifetimeManager(host));
        }

        private static void InitializeActivityTrackerIfNeeded(Context context, DistributedContentSettings settings)
        {
            CacheActivityTracker.Start(context, SystemClock.Instance, settings.CacheActivityTracker);
        }

        private class DistributedCacheServiceHostBasedLifetimeManager : ILifetimeManager
        {
            private readonly IDistributedCacheServiceHost _host;

            public DistributedCacheServiceHostBasedLifetimeManager(IDistributedCacheServiceHost host) => _host = host;

            public void RequestTeardown(string reason) => _host.RequestTeardown(reason);
        }
    }
}
