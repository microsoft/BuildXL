// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Cache.Logging;
using BuildXL.Cache.Logging.External;
using static BuildXL.Utilities.ConfigurationHelper;

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
                pathTransformer: null,
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

        private static (IAbsolutePathRemoteFileCopier copier, IAbsolutePathTransformer pathTransformer, IContentCommunicationManager copyRequester)
            BuildCopyInfrastructure(ILogger logger, DistributedCacheServiceConfiguration config)
        {
            var dcs = config.DistributedContentSettings;


            var grpcCopyClientConfiguration = new GrpcCopyClientConfiguration();
            ApplyIfNotNull(dcs.GrpcCopyClientBufferSizeBytes, v => grpcCopyClientConfiguration.ClientBufferSizeBytes = v);
            ApplyIfNotNull(dcs.GrpcCopyClientUseGzipCompression, v => grpcCopyClientConfiguration.UseGzipCompression = v);
            ApplyIfNotNull(dcs.GrpcCopyClientConnectOnStartup, v => grpcCopyClientConfiguration.ConnectOnStartup = v);
            ApplyIfNotNull(dcs.GrpcCopyClientConnectionEstablishmentTimeoutSeconds, v => grpcCopyClientConfiguration.ConnectionEstablishmentTimeout = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(dcs.GrpcCopyClientDisconnectionTimeoutSeconds, v => grpcCopyClientConfiguration.DisconnectionTimeout = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(dcs.GrpcCopyClientConnectionTimeoutSeconds, v => grpcCopyClientConfiguration.ConnectionTimeout = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(dcs.GrpcCopyClientOperationDeadlineSeconds, v => grpcCopyClientConfiguration.OperationDeadline = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(dcs.GrpcCopyClientGrpcCoreClientOptions, v => grpcCopyClientConfiguration.GrpcCoreClientOptions = v);

            var resourcePoolConfiguration = new ResourcePoolConfiguration();
            ApplyIfNotNull(dcs.MaxGrpcClientCount, v => resourcePoolConfiguration.MaximumResourceCount = v);
            ApplyIfNotNull(dcs.MaxGrpcClientAgeMinutes, v => resourcePoolConfiguration.MaximumAge = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(dcs.GrpcCopyClientCacheGarbageCollectionPeriodMinutes, v => resourcePoolConfiguration.GarbageCollectionPeriod = TimeSpan.FromMinutes(v));

            // We don't have to dispose the copier here. RunAsync will take care of that.
            var grpcCopyClientCacheConfiguration = new GrpcCopyClientCacheConfiguration()
            {
                ResourcePoolConfiguration = resourcePoolConfiguration,
                GrpcCopyClientConfiguration = grpcCopyClientConfiguration,
            };
            ApplyIfNotNull(dcs.GrpcCopyClientCacheResourcePoolVersion, v => grpcCopyClientCacheConfiguration.ResourcePoolVersion = (GrpcCopyClientCacheConfiguration.PoolVersion)v);

            var grpcFileCopierConfiguration = new GrpcFileCopierConfiguration()
            {
                GrpcPort = (int)config.LocalCasSettings.ServiceSettings.GrpcPort,
                GrpcCopyClientCacheConfiguration = grpcCopyClientCacheConfiguration,
            };
            ApplyIfNotNull(dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy, v => {
                if (!Enum.TryParse<GrpcFileCopierConfiguration.ClientInvalidationPolicy>(v, out var parsed))
                {
                    throw new ArgumentException($"Failed to parse `{nameof(dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy)}` setting with value `{dcs.GrpcFileCopierGrpcCopyClientInvalidationPolicy}` into type `{nameof(GrpcFileCopierConfiguration.ClientInvalidationPolicy)}`");
                }

                grpcFileCopierConfiguration.GrpcCopyClientInvalidationPolicy = parsed;
            });

            var grpcFileCopier = new GrpcFileCopier(
                    new Context(logger),
                    grpcFileCopierConfiguration);

            return (
                    copier: grpcFileCopier,
                    pathTransformer: new GrpcDistributedPathTransformer(
                        junctionsByDirectory: dcs.GetAutopilotAlternateDriveMap(),
                        logger: logger),
                    copyRequester: grpcFileCopier
                );
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

            // NOTE(jubayard): this is the entry point for running CASaaS. At this point, the Logger inside the
            // arguments holds the client's implementation of our logging interface ILogger. Here, we may override the
            // client's decision with our own.
            // The disposableToken helps ensure that we shutdown properly and all logs are sent to their final
            // destination.
            var loggerReplacement = CreateReplacementLogger(arguments);
            arguments.Logger = loggerReplacement.Logger;
            using var disposableToken = loggerReplacement.DisposableToken;

            var context = new Context(arguments.Logger);

            InitializeActivityTrackerIfNeeded(context, arguments.Configuration.DistributedContentSettings);

            AdjustCopyInfrastructure(arguments);

            await ReportStartingServiceAsync(context, host);

            var factory = new CacheServerFactory(arguments);

            var operationContext = new OperationContext(context);
            ServiceOfflineDurationTracker serviceRunningTracker = null;

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

                    var serviceRunningTrackerResult = ServiceOfflineDurationTracker.Create(
                        operationContext,
                        SystemClock.Instance,
                        new PassThroughFileSystem(),
                        arguments.Configuration);

                    ReportServiceStarted(operationContext, host, serviceRunningTrackerResult, timer.Elapsed);

                    serviceRunningTracker = serviceRunningTrackerResult.GetValueOrDefault();
                    await arguments.Cancellation.WaitForCancellationAsync();
                    ReportShuttingDownService(context);
                }
                catch (Exception e)
                {
                    ReportServiceStartupFailed(context, e, timer.Elapsed);
                    throw;
                }
                finally
                {
                    // Writing the current time before shutting the service down.
                    serviceRunningTracker?.LogCurrentTimeStampToFile(operationContext);

                    timer.Reset();
                    var timeoutInMinutes = arguments.Configuration?.DistributedContentSettings?.MaxShutdownDurationInMinutes ?? 30;
                    var result = await server
                        .ShutdownAsync(context)
                        .WithTimeoutAsync("Server shutdown", TimeSpan.FromMinutes(timeoutInMinutes));

                    serviceRunningTracker?.Dispose();
                    ReportServiceStopped(context, host, result, timer.Elapsed);
                }
            }
        }

        private static void AdjustCopyInfrastructure(DistributedCacheServiceArguments arguments)
        {
            if (arguments.BuildCopyInfrastructure != null)
            {
                var (copier, pathTransformer, copyRequester) = arguments.BuildCopyInfrastructure(arguments.Logger);
                arguments.Copier = copier;
                arguments.PathTransformer = pathTransformer;
                arguments.CopyRequester = copyRequester;
            }
        }

        private static void ReportServiceStartupFailed(Context context, Exception exception, TimeSpan startupDuration)
        {
            LifetimeTrackerTracer.ServiceStartupFailed(context, exception, startupDuration);
        }

        private static Task ReportStartingServiceAsync(Context context, IDistributedCacheServiceHost host)
        {
            LifetimeTrackerTracer.StartingService(context);
            return host.OnStartingServiceAsync();
        }

        private static void ReportServiceStarted(
            OperationContext context,
            IDistributedCacheServiceHost host,
            Result<ServiceOfflineDurationTracker> trackerResult,
            TimeSpan startupDuration)
        {
            var shutdownTimeUtc = trackerResult.Then(v => v.GetShutdownTime(context, logTimeStampToFile: true));
            LifetimeTrackerTracer.ServiceStarted(context, startupDuration, shutdownTimeUtc);
            host.OnStartedService();
        }

        private static void ReportShuttingDownService(Context context)
        {
            LifetimeTrackerTracer.ShuttingDownService(context);
        }

        private static void ReportServiceStopped(Context context, IDistributedCacheServiceHost host, BoolResult result, TimeSpan shutdownDuration)
        {
            CacheActivityTracker.Stop();
            LifetimeTrackerTracer.ServiceStopped(context, result, shutdownDuration);
            host.OnTeardownCompleted();
        }

        /// <summary>
        ///     This method allows CASaaS to replace the host's logger for our own logger.
        /// </summary>
        /// <remarks>
        ///     Since we don't perform any kind of stats aggregation on our side (i.e. statsd, MDM, etc), we rely on
        ///     the host to do it. This is done through <see cref="MetricsAdapter"/>.
        ///
        ///     The situation with respect to shutdown is a little bit odd: we create a custom target, which holds some
        ///     managed resources that need to be released (because this release ensures any remaining logs will be
        ///     sent to Kusto).
        ///     NLog will make sure to dispose those resources when we shut it down, but we may actually return the
        ///     host's logger, in which case we don't consider that we own it, because clean up may happen on whatever
        ///     code is actually using us, so we don't want to dispose in that case.
        /// </remarks>
        private static (ILogger Logger, IDisposable DisposableToken) CreateReplacementLogger(DistributedCacheServiceArguments arguments)
        {
            var logger = arguments.Logger;

            var loggingSettings = arguments.Configuration.LoggingSettings;
            if (string.IsNullOrEmpty(loggingSettings?.NLogConfigurationPath))
            {
                return (logger, null);
            }

            Contract.RequiresNotNull(arguments.TelemetryFieldsProvider);

            // This context is associated to the host's logger. In this way, we can make sure that if we have any
            // issues with our logging, we can always go and read the host's logs to figure out what's going on.
            var context = new Context(logger);
            var operationContext = new OperationContext(context);

            context.Info($"Replacing cache logger for NLog-based implementation using configuration file at `{loggingSettings.NLogConfigurationPath}`");

            try
            {
                var nLogAdapter = CreateNLogAdapter(operationContext, arguments);
                if (arguments.Logger is IOperationLogger operationLogger)
                {
                    // NOTE(jubayard): the MetricsAdapter doesn't own the loggers, and hence won't dispose them. This
                    // means we don't change the disposableToken.
                    var wrapper = new MetricsAdapter(nLogAdapter, operationLogger);
                    return (wrapper, nLogAdapter);
                }

                return (nLogAdapter, nLogAdapter);
            }
            catch (Exception e)
            {
                context.Error($"Failed to instantiate NLog-based logger with error: {e}");
                return (logger, null);
            }
        }

        private static IStructuredLogger CreateNLogAdapter(OperationContext operationContext, DistributedCacheServiceArguments arguments)
        {
            Contract.RequiresNotNull(arguments.Configuration.LoggingSettings);

            // This is done for performance. See: https://github.com/NLog/NLog/wiki/performance#configure-nlog-to-not-scan-for-assemblies
            NLog.Config.ConfigurationItemFactory.Default = new NLog.Config.ConfigurationItemFactory(typeof(NLog.ILogger).GetTypeInfo().Assembly);

            // This is needed for dependency ingestion. See: https://github.com/NLog/NLog/wiki/Dependency-injection-with-NLog
            // The issue is that we need to construct a log, which requires access to both our config and the host. It
            // seems too much to put it into the AzureBlobStorageLogTarget itself, so we do it here.
            var defaultConstructor = NLog.Config.ConfigurationItemFactory.Default.CreateInstance;
            NLog.Config.ConfigurationItemFactory.Default.CreateInstance = type =>
            {
                if (type == typeof(AzureBlobStorageLogTarget))
                {
                    var log = CreateAzureBlobStorageLogAsync(operationContext, arguments, arguments.Configuration.LoggingSettings.Configuration).Result;
                    var target = new AzureBlobStorageLogTarget(log);
                    return target;
                }

                return defaultConstructor(type);
            };

            NLog.Targets.Target.Register<AzureBlobStorageLogTarget>(nameof(AzureBlobStorageLogTarget));

            // This is done in order to allow our logging configuration to access key telemetry information.
            var telemetryFieldsProvider = arguments.TelemetryFieldsProvider;

            NLog.LayoutRenderers.LayoutRenderer.Register("APEnvironment", _ => telemetryFieldsProvider.APEnvironment);
            NLog.LayoutRenderers.LayoutRenderer.Register("APCluster", _ => telemetryFieldsProvider.APCluster);
            NLog.LayoutRenderers.LayoutRenderer.Register("APMachineFunction", _ => telemetryFieldsProvider.APMachineFunction);
            NLog.LayoutRenderers.LayoutRenderer.Register("MachineName", _ => telemetryFieldsProvider.MachineName);
            NLog.LayoutRenderers.LayoutRenderer.Register("ServiceName", _ => telemetryFieldsProvider.ServiceName);
            NLog.LayoutRenderers.LayoutRenderer.Register("ServiceVersion", _ => telemetryFieldsProvider.ServiceVersion);
            NLog.LayoutRenderers.LayoutRenderer.Register("Stamp", _ => telemetryFieldsProvider.Stamp);
            NLog.LayoutRenderers.LayoutRenderer.Register("Ring", _ => telemetryFieldsProvider.Ring);
            NLog.LayoutRenderers.LayoutRenderer.Register("ConfigurationId", _ => telemetryFieldsProvider.ConfigurationId);
            NLog.LayoutRenderers.LayoutRenderer.Register("CacheVersion", _ => Utilities.Branding.Version);

            NLog.LayoutRenderers.LayoutRenderer.Register("Role", _ => GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole));
            NLog.LayoutRenderers.LayoutRenderer.Register("BuildId", _ => GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.BuildId));

            // Follows ISO8601 without timezone specification.
            // See: https://kusto.azurewebsites.net/docs/query/scalar-data-types/datetime.html
            // See: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings?view=netframework-4.8#the-round-trip-o-o-format-specifier
            var processStartTimeUtc = SystemClock.Instance.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            NLog.LayoutRenderers.LayoutRenderer.Register("ProcessStartTimeUtc", _ => processStartTimeUtc);

            var configuration = new NLog.Config.XmlLoggingConfiguration(arguments.Configuration.LoggingSettings.NLogConfigurationPath);

            return new NLogAdapter(operationContext.TracingContext.Logger, configuration);
        }

        private static async Task<AzureBlobStorageLog> CreateAzureBlobStorageLogAsync(OperationContext operationContext, DistributedCacheServiceArguments arguments, AzureBlobStorageLogPublicConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration);

            // There is a big issue here: on the one hand, we'd like to be able to configure everything from the XML
            // instead of our JSON configuration, simply because the XML is self-contained. On the other hand, the XML
            // will likely be shared across all stamps, so there's no "stamp-specific" configuration in there. That
            // means all stamp-level configuration must be done through the JSON.

            AzureBlobStorageCredentials credentials = await arguments.Host.GetBlobCredentialsAsync(
                configuration.SecretName, 
                configuration.UseSasTokens, 
                operationContext.Token);

            var azureBlobStorageLogConfiguration = ToInternalConfiguration(configuration);

            var azureBlobStorageLog = new AzureBlobStorageLog(
                configuration: azureBlobStorageLogConfiguration,
                context: operationContext,
                clock: SystemClock.Instance,
                fileSystem: new PassThroughFileSystem(),
                telemetryFieldsProvider: arguments.TelemetryFieldsProvider,
                credentials: credentials,
                additionalBlobMetadata: null);

            await azureBlobStorageLog.StartupAsync().ThrowIfFailure();

            return azureBlobStorageLog;
        }

        private static AzureBlobStorageLogConfiguration ToInternalConfiguration(AzureBlobStorageLogPublicConfiguration configuration)
        {
            Contract.RequiresNotNullOrEmpty(configuration.SecretName);
            Contract.RequiresNotNullOrEmpty(configuration.WorkspaceFolderPath);

            var result = new AzureBlobStorageLogConfiguration(new ContentStore.Interfaces.FileSystem.AbsolutePath(configuration.WorkspaceFolderPath));

            if (configuration.ContainerName != null)
            {
                result.ContainerName = configuration.ContainerName;
            }

            if (configuration.WriteMaxDegreeOfParallelism != null)
            {
                result.WriteMaxDegreeOfParallelism = configuration.WriteMaxDegreeOfParallelism.Value;
            }

            if (configuration.WriteMaxIntervalSeconds != null)
            {
                result.WriteMaxInterval = TimeSpan.FromSeconds(configuration.WriteMaxIntervalSeconds.Value);
            }

            if (configuration.WriteMaxBatchSize != null)
            {
                result.WriteMaxBatchSize = configuration.WriteMaxBatchSize.Value;
            }

            if (configuration.UploadMaxDegreeOfParallelism != null)
            {
                result.UploadMaxDegreeOfParallelism = configuration.UploadMaxDegreeOfParallelism.Value;
            }

            if (configuration.UploadMaxIntervalSeconds != null)
            {
                result.UploadMaxInterval = TimeSpan.FromSeconds(configuration.UploadMaxIntervalSeconds.Value);
            }

            return result;
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
