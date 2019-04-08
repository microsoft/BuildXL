// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

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
            IDistributedCacheServiceHost host,
            HostInfo hostInfo,
            CancellationToken cancellation,
            string dataRootPath,
            DistributedCacheServiceConfiguration configuration,
            string keyspace)
        {
            Logger = logger;
            Copier = copier;
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
