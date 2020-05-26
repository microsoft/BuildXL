// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

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
    public class DistributedCacheServiceArguments
    {
        /// <nodoc />
        public DistributedCacheServiceHostOverrides Overrides { get; set; } = DistributedCacheServiceHostOverrides.Default;

        /// <nodoc />
        public ILogger Logger { get; internal set; }

        /// <summary>
        ///     When this functor is present, and assuming the cache replaces the host's logger with its own, it is
        ///     expected to buid <see cref="Copier"/>, <see cref="PathTransformer"/>, and <see cref="CopyRequester"/>.
        ///     
        ///     This is done this way because constructing those elements requires access to an <see cref="ILogger"/>,
        ///     which will be replaced cache-side.
        /// </summary>
        public Func<ILogger, (
            IAbsolutePathFileCopier Copier,
            IAbsolutePathTransformer PathTransformer,
            IContentCommunicationManager CopyRequester)> BuildCopyInfrastructure { get; set; } = null;

        /// <nodoc />
        public IAbsolutePathFileCopier Copier { get; internal set; }

        /// <nodoc />
        public IAbsolutePathTransformer PathTransformer { get; internal set; }

        /// <nodoc />
        public IContentCommunicationManager CopyRequester { get; internal set; }

        /// <nodoc />
        public IDistributedCacheServiceHost Host { get; }

        /// <nodoc />
        public HostInfo HostInfo { get; }

        /// <nodoc />
        public CancellationToken Cancellation { get; }

        /// <nodoc />
        public string DataRootPath { get; }

        /// <nodoc />
        public DistributedCacheServiceConfiguration Configuration { get; }

        /// <nodoc />
        public string Keyspace { get; }

        /// <nodoc />
        public ITelemetryFieldsProvider TelemetryFieldsProvider { get; set; }

        /// <inheritdoc />
        public DistributedCacheServiceArguments(
            ILogger logger,
            IAbsolutePathFileCopier copier,
            IAbsolutePathTransformer pathTransformer,
            IContentCommunicationManager copyRequester,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            CancellationToken cancellation,
            string dataRootPath,
            DistributedCacheServiceConfiguration configuration,
            string keyspace)
        {
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(host);
            Contract.RequiresNotNull(hostInfo);
            Contract.RequiresNotNull(configuration);

            Logger = logger;
            Copier = copier;
            CopyRequester = copyRequester;
            PathTransformer = pathTransformer;
            Host = host;
            HostInfo = hostInfo;
            Cancellation = cancellation;
            DataRootPath = dataRootPath;
            Configuration = configuration;

            Keyspace = ComputeKeySpace(hostInfo, configuration, keyspace);
        }

        private static string ComputeKeySpace(HostInfo hostInfo, DistributedCacheServiceConfiguration configuration, string keyspace)
        {
            Contract.RequiresNotNull(hostInfo);
            Contract.RequiresNotNull(configuration);

            string keySpaceString = keyspace;
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
