// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    ///     Arguments for constructing the nlog logger.
    /// </summary>
    public class LoggerFactoryArguments
    {
        /// <nodoc />
        public ILogger Logger { get; internal set; }

        /// <nodoc />
        public LoggingSettings LoggingSettings { get; }

        /// <nodoc />
        public ISecretsProvider SecretsProvider { get; }

        /// <nodoc />
        public ITelemetryFieldsProvider TelemetryFieldsProvider { get; set; }

        /// <inheritdoc />
        public LoggerFactoryArguments(
            ILogger logger,
            ISecretsProvider secretsProvider,
            LoggingSettings loggingSettings)
        {
            Contract.RequiresNotNull(logger);

            Logger = logger;
            LoggingSettings = loggingSettings;
            SecretsProvider = secretsProvider;
        }
    }
}
