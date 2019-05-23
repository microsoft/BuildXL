// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Vsts;
using BuildXL.Storage;
#if !PLATFORM_OSX
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
#else
using System.Net;
using System.Security;
using Microsoft.VisualStudio.Services.Common;
#endif

namespace BuildXL.Cache.BuildCacheAdapter
{
    internal static class BuildCacheUtils
    {
        private const string CredentialProvidersPathEnvVariable = "ARTIFACT_CREDENTIALPROVIDERS_PATH";

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposed by another object")]
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", Justification = "Not applicable")]
        internal static BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache CreateBuildCacheCache<T>(T cacheConfig, ILogger logger, string pat = null) where T : BuildCacheCacheConfig
        {
            // TODO: Remove check when all clients are updated with unified Dedup flag
            if (ContentHashingUtilities.HashInfo.HashType == HashType.DedupNodeOrChunk ^ cacheConfig.UseDedupStore)
            {
                var store = cacheConfig.UseDedupStore ? "DedupStore" : "BlobStore";
                throw new ArgumentException($"HashType {ContentHashingUtilities.HashInfo.HashType} cannot be used with {store}");
            }

            string credentialProviderPath = Environment.GetEnvironmentVariable(CredentialProvidersPathEnvVariable);
            if (!string.IsNullOrWhiteSpace(credentialProviderPath))
            {
                logger.Debug($"Credential providers path specified: {credentialProviderPath}");
            }
            else
            {
                logger.Debug("Using current user's credentials for obtaining AAD token");
            }

            VssCredentialsFactory credentialsFactory;

#if !PLATFORM_OSX
            credentialsFactory = new VssCredentialsFactory(new VsoCredentialHelper(s => logger.Debug(s)));
#else
            var secPat = new SecureString();
            if (!string.IsNullOrWhiteSpace(pat))
            {
                foreach (char c in pat)
                {
                    secPat.AppendChar(c);
                }
            }
            else
            {
                throw new ArgumentException("PAT must be supplied when running with CoreCLR");
            }

            credentialsFactory = new VssCredentialsFactory(new VssBasicCredential(new NetworkCredential(string.Empty, secPat)));
#endif

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
