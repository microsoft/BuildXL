// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Top level entry point for creating and running a distributed cache service
    /// </summary>
    public static class DistributedCacheServiceFacade
    {
        /// <summary>
        /// Creates and runs a distributed cache service
        /// </summary>
        /// <exception cref="CacheException">thrown when cache startup fails</exception>
        public static Task RunWithConfigurationAsync(
            ILogger logger,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            DistributedCacheServiceConfiguration config,
            CancellationToken token,
            string keyspace = null)
        {
            logger.Info($"CAS log severity set to {config.MinimumLogSeverity}");
           
            var arguments = new DistributedCacheServiceArguments(
                logger: logger,
                copier: null,
                copyRequester: null,
                host: host,
                hostInfo: hostInfo,
                cancellation: token,
                dataRootPath: config.DataRootPath,
                configuration: config,
                keyspace: keyspace)
            {
                TelemetryFieldsProvider = telemetryFieldsProvider,
                BuildCopyInfrastructure = logger => BuildCopyInfrastructure(logger, config),
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
            var loggerReplacement = LoggerFactory.CreateReplacementLogger(arguments);
            arguments.Logger = loggerReplacement.Logger;
            using var disposableToken = loggerReplacement.DisposableToken;

            var context = new Context(arguments.Logger);
            var operationContext = new OperationContext(context, arguments.Cancellation);

            InitializeActivityTrackerIfNeeded(context, arguments.Configuration.DistributedContentSettings);

            AdjustCopyInfrastructure(arguments);

            await ReportStartingServiceAsync(operationContext, host, arguments);

            var factory = new CacheServerFactory(arguments);

            // Technically, this method doesn't own the file copier, but no one actually owns it.
            // So to clean up the resources (and print some stats) we dispose it here.
            using (arguments.Copier as IDisposable)
            using (var server = factory.Create())
            {
                try
                {
                    var startupResult = await server.StartupAsync(context);
                    if (!startupResult)
                    {
                        throw new CacheException(startupResult.ToString());
                    }

                    await ReportServiceStartedAsync(operationContext, server, host);
                    using var cancellationAwaiter = arguments.Cancellation.ToAwaitable();
                    await cancellationAwaiter.CompletionTask;
                    await ReportShuttingDownServiceAsync(operationContext, host);
                }
                catch (Exception e)
                {
                    ReportServiceStartupFailed(context, e, timer.Elapsed);
                    throw;
                }
                finally
                {
                    var timeoutInMinutes = arguments.Configuration?.DistributedContentSettings?.MaxShutdownDurationInMinutes ?? 5;
                    var result = await server
                        .ShutdownAsync(context)
                        .WithTimeoutAsync("Server shutdown", TimeSpan.FromMinutes(timeoutInMinutes));
                    ReportServiceStopped(context, host, result);
                }
            }
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
            
            var logFilePath = GetPathForLifetimeTracking(configuration);
            
            LifetimeTracker.ServiceStarting(context, logInterval, logFilePath, arguments.TelemetryFieldsProvider.ServiceName);

            return host.OnStartingServiceAsync();
        }

        private static AbsolutePath GetPathForLifetimeTracking(DistributedCacheServiceConfiguration configuration) => configuration.LocalCasSettings.GetCacheRootPathWithScenario(LocalCasServiceSettings.DefaultCacheName);

        private static async Task ReportServiceStartedAsync(
            OperationContext context,
            StartupShutdownSlimBase server,
            IDistributedCacheServiceHost host)
        {
            LifetimeTracker.ServiceStarted(context);
            host.OnStartedService();

            if (host is IDistributedCacheServiceHostInternal hostInternal
                && server is IServicesProvider sp
                && sp.TryGetService<ICacheServerServices>(out var services))
            {
                await hostInternal.OnStartedServiceAsync(context, services);
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
            if (settings.EnableCacheActivityTracker)
            {
                CacheActivityTracker.Start(context, SystemClock.Instance, settings.TrackingActivityWindow, settings.TrackingSnapshotPeriod, settings.TrackingReportPeriod);
            }
        }

        private class DistributedCacheServiceHostBasedLifetimeManager : ILifetimeManager
        {
            private readonly IDistributedCacheServiceHost _host;

            public DistributedCacheServiceHostBasedLifetimeManager(IDistributedCacheServiceHost host) => _host = host;

            public void RequestTeardown(string reason) => _host.RequestTeardown(reason);
        }
    }
}
