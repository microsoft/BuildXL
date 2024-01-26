// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using NLog.LayoutRenderers;

#nullable enable

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    /// Helps configuring an NLog adapter using the expected schema
    /// </summary>
    public static class NLogAdapterHelper
    {
        /// <summary>
        /// Creates a <see cref="IStructuredLogger"/> that uploads log lines to a blob storage account following the schema
        /// CASaas uses (check Public\Src\Cache\Kusto\NLog.example.config).
        /// </summary>
        public static Task<IStructuredLogger> CreateAdapterAsync(
            ILogger logger,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            string nLogConfigurationPath,
            Dictionary<string, string> configurationReplacements,
            AzureBlobStorageLog log)
        {
            return CreateAdapterAsync(
                logger,
                telemetryFieldsProvider,
                GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.LocalLocationStoreRole),
                GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey.BuildId),
                nLogConfigurationPath,
                configurationContent: null,
                configurationReplacements, log);
        }

        /// <summary>
        /// Creates a <see cref="IStructuredLogger"/> that uploads log lines to a blob storage account for the cache client.
        /// </summary>
        /// <remarks>
        /// The schema of the log line follows the same used in <see cref="CreateAdapterAsync(ILogger, ITelemetryFieldsProvider, string, Dictionary{string, string}, AzureBlobStorageLog)"/>
        /// to simplify existing cache queries used for diagnosing CASaas issues.
        /// </remarks>
        public static Task<IStructuredLogger> CreateAdapterForCacheClientAsync(
            ILogger logger,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            string role,
            Dictionary<string, string> configurationReplacements,
            AzureBlobStorageLog log)
        {
            return CreateAdapterAsync(logger, telemetryFieldsProvider, role, buildId: telemetryFieldsProvider.BuildId, nLogConfigurationPath: null, configurationContent: GetNLogClientConfigurationContent(), configurationReplacements, log);
        }

        private static Task<IStructuredLogger> CreateAdapterAsync(
            ILogger logger,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            string? role,
            string? buildId,
            string? nLogConfigurationPath,
            string? configurationContent,
            Dictionary<string, string> configurationReplacements,
            AzureBlobStorageLog log)
        {
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
                    return new AzureBlobStorageLogTarget(log);
                }

                return defaultConstructor(type);
            };

            NLog.Targets.Target.Register<AzureBlobStorageLogTarget>(nameof(AzureBlobStorageLogTarget));

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

            LayoutRenderer.Register("Role", _ => role);
            LayoutRenderer.Register("BuildId", _ => buildId);

            // Follows ISO8601 without timezone specification.
            // See: https://kusto.azurewebsites.net/docs/query/scalar-data-types/datetime.html
            // See: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings?view=netframework-4.8#the-round-trip-o-o-format-specifier
            var processStartTimeUtc = SystemClock.Instance.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            NLog.LayoutRenderers.LayoutRenderer.Register("ProcessStartTimeUtc", _ => processStartTimeUtc);

#pragma warning disable AsyncFixer02 // Long-running or blocking operations inside an async method
            // If the log path is specified, retrieve the content from it
            if (nLogConfigurationPath != null)
            {
                // We don't use ReadAllTextAsync here because net472 doesn't allow us to do so
                configurationContent = File.ReadAllText(nLogConfigurationPath);
            }
