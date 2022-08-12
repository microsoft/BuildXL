// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    ///     Arguments for constructing the nlog logger.
    /// </summary>
    public record LoggerFactoryArguments
    {
        /// <nodoc />
        public ILogger Logger => TracingContext.Logger;

        public Context TracingContext { get; }

        /// <nodoc />
        public LoggingSettings? LoggingSettings { get; }

        /// <nodoc />
        public ISecretsProvider SecretsProvider { get; }

        /// <nodoc />
        public ITelemetryFieldsProvider TelemetryFieldsProvider { get; }

        /// <nodoc />
        public LoggerFactoryArguments(
            Context context,
            ISecretsProvider secretsProvider,
            LoggingSettings? loggingSettings,
            ITelemetryFieldsProvider telemetryFieldsProvider)
        {
            TracingContext = context;
            LoggingSettings = loggingSettings;
            SecretsProvider = secretsProvider;
            TelemetryFieldsProvider = telemetryFieldsProvider;
        }
    }
}
