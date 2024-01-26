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
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Logging;
using BuildXL.Cache.Logging.External;
using BuildXL.Utilities.Core;
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
        public static async Task<(ILogger Logger, IDisposable? DisposableToken)> ReplaceLoggerAsync(LoggerFactoryArguments arguments)
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
                var nLogAdapter = await CreateNLogAdapterAsync(operationContext, arguments);
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
                    operationLogger = MdmOperationLogger.Create(
                        context,
                        loggingSettings.MdmAccountName,
                        GetDefaultDimensions(arguments),
                        loggingSettings.SaveMetricsAsynchronously,
                        loggingSettings.MetricsNagleQueueCapacityLimit,
                        loggingSettings.MetricsNagleQueueBatchSize);
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

        private static async Task<IStructuredLogger> CreateNLogAdapterAsync(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            Contract.RequiresNotNull(arguments.LoggingSettings);

            // The NLogAdapter will take ownership of the log for the purposes of shutdown
            var log = await CreateAzureBlobStorageLogAsync(operationContext, arguments);

            try
            {
                // Using a custom renderer to make the exceptions stack traces clearer.
                LayoutRenderer.Register<DemystifiedExceptionLayoutRenderer>("exception");

                return await NLogAdapterHelper.CreateAdapterAsync(
                    operationContext.TracingContext.Logger, arguments.TelemetryFieldsProvider, arguments.LoggingSettings!.NLogConfigurationPath!, arguments.LoggingSettings.NLogConfigurationReplacements, log);
            }
            catch (Exception)
            {
                await log.ShutdownAsync().IgnoreFailure();
                throw;
            }
        }

        private static async Task<AzureBlobStorageLog> CreateAzureBlobStorageLogAsync(OperationContext operationContext, LoggerFactoryArguments arguments)
        {
            var configuration = arguments.LoggingSettings?.Configuration;
            Contract.AssertNotNull(configuration);

            // There is a big issue here: on the one hand, we'd like to be able to configure everything from the XML
            // instead of our JSON configuration, simply because the XML is self-contained. On the other hand, the XML
            // will likely be shared across all stamps, so there's no "stamp-specific" configuration in there. That
            // means all stamp-level configuration must be done through the JSON.

            IAzureStorageCredentials credentials = await arguments.SecretsProvider.GetBlobCredentialsAsync(
                configuration.SecretName,
                configuration.UseSasTokens,
                operationContext.Token);

            var azureBlobStorageLog = new AzureBlobStorageLog(
                configuration: ToInternalConfiguration(configuration),
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