#pragma warning restore AsyncFixer02 // Long-running or blocking operations inside an async method

            foreach (var replacement in configurationReplacements)
            {
                configurationContent = configurationContent!.Replace(replacement.Key, replacement.Value);
            }

            // The following are not disposed because NLog takes ownership of them
            var textReader = new StringReader(configurationContent!);
            var reader = XmlReader.Create(textReader);
            var configuration = new NLog.Config.XmlLoggingConfiguration(reader, nLogConfigurationPath);

            return Task.FromResult((IStructuredLogger)new NLogAdapter(logger, configuration, log));
        }

        private static string GetNLogClientConfigurationContent()
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<nlog xmlns=""http://www.nlog-project.org/schemas/NLog.xsd""
      xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
      autoReload=""true""
      throwExceptions=""true""
      throwConfigExceptions=""true""
      parseMessageTemplates=""false"">

    <targets>
        <target name=""LogUpload""
            xsi:type=""AzureBlobStorageLogTarget"">
            
            <layout xsi:type=""CsvLayout"" quoting=""Nothing"" withHeader=""false"" delimiter=""Comma"">
                <column name=""PreciseTimeStamp"" layout=""${{longdate:universalTime=true}}"" quoting=""Nothing"" />
                <column name=""LocalPreciseTimeStamp"" layout=""${{longdate:universalTime=false}}"" quoting=""Nothing"" />
                <column name=""CorrelationId"" layout=""${{event-properties:item=CorrelationId}}"" quoting=""Nothing"" />
                <column name=""Role"" layout=""${{Role}}"" quoting=""Nothing"" />
                <column name=""Component"" layout=""${{event-properties:item=OperationComponent}}"" quoting=""All"" />
                <column name=""Operation"" layout=""${{event-properties:item=OperationName}}"" quoting=""All"" />
                <column name=""Arguments"" layout=""${{event-properties:item=OperationArguments}}"" quoting=""All"" />
                <column name=""Duration"" layout=""${{event-properties:item=OperationDuration}}"" quoting=""Nothing"" />
                <column name=""Result"" layout=""${{event-properties:item=OperationResult}}"" quoting=""All"" />
                <column name=""BuildId"" layout=""${{BuildId}}"" quoting=""Nothing"" />
                <!-- See: https://github.com/NLog/NLog/wiki/Message-Layout-Renderer -->
                <column name=""Message"" layout=""${{message}}"" quoting=""All"" />
                <column name=""Exception"" layout=""${{exception:format=toString}}"" quoting=""All"" />
                <!-- See: https://github.com/NLog/NLog/wiki/ProcessId-Layout-Renderer -->
                <column name=""ProcessId"" layout=""${{processid}}"" quoting=""Nothing"" />
                <!-- See: https://github.com/NLog/NLog/wiki/ThreadId-Layout-Renderer -->
                <column name=""ThreadId"" layout=""${{threadid}}"" quoting=""Nothing"" />
                <column name=""Machine"" layout=""${{MachineName}}"" quoting=""Nothing"" />
                <column name=""Stamp"" layout=""${{Stamp}}"" quoting=""Nothing"" />
                <column name=""Ring"" layout=""${{Ring}}"" quoting=""Nothing"" />
                <column name=""ConfigurationId"" layout=""${{ConfigurationId}}"" quoting=""Nothing"" />
                <column name=""Service"" layout=""${{ServiceName}}"" quoting=""Nothing"" />
                <column name=""ServiceVersion"" layout=""${{ServiceVersion}}"" quoting=""Nothing"" />
                <column name=""CacheVersion"" layout=""${{CacheVersion}}"" quoting=""Nothing"" />
                <column name=""ProcessStartTimeUtc"" layout=""${{ProcessStartTimeUtc}}"" quoting=""Nothing"" />
                <!-- See: https://github.com/NLog/NLog/wiki/Level-Layout-Renderer -->
                <column name=""LogLevel"" layout=""${{level:format=Ordinal}}"" quoting=""Nothing"" />
                <column name=""MachineFunction"" layout=""${{APMachineFunction}}"" quoting=""Nothing"" />
                <column name=""Environment"" layout=""${{APEnvironment}}"" quoting=""Nothing"" />
            </layout>
        </target>
    </targets>

    <rules>
        <!-- Trace corresponds to our Diagnostic level -->
        <logger name=""*"" minlevel=""Trace"" writeTo=""LogUpload"" />
    </rules>
</nlog>";
        }

    }
}
