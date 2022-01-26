// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Storage;
using BuildXL.Utilities.Authentication;

namespace BuildXL.Cache.BuildCacheAdapter
{
    internal static class BuildCacheUtils
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposed by another object")]
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", Justification = "Not applicable")]
        internal static BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache CreateBuildCacheCache<T>(T cacheConfig, ILogger logger, string pat = null) where T : BuildCacheCacheConfig
        {
            // TODO: Remove check when all clients are updated with unified Dedup flag
            if ((ContentHashingUtilities.HashInfo.HashType.IsValidDedup()) ^ cacheConfig.UseDedupStore)
            {
                var store = cacheConfig.UseDedupStore ? "DedupStore" : "BlobStore";
                throw new ArgumentException($"HashType {ContentHashingUtilities.HashInfo.HashType} cannot be used with {store}");
            }

            var credentialProviderHelper = new CredentialProviderHelper(m => logger.Debug(m));

            if (credentialProviderHelper.IsCredentialProviderSpecified())
            {
                logger.Debug($"Credential providers path specified: '{credentialProviderHelper.CredentialHelperPath}'");
            }
            else
            {
                logger.Debug("Using current user's credentials for obtaining AAD token");
            }

            var credentialsFactory = new VssCredentialsFactory(pat, credentialProviderHelper, m => logger.Debug(m));

            logger.Diagnostic("Creating BuildCacheCache factory");
            var fileSystem = new PassThroughFileSystem(logger);

            // TODO: Once write-behind is implemented send a contentstorefunc down to the create.
            Func<IContentStore> writeThroughStore = null;
            if (!string.IsNullOrWhiteSpace(cacheConfig.CacheName))
            {
                ServiceClientRpcConfiguration rpcConfiguration;
                if (cacheConfig.GrpcPort != 0)
                {
                    rpcConfiguration = new ServiceClientRpcConfiguration((int)cacheConfig.GrpcPort);
                }
                else
                {
                    var factory = new MemoryMappedFileGrpcPortSharingFactory(logger, cacheConfig.GrpcPortFileName);
                    var portReader = factory.GetPortReader();
                    var port = portReader.ReadPort();

                    rpcConfiguration = new ServiceClientRpcConfiguration(port);
                }

                writeThroughStore = () =>
                                        new ServiceClientContentStore(
                                            logger,
                                            fileSystem,
                                            cacheConfig.CacheName,
                                            rpcConfiguration,
                                            cacheConfig.ConnectionRetryIntervalSeconds,
                                            cacheConfig.ConnectionRetryCount,
                                            scenario: cacheConfig.ScenarioName);
            }

            BuildCacheServiceConfiguration buildCacheServiceConfiguration = cacheConfig.AsBuildCacheServiceConfigurationFile();
            return BuildCacheCacheFactory.Create(fileSystem, logger, credentialsFactory, buildCacheServiceConfiguration, writeThroughStore);
        }
    }
}
