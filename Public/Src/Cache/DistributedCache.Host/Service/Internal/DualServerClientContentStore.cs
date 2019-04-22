// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Content store containing both a distributed <see cref="LocalContentStore"/> and <see cref="ServiceClientContentStore"/>.
    /// </summary>
    public class DualServerClientContentStore : IContentStore
    {
        private LocalContentServer _localContentServer;
        private ServiceClientContentStore _serviceClientContentStore;

        /// <inheritdoc />
        public bool StartupCompleted => (_localContentServer?.StartupCompleted ?? false) && (_serviceClientContentStore?.StartupCompleted ?? false);

        /// <inheritdoc />
        public bool StartupStarted => (_localContentServer?.StartupStarted ?? false) && (_serviceClientContentStore?.StartupStarted ?? false);

        /// <inheritdoc />
        public bool ShutdownCompleted => (_localContentServer?.ShutdownCompleted ?? false) && (_serviceClientContentStore?.ShutdownCompleted ?? false);

        /// <inheritdoc />
        public bool ShutdownStarted => (_localContentServer?.ShutdownStarted ?? false) && (_serviceClientContentStore?.ShutdownStarted ?? false);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileSystem">File system object used by the client.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="copier">File copier used by the content server.</param>
        /// <param name="pathTransformer">File path transformer used by the content server.</param>
        /// <param name="maxQuotaMB">Maximum size (in MB) of the cache.</param>
        /// <param name="cacheName">Name of the cache.</param>
        /// <param name="grpcPort">GRPC port which the server listens to and the client transmits to.</param>
        /// <param name="stampId">Redis stamp identifier.</param>
        /// <param name="ringId">Redis ring identifier.</param>
        /// <param name="scenarioName">Unique name for the CAS. Allows multiple CAS services to coexist.</param>
        /// <param name="dataRootPath">Path to the root of CAS content.</param>
        public DualServerClientContentStore(
            IAbsFileSystem fileSystem,
            ILogger logger,
            IAbsolutePathFileCopier copier,
            IAbsolutePathTransformer pathTransformer,
            int maxQuotaMB,
            string cacheName,
            int grpcPort,
            string stampId,
            string ringId,
            string scenarioName,
            AbsolutePath dataRootPath)
        {
            Contract.Assert(grpcPort >= 0);
            Contract.Assert(maxQuotaMB >= 0);
            Contract.Assert(!string.IsNullOrWhiteSpace(cacheName));
            Contract.Assert(!string.IsNullOrWhiteSpace(ringId));
            Contract.Assert(!string.IsNullOrWhiteSpace(stampId));

            var redisConnectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable);

            var localCasSettings = LocalCasSettings.Default(maxQuotaMB, dataRootPath.Path, cacheName, (uint)grpcPort);
            localCasSettings.ServiceSettings.ScenarioName = scenarioName;

            if (dataRootPath.IsLocal)
            {
                localCasSettings.DrivePreferenceOrder.Add(AbsolutePath.RootPath(dataRootPath.DriveLetter).Path);
            }

            var contentServerFactory = new ContentServerFactory(new DistributedCacheServiceArguments(
                logger,
                copier,
                pathTransformer,
                new TestDistributedCacheServiceHost(),
                new HostInfo(stampId, ringId, new List<string>()),
                default(CancellationToken),
                dataRootPath.Path,
                new DistributedCacheServiceConfiguration(
                    localCasSettings,
                    DistributedContentSettings.CreateEnabled(new Dictionary<string, string>() { { stampId, redisConnectionString } }, true)),
                string.Empty));
            _localContentServer = contentServerFactory.Create();

            _serviceClientContentStore = new ServiceClientContentStore(
                logger,
                fileSystem,
                new ServiceClientContentStoreConfiguration(cacheName, new ServiceClientRpcConfiguration(grpcPort), scenarioName));
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return _serviceClientContentStore.CreateReadOnlySession(context, name, implicitPin);
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return _serviceClientContentStore.CreateSession(context, name, implicitPin);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _serviceClientContentStore?.Dispose();
            _localContentServer?.Dispose();
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return _serviceClientContentStore.GetStatsAsync(context);
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            await _serviceClientContentStore.ShutdownAsync(context).ThrowIfFailure();
            return await _localContentServer.ShutdownAsync(context).ThrowIfFailure();
        }

        /// <inheritdoc />
        public async Task<BoolResult> StartupAsync(Context context)
        {
            await _localContentServer.StartupAsync(context).ThrowIfFailure();
            return await _serviceClientContentStore.StartupAsync(context).ThrowIfFailure();
        }

        private sealed class TestDistributedCacheServiceHost : IDistributedCacheServiceHost
        {
            public string GetSecretStoreValue(string key)
            {
                return key;
            }

            public void OnStartedService()
            {
            }

            public Task OnStartingServiceAsync()
            {
                return Task.Run(() => { });
            }

            public void OnTeardownCompleted()
            {
            }

            public Task<Dictionary<string, string>> RetrieveKeyVaultSecretsAsync(List<string> secrets, CancellationToken token)
            {
                return Task.FromResult(new Dictionary<string, string>());
            }
        }
    }
}
