// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Arguments for constructing cache service
    /// </summary>
    public class DistributedCacheServiceArguments
    {
        /// <nodoc />
        public ILogger Logger { get; }

        /// <nodoc />
        public IAbsolutePathFileCopier Copier { get; }

        public ICopyRequester CopyRequester { get; }

        /// <nodoc />
        public IAbsolutePathTransformer PathTransformer { get; }

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

        /// <inheritdoc />
        public DistributedCacheServiceArguments(
            ILogger logger,
            IAbsolutePathFileCopier copier,
            IAbsolutePathTransformer pathTransformer,
            ICopyRequester copyRequester,
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            CancellationToken cancellation,
            string dataRootPath,
            DistributedCacheServiceConfiguration configuration,
            string keyspace)
        {
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
            string keySpaceString = keyspace;
            if (!string.IsNullOrWhiteSpace(configuration.DistributedContentSettings.KeySpacePrefix))
            {
                keySpaceString = configuration.DistributedContentSettings.KeySpacePrefix + keySpaceString;
            }

            if (configuration.UseStampBasedIsolation)
            {
                keySpaceString = hostInfo.StampId + keySpaceString;
            }

            return keySpaceString;
        }
    }
}
