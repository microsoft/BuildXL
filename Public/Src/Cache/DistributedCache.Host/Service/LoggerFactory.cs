// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using BuildXL.Cache.ContentStore.Distributed;
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
    /// Top level entry point for creating an NLog logger
    /// </summary>
    public static class LoggerFactory
    {
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
        public static (ILogger Logger, IDisposable DisposableToken) CreateReplacementLogger(LoggerFactoryArguments arguments)
        {
            Contract.RequiresNotNull(arguments.TelemetryFieldsProvider);

            var logger = arguments.Logger;

            var loggingSettings = arguments.LoggingSettings;
            if (string.IsNullOrEmpty(loggingSettings?.NLogConfigurationPath))
            {
                return (logger, null);
            }

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

        private static IStructuredLogger CreateNLogAdapter(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            Contract.RequiresNotNull(arguments.LoggingSettings);

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
                    var log = CreateAzureBlobStorageLogAsync(operationContext, arguments).Result;
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

            var configurationContent = File.ReadAllText(arguments.LoggingSettings.NLogConfigurationPath);

            foreach (var replacement in arguments.LoggingSettings.NLogConfigurationReplacements)
            {
                configurationContent = configurationContent.Replace(replacement.Key, replacement.Value);
            }

            var textreader = new StringReader(configurationContent);
            var reader = XmlReader.Create(textreader);

            var configuration = new NLog.Config.XmlLoggingConfiguration(reader, arguments.LoggingSettings.NLogConfigurationPath);

            return new NLogAdapter(operationContext.TracingContext.Logger, configuration);
        }

        private static async Task<AzureBlobStorageLog> CreateAzureBlobStorageLogAsync(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            var configuration = arguments.LoggingSettings.Configuration;
            Contract.AssertNotNull(configuration);

            // There is a big issue here: on the one hand, we'd like to be able to configure everything from the XML
            // instead of our JSON configuration, simply because the XML is self-contained. On the other hand, the XML
            // will likely be shared across all stamps, so there's no "stamp-specific" configuration in there. That
            // means all stamp-level configuration must be done through the JSON.

            AzureBlobStorageCredentials credentials = await arguments.SecretsProvider.GetBlobCredentialsAsync(
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
    }
}
