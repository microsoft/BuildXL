// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Logging;
using BuildXL.Cache.Logging.External;
using BuildXL.Utilities;
using BuildXL.Utilities.ConfigurationHelpers;
using NLog.LayoutRenderers;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Top level entry point for creating an NLog logger
    /// </summary>
    public static class LoggerFactory
    {
        private static readonly Tracer Tracer = new Tracer(nameof(LoggerFactory));

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
        public static (ILogger Logger, IDisposable? DisposableToken) ReplaceLogger(LoggerFactoryArguments arguments)
        {
            var logger = arguments.Logger;

            var loggingSettings = arguments.LoggingSettings;
            if (loggingSettings is null || string.IsNullOrEmpty(loggingSettings.NLogConfigurationPath))
            {
                return (logger, DisposableToken: null);
            }

            // This context is associated to the host's logger. In this way, we can make sure that if we have any
            // issues with our logging, we can always go and read the host's logs to figure out what's going on.
            var context = arguments.TracingContext;
            var operationContext = new OperationContext(context);

            Tracer.Info(context, $"Replacing cache logger for NLog-based implementation using configuration file at `{loggingSettings.NLogConfigurationPath}`");

            try
            {
                var nLogAdapter = CreateNLogAdapter(operationContext, arguments);
                ILogger replacementLogger = nLogAdapter;
                
                if (arguments.Logger is IOperationLogger operationLogger)
                {
                    // NOTE(jubayard): the MetricsAdapter doesn't own the loggers, and hence won't dispose them. This
                    // means we don't change the disposableToken.
                    Tracer.Debug(context, "Creating MetricsAdapter with an existing 'operationLogger'.");
                    replacementLogger = new MetricsAdapter(nLogAdapter, operationLogger);
                }
                // The current implementation now supports the mdm metrics as well.
                else if (!string.IsNullOrEmpty(loggingSettings.MdmAccountName))
                {
                    Tracer.Debug(context, "Creating MetricsLogger with an in-proc MdmOperationLogger.");
                    operationLogger = MdmOperationLogger.Create(context, loggingSettings.MdmAccountName, GetDefaultDimensions(arguments));
                    replacementLogger = new MetricsAdapter(nLogAdapter, operationLogger);
                }

                // Replacing a logger passed to the context to allow the components that saved the context to its internal state
                // to trace messages to Kusto and Mdm as well.
                context.ReplaceLogger(replacementLogger);
                return (replacementLogger, nLogAdapter);
            }
            catch (Exception e)
            {
                Tracer.Error(context, $"Failed to instantiate NLog-based logger with error: {e}");
                return (logger, DisposableToken: null);
            }
        }

        private static List<DefaultDimension> GetDefaultDimensions(LoggerFactoryArguments arguments)
        {
            // This is a set of default dimensions used by all AP services:
            var fieldsProvider = arguments.TelemetryFieldsProvider;
            var result = new List<DefaultDimension>
             {
                 new ("Machine", fieldsProvider.MachineName),
                 new ("ProcessName", AppDomain.CurrentDomain.FriendlyName),
                 new ("Stamp", fieldsProvider.Stamp),
                 new ("Ring", fieldsProvider.Ring),
                 new ("Environment", fieldsProvider.APEnvironment),
                 new ("Cluster", fieldsProvider.APCluster),
                 new ("ServiceVersion", fieldsProvider.ServiceVersion),
             };

            return result
                // Filtering out nulls
                .Where(d => !string.IsNullOrEmpty(d.Value))
                .ToList();
        }

        private static IStructuredLogger CreateNLogAdapter(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            Contract.RequiresNotNull(arguments.LoggingSettings);

            // This is done for performance. See: https://github.com/NLog/NLog/wiki/performance#configure-nlog-to-not-scan-for-assemblies
            NLog.Config.ConfigurationItemFactory.Default = new NLog.Config.ConfigurationItemFactory(typeof(NLog.ILogger).GetTypeInfo().Assembly);

            // This is needed for dependency injection. See: https://github.com/NLog/NLog/wiki/Dependency-injection-with-NLog
            // The issue is that we need to construct a log, which requires access to both our config and the host. It
            // seems too much to put it into the AzureBlobStorageLogTarget itself, so we do it here.
            var defaultConstructor = NLog.Config.ConfigurationItemFactory.Default.CreateInstance;
            NLog.Config.ConfigurationItemFactory.Default.CreateInstance = type =>
            {
                if (type == typeof(AzureBlobStorageLogTarget))
                {
                    var log = CreateAzureBlobStorageLogAsync(operationContext, arguments).GetAwaiter().GetResult();
                    var target = new AzureBlobStorageLogTarget(log);
                    return target;
                }

                return defaultConstructor(type);
            };

            NLog.Targets.Target.Register<AzureBlobStorageLogTarget>(nameof(AzureBlobStorageLogTarget));

            // Using a custom renderer to make the exceptions stack traces clearer.
            LayoutRenderer.Register<DemystifiedExceptionLayoutRenderer>("exception");

            // This is done in order to allow our logging configuration to access key telemetry information.
            var telemetryFieldsProvider = arguments.TelemetryFieldsProvider;

            LayoutRenderer.Register("APEnvironment", _ => telemetryFieldsProvider.APEnvironment);
            LayoutRenderer.Register("APCluster", _ => telemetryFieldsProvider.APCluster);
            LayoutRenderer.Register("APMachineFunction", _ => telemetryFieldsProvider.APMachineFunction);
            LayoutRenderer.Register("MachineName", _ => telemetryFieldsProvider.MachineName);
            LayoutRenderer.Register("ServiceName", _ => telemetryFieldsProvider.ServiceName);
            LayoutRenderer.Register("ServiceVersion", _ => telemetryFieldsProvider.ServiceVersion);
            LayoutRenderer.Register("Stamp", _ => telemetryFieldsProvider.Stamp);
            LayoutRenderer.Register("Ring", _ => telemetryFieldsProvider.Ring);
            LayoutRenderer.Register("ConfigurationId", _ => telemetryFieldsProvider.ConfigurationId);
            LayoutRenderer.Register("CacheVersion", _ => Utilities.Branding.Version);

            LayoutRenderer.Register("Role", _ => GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole));
            LayoutRenderer.Register("BuildId", _ => GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.BuildId));

            // Follows ISO8601 without timezone specification.
            // See: https://kusto.azurewebsites.net/docs/query/scalar-data-types/datetime.html
            // See: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings?view=netframework-4.8#the-round-trip-o-o-format-specifier
            var processStartTimeUtc = SystemClock.Instance.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            NLog.LayoutRenderers.LayoutRenderer.Register("ProcessStartTimeUtc", _ => processStartTimeUtc);

            var configurationContent = File.ReadAllText(arguments.LoggingSettings.NLogConfigurationPath!);

            foreach (var replacement in arguments.LoggingSettings.NLogConfigurationReplacements)
            {
                configurationContent = configurationContent.Replace(replacement.Key, replacement.Value);
            }

            var textReader = new StringReader(configurationContent);
            var reader = XmlReader.Create(textReader);

            var configuration = new NLog.Config.XmlLoggingConfiguration(reader, arguments.LoggingSettings.NLogConfigurationPath);

            return new NLogAdapter(operationContext.TracingContext.Logger, configuration);
        }
        
        private static async Task<AzureBlobStorageLog> CreateAzureBlobStorageLogAsync(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            var configuration = arguments.LoggingSettings?.Configuration;
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
            Contract.RequiresNotNullOrEmpty(configuration.WorkspaceFolderPath);

            var result = new AzureBlobStorageLogConfiguration(new ContentStore.Interfaces.FileSystem.AbsolutePath(configuration.WorkspaceFolderPath));

            configuration.ContainerName.ApplyIfNotNull(v => result.ContainerName = v);
            configuration.WriteMaxDegreeOfParallelism.ApplyIfNotNull(v => result.WriteMaxDegreeOfParallelism = v);
            configuration.WriteMaxIntervalSeconds.ApplyIfNotNull(v => result.WriteMaxInterval = TimeSpan.FromSeconds(v));
            configuration.WriteMaxBatchSize.ApplyIfNotNull(v => result.WriteMaxBatchSize = v);
            configuration.UploadMaxDegreeOfParallelism.ApplyIfNotNull(v => result.UploadMaxDegreeOfParallelism = v);
            configuration.UploadMaxIntervalSeconds.ApplyIfNotNull(v => result.UploadMaxInterval = TimeSpan.FromSeconds(v));

            return result;
        }

        internal class DemystifiedExceptionLayoutRenderer : ExceptionLayoutRenderer
        {
            /// <inheritdoc />
            protected override void AppendToString(StringBuilder sb, Exception ex)
            {
                sb.Append(ex.ToStringDemystified());
            }
        }
    }
}
