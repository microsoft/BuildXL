// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    ///     Arguments for constructing the cache service.
    /// </summary>
    /// <remarks>
    ///     This is our way to receive parameters from clients that are hosting a cache service. This object is to be
    ///     created on the side of whoever is using the cache, and will be processed by the cache to build all of the
    ///     required objects.
    /// </remarks>
    public record DistributedCacheServiceArguments : LoggerFactoryArguments
    {
        /// <nodoc />
        public DistributedCacheServiceHostOverrides Overrides { get; set; } = DistributedCacheServiceHostOverrides.Default;

        /// <summary>
        ///     When this functor is present, and assuming the cache replaces the host's logger with its own, it is
        ///     expected to build <see cref="Copier"/> and <see cref="CopyRequester"/>.
        ///     
        ///     This is done this way because constructing those elements requires access to an <see cref="ILogger"/>,
        ///     which will be replaced cache-side.
        /// </summary>
        public Func<ILogger, (
            IRemoteFileCopier Copier,
            IContentCommunicationManager CopyRequester)>? BuildCopyInfrastructure { get; set; } = null;

        /// <nodoc />
        public IRemoteFileCopier? Copier { get; internal set; }

        /// <nodoc />
        public IContentCommunicationManager? CopyRequester { get; internal set; }

        /// <nodoc />
        public IDistributedCacheServiceHost Host { get; init;  }

        /// <nodoc />
        public IAbsFileSystem FileSystem { get; }

        /// <nodoc />
        public HostInfo HostInfo { get; }

        /// <nodoc />
        public CancellationToken Cancellation { get; internal set; }

        /// <nodoc />
        public string DataRootPath { get; }

        /// <nodoc />
        public DistributedCacheServiceConfiguration Configuration { get; }

        /// <nodoc />
        public string Keyspace { get; }

        /// <summary>
        /// If true, the configuration will be traced during construction.
        /// </summary>
        public bool TraceConfiguration { get; set; } = true;

        /// <summary>
        /// A backward compat constructor.
        /// </summary>
        public DistributedCacheServiceArguments(
            ILogger logger,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            IRemoteFileCopier? copier,
            IContentCommunicationManager? copyRequester,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            CancellationToken cancellation,
            string dataRootPath,
            DistributedCacheServiceConfiguration configuration,
            string? keyspace,
            IAbsFileSystem? fileSystem = null)
            : this(new Context(logger), telemetryFieldsProvider, copier, copyRequester, host, hostInfo, cancellation, dataRootPath, configuration, keyspace, fileSystem)
        {
        }

        /// <inheritdoc />
        public DistributedCacheServiceArguments(
            Context tracingContext,
            ITelemetryFieldsProvider telemetryFieldsProvider,
            IRemoteFileCopier? copier,
            IContentCommunicationManager? copyRequester,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            CancellationToken cancellation,
            string dataRootPath,
            DistributedCacheServiceConfiguration configuration,
            string? keyspace,
            IAbsFileSystem? fileSystem = null)
            : base(tracingContext, host, configuration.LoggingSettings, telemetryFieldsProvider)
        {
            Contract.Requires(tracingContext != null);
            Contract.Requires(host != null);
            Contract.Requires(hostInfo != null);
            Contract.Requires(configuration != null);

            Copier = copier;
            CopyRequester = copyRequester;
            Host = host;
            HostInfo = hostInfo;
            Cancellation = cancellation;
            DataRootPath = dataRootPath;
            Configuration = configuration;
            FileSystem = fileSystem ?? new PassThroughFileSystem(tracingContext.Logger);

            Keyspace = ComputeKeySpace(hostInfo, configuration, keyspace);
        }

        private static string ComputeKeySpace(HostInfo hostInfo, DistributedCacheServiceConfiguration configuration, string? keyspace)
        {
            string? keySpaceString = keyspace;
            if (!string.IsNullOrWhiteSpace(configuration.DistributedContentSettings.KeySpacePrefix))
            {
                keySpaceString = configuration.DistributedContentSettings.KeySpacePrefix + keySpaceString;
            }

            if (configuration.UseStampBasedIsolation)
            {
                keySpaceString = hostInfo.StampId + keySpaceString;
            }

            keySpaceString = hostInfo.AppendRingSpecifierIfNeeded(keySpaceString, configuration.DistributedContentSettings.UseRingIsolation);

            return keySpaceString;
        }
    }
}
