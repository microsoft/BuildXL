// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    ///     Daemon configuration.
    /// </summary>
    public sealed class DaemonConfig : IServerConfig, IClientConfig
    {
        /// <nodoc/>
        public ILogger Logger { get; }

        /// <inheritdoc/>
        ILogger IServerConfig.Logger => Logger;

        /// <inheritdoc/>
        ILogger IClientConfig.Logger => Logger;

        #region ConfigOptions

        // ==================================================================================================
        // Config options
        // ==================================================================================================

        /// <summary>
        ///     Moniker for identifying client/server communications.
        /// </summary>
        public string Moniker { get; }

        /// <inheritdoc />
        public int MaxConcurrentClients => DefaultMaxConcurrentClients;

        /// <inheritdoc />
        public int MaxConnectRetries { get; }

        /// <inheritdoc />
        public TimeSpan ConnectRetryDelay { get; }

        /// <inheritdoc />
        public bool StopOnFirstFailure { get; }

        /// <summary>
        ///     Enable logging ETW events related to drop creation and finalization.
        /// </summary>
        public bool EnableCloudBuildIntegration { get; }
        #endregion

        #region Defaults

        // ==================================================================================================
        // Defaults
        // ==================================================================================================

        /// <nodoc/>
        public const int DefaultMaxConcurrentClients = 5000;

        /// <nodoc/>
        public const int DefaultMaxConnectRetries = 1;

        /// <nodoc/>
        public const bool DefaultStopOnFirstFailure = false;

        /// <nodoc/>
        public static readonly TimeSpan DefaultConnectRetryDelay = TimeSpan.FromSeconds(5);

        /// <nodoc/>
        public static bool DefaultEnableCloudBuildIntegration { get; } = false;
        #endregion

        // ==================================================================================================
        // Constructor
        // ==================================================================================================

        /// <nodoc/>
        public DaemonConfig(
            ILogger logger,
            string moniker,
            int? maxConnectRetries = null,
            TimeSpan? connectRetryDelay = null,
            bool? stopOnFirstFailure = null,
            bool? enableCloudBuildIntegration = null)
        {
            Contract.Requires(logger != null);
            Moniker = moniker;
            Logger = logger;
            MaxConnectRetries = maxConnectRetries ?? DefaultMaxConnectRetries;
            ConnectRetryDelay = connectRetryDelay ?? DefaultConnectRetryDelay;
            StopOnFirstFailure = stopOnFirstFailure ?? DefaultStopOnFirstFailure;
            EnableCloudBuildIntegration = enableCloudBuildIntegration ?? DefaultEnableCloudBuildIntegration;
        }
    }
}
