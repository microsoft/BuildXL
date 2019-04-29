using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    /// Cache configuration for <see cref="LocalCache"/>.
    /// </summary>
    public class LocalCacheConfiguration
    {
        /// <summary>
        /// Whether the cache will communicate with a server in a separate process via GRPC.
        /// </summary>
        public bool EnableContentServer { get; }

        /// <summary>
        /// The GRPC port to use.
        /// </summary>
        public int GrpcPort { get; }

        /// <summary>
        /// Name of one of the named caches owned by CASaaS.
        /// </summary>
        public string CacheName { get; }

        /// <summary>
        /// Name of the custom scenario that the CAS services.
        /// Allows multiple CAS services to coexist in a machine
        /// since this factors into the cache root and the event that
        /// identifies a particular CAS instance.
        /// </summary>
        public string ScenarioName { get; }

        /// <nodoc />
        public uint RetryIntervalSeconds { get; }

        /// <nodoc />
        public uint RetryCount { get; }

        /// <summary>
        /// Create a local cache configuration which has distributed features.
        /// </summary>
        public static LocalCacheConfiguration CreateServerEnabled(int grpcPort, string cacheName, string scenarioName, uint retryIntervalSeconds, uint retryCount)
        {
            Contract.Assert(grpcPort > 0, $"Local server must have a positive GRPC port. Found {grpcPort}.");
            Contract.Assert(!string.IsNullOrWhiteSpace(cacheName), $"Local server must have a non-empty cache name. Found {cacheName}.");

            return new LocalCacheConfiguration(true, grpcPort, cacheName, scenarioName, retryIntervalSeconds, retryCount);
        }

        /// <summary>
        /// Create a local cache configuration which does not have any distributed features.
        /// </summary>
        public static LocalCacheConfiguration CreateServerDisabled()
        {
            return new LocalCacheConfiguration(false);
        }

        private LocalCacheConfiguration(bool enableContentServer, int grpcPort = 0, string cacheName = null, string scenarioName = null, uint retryIntervalSeconds = 0, uint retryCount = 0)
        {
            EnableContentServer = enableContentServer;
            GrpcPort = grpcPort;
            CacheName = cacheName;
            ScenarioName = scenarioName;
            RetryIntervalSeconds = retryIntervalSeconds;
            RetryCount = retryCount;
        }

        /// <summary>
        /// Create a <see cref="ServiceClientContentStoreConfiguration"/> based on this configuration.
        /// </summary>
        public ServiceClientContentStoreConfiguration GetServiceClientContentStoreConfiguration()
        {
            return new ServiceClientContentStoreConfiguration(CacheName, new ServiceClientRpcConfiguration(GrpcPort), ScenarioName);
        }
    }
}
