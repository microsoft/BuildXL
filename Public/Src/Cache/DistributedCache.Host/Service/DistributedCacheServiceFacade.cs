// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Cache.Logging;
using BuildXL.Cache.Logging.External;

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
        public static async Task RunAsync(DistributedCacheServiceArguments arguments)
        {
            // Switching to another thread.
            await Task.Yield();

            var host = arguments.Host;

            // NOTE(jubayard): this is the entry point for running CASaaS. At this point, the Logger inside the
            // arguments holds the client's implementation of our logging interface ILogger. Here, we may override the
            // client's decision with our own.
            // The disposableToken helps ensure that we shutdown properly and all logs are sent to their final
            // destination.
            var loggerReplacement = CreateReplacementLogger(arguments);
            arguments.Logger = loggerReplacement.Logger;
            using var disposableToken = loggerReplacement.DisposableToken;

            if (arguments.BuildCopyInfrastructure != null)
            {
                var (copier, pathTransformer, copyRequester) = arguments.BuildCopyInfrastructure(arguments.Logger);
                arguments.Copier = copier;
                arguments.PathTransformer = pathTransformer;
                arguments.CopyRequester = copyRequester;
            }

            var logger = arguments.Logger;

            var factory = new CacheServerFactory(arguments);

            await host.OnStartingServiceAsync();

            // Technically, this method doesn't own the file copier, but no one actually owns it.
            // So to clean up the resources (and print some stats) we dispose it here.
            using (arguments.Copier as IDisposable)
            using (var server = factory.Create())
            {
                var context = new Context(logger);

                try
                {
                    var startupResult = await server.StartupAsync(context);
                    if (!startupResult)
                    {
                        throw new CacheException(startupResult.ToString());
                    }

                    host.OnStartedService();

                    logger.Info("Service started");

                    await arguments.Cancellation.WaitForCancellationAsync();

                    logger.Always("Exit event set");
                }
                finally
                {
                    var timeoutInMinutes = arguments?.Configuration?.DistributedContentSettings?.MaxShutdownDurationInMinutes ?? 30;
                    BoolResult result = await ShutdownWithTimeoutAsync(context, server, TimeSpan.FromMinutes(timeoutInMinutes));
                    if (!result)
                    {
                        logger.Warning("Failed to shutdown local content server: {0}", result);
                    }

                    host.OnTeardownCompleted();
                }
            }
        }

        private static async Task<BoolResult> ShutdownWithTimeoutAsync(Context context, IStartupShutdownSlim server, TimeSpan timeout)
        {
            var shutdownTask = server.ShutdownAsync(context);
            if (await Task.WhenAny(shutdownTask, Task.Delay(timeout)) != shutdownTask)
            {
                return new BoolResult($"Server shutdown didn't finished after '{timeout}'.");
            }

            // shutdownTask is done already. Just getting the result out of it.
            return await shutdownTask;
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

        private static ILogger CreateNLogAdapter(OperationContext operationContext, DistributedCacheServiceArguments arguments)
        {
            Contract.RequiresNotNull(arguments.Configuration.LoggingSettings);

            // This is done for performance. See: https://github.com/NLog/NLog/wiki/performance#configure-nlog-to-not-scan-for-assemblies
            NLog.Config.ConfigurationItemFactory.Default = new NLog.Config.ConfigurationItemFactory(typeof(NLog.ILogger).GetTypeInfo().Assembly);

            // This is needed for dependency ingestion. See: https://github.com/NLog/NLog/wiki/Dependency-injection-with-NLog
            // The issue is that we need to construct a log, which requires access to both our config and the host. It
            // seems too much to put it into the AzureBlobStorageLogTarget itself, so we do it here.
            var numBlobStorageTargets = 0;
            var defaultConstructor = NLog.Config.ConfigurationItemFactory.Default.CreateInstance;
            NLog.Config.ConfigurationItemFactory.Default.CreateInstance = type =>
            {
                if (type == typeof(AzureBlobStorageLogTarget))
                {
                    // There shouldn't be more than one per instantiation, just a smoke test.
                    Contract.Assert(numBlobStorageTargets == 0);
                    var log = CreateAzureBlobStorageLogAsync(operationContext, arguments, arguments.Configuration.LoggingSettings.Configuration).Result;
                    var target = new AzureBlobStorageLogTarget(log);
                    numBlobStorageTargets++;
                    return target;
                }

                return defaultConstructor(type);
            };

            NLog.Targets.Target.Register<AzureBlobStorageLogTarget>(nameof(AzureBlobStorageLogTarget));

            // This is done in order to allow our logging configuration to access key telemetry information.
            var telemetryFieldsProvider = arguments.TelemetryFieldsProvider;
            NLog.LayoutRenderers.LayoutRenderer.Register("BuildId", _ => telemetryFieldsProvider.BuildId);
            NLog.LayoutRenderers.LayoutRenderer.Register("APCluster", _ => telemetryFieldsProvider.APCluster);
            NLog.LayoutRenderers.LayoutRenderer.Register("APMachineFunction", _ => telemetryFieldsProvider.APMachineFunction);
            NLog.LayoutRenderers.LayoutRenderer.Register("MachineName", _ => telemetryFieldsProvider.MachineName);
            NLog.LayoutRenderers.LayoutRenderer.Register("ServiceName", _ => telemetryFieldsProvider.ServiceName);
            NLog.LayoutRenderers.LayoutRenderer.Register("ServiceVersion", _ => telemetryFieldsProvider.ServiceVersion);
            NLog.LayoutRenderers.LayoutRenderer.Register("Stamp", _ => telemetryFieldsProvider.Stamp);
            NLog.LayoutRenderers.LayoutRenderer.Register("Ring", _ => telemetryFieldsProvider.Ring);
            NLog.LayoutRenderers.LayoutRenderer.Register("ConfigurationId", _ => telemetryFieldsProvider.ConfigurationId);
            NLog.LayoutRenderers.LayoutRenderer.Register("CacheVersion", _ => Utilities.Branding.Version);

            // Follows ISO8601 without timezone specification.
            // See: https://kusto.azurewebsites.net/docs/query/scalar-data-types/datetime.html
            // See: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings?view=netframework-4.8#the-round-trip-o-o-format-specifier
            var processStartTimeUtc = SystemClock.Instance.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            NLog.LayoutRenderers.LayoutRenderer.Register("ProcessStartTimeUtc", _ => processStartTimeUtc);

            var configuration = new NLog.Config.XmlLoggingConfiguration(arguments.Configuration.LoggingSettings.NLogConfigurationPath);

            return new NLogAdapter(configuration);
        }

        private static async Task<AzureBlobStorageLog> CreateAzureBlobStorageLogAsync(OperationContext operationContext, DistributedCacheServiceArguments arguments, AzureBlobStorageLogPublicConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration);

            // There is a big issue here: on the one hand, we'd like to be able to configure everything from the XML
            // instead of our JSON configuration, simply because the XML is self-contained. On the other hand, the XML
            // will likely be shared across all stamps, so there's no "stamp-specific" configuration in there. That
            // means all stamp-level configuration must be done through the JSON.

            AzureBlobStorageCredentials credentials = null;

            if (configuration.UseSasTokens)
            {
                var secrets = await arguments.Host.RetrieveSecretsAsync(new List<RetrieveSecretsRequest>()
                {
                    new RetrieveSecretsRequest(configuration.SecretName, SecretKind.SasToken)
                }, token: operationContext.Token);

                credentials = new AzureBlobStorageCredentials((UpdatingSasToken)secrets[configuration.SecretName]);
            }
            else
            {
                var secrets = await arguments.Host.RetrieveSecretsAsync(new List<RetrieveSecretsRequest>()
                {
                    new RetrieveSecretsRequest(configuration.SecretName, SecretKind.PlainText)
                }, token: operationContext.Token);

                credentials = new AzureBlobStorageCredentials((PlainTextSecret)secrets[configuration.SecretName]);
            }

            var azureBlobStorageLogConfiguration = ToInternalConfiguration(configuration);

            var azureBlobStorageLog = new AzureBlobStorageLog(
                configuration: azureBlobStorageLogConfiguration,
                context: operationContext,
                clock: SystemClock.Instance,
                fileSystem: new PassThroughFileSystem(),
                telemetryFieldsProvider: arguments.TelemetryFieldsProvider,
                credentials: credentials);

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
    }
}
