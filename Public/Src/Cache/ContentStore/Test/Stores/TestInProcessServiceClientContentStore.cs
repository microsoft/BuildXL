// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Sessions;
using ContentStoreTest.Test;

namespace ContentStoreTest.Stores
{
    public class TestInProcessServiceClientContentStore : ServiceClientContentStore, ITestServiceClientContentStore
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly ServiceConfiguration _configuration;
        private readonly TimeSpan? _heartbeatInterval;
        public LocalContentServer Server;
        private string _overrideCacheName;
        private bool _doNotStartService;

        private static ServiceClientContentStoreConfiguration CreateConfiguration(
            string cacheName,
            string scenario,
            ServiceConfiguration serviceConfiguration,
            uint retryIntervalSeconds = DefaultRetryIntervalSeconds,
            uint retryCount = DefaultRetryCount)
        {
            return new ServiceClientContentStoreConfiguration(
                       cacheName,
                       new ServiceClientRpcConfiguration((int)serviceConfiguration.GrpcPort),
                       scenario)
                   {
                        RetryCount = retryCount,
                        RetryIntervalSeconds = retryIntervalSeconds,
                   };
        }

        public TestInProcessServiceClientContentStore(
            IAbsFileSystem fileSystem,
            ILogger logger,
            string cacheName,
            string scenario,
            TimeSpan? heartbeatInterval,
            ServiceConfiguration serviceConfiguration,
            uint retryIntervalSeconds = DefaultRetryIntervalSeconds,
            uint retryCount = DefaultRetryCount,
            LocalServerConfiguration localContentServerConfiguration = null,
            Func<AbsolutePath, IContentStore> contentStoreFactory = null)
            : base(logger, fileSystem, CreateConfiguration(cacheName, scenario + TestBase.ScenarioSuffix, serviceConfiguration, retryIntervalSeconds, retryCount))
        {
            _fileSystem = fileSystem;
            _logger = logger;
            _heartbeatInterval = heartbeatInterval;
            _configuration = serviceConfiguration;
            Server = new LocalContentServer(
                _fileSystem,
                _logger,
                Configuration.Scenario,
                contentStoreFactory ?? (path => new FileSystemContentStore(FileSystem, SystemClock.Instance, path)),
                localContentServerConfiguration?.OverrideServiceConfiguration(_configuration) ?? TestConfigurationHelper.CreateLocalContentServerConfiguration(_configuration));
            SetThreadPoolSizes();
        }

        private static void SetThreadPoolSizes()
        {
            ThreadPool.GetMaxThreads(out int workerThreads, out int completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);

            ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }

        public void SetOverrideCacheName(string value)
        {
            _overrideCacheName = value;
        }

        public void SetDoNotStartService(bool value)
        {
            _doNotStartService = value;
        }

        public async Task RestartServerAsync(Context context)
        {
            if (_doNotStartService)
            {
                throw new InvalidOperationException();
            }

            await Server.ShutdownAsync(context).ShouldBeSuccess();
            Server.Dispose();

            Server = new LocalContentServer(
                _fileSystem, _logger, Configuration.Scenario, path => new FileSystemContentStore(FileSystem, SystemClock.Instance, path),
                TestConfigurationHelper.CreateLocalContentServerConfiguration(_configuration));

            var startupResult = await Server.StartupAsync(context);
            if (!startupResult.Succeeded)
            {
                throw new InvalidOperationException($"Server startup Failed {startupResult.ErrorMessage}:{startupResult.Diagnostics}");
            }
        }

        public async Task ShutdownServerAsync(Context context)
        {
            await Server.ShutdownAsync(context).ShouldBeSuccess();
            Server.Dispose();
        }

        protected override async Task<BoolResult> PreStartupAsync(Context context)
        {
            if (_doNotStartService)
            {
                return BoolResult.Success;
            }

            var r = await Task.Run(() => Server.StartupAsync(context));

            if (r.Succeeded)
            {
                if (!(await Task.Run(() => LocalContentServer.EnsureRunning(context, Configuration.Scenario, 10000))))
                {
                    return new BoolResult("Failed to detect server ready in same process");
                }
            }

            return r;
        }

        protected override Task<BoolResult> PostShutdownAsync(Context context)
        {
            return !_doNotStartService && !Server.ShutdownStarted
                ? Server.ShutdownAsync(context)
                : Task.FromResult(BoolResult.Success);
        }

        // RPC config deferred until session creation because server has to be previously started.
        private ServiceClientRpcConfiguration GetRpcConfig()
        {
            var portReaderFactory =
                new MemoryMappedFileGrpcPortSharingFactory(_logger, _configuration.GrpcPortFileName);
            var portReader = portReaderFactory.GetPortReader();
            var grpcPort = portReader.ReadPort();
            return new ServiceClientRpcConfiguration(grpcPort, _heartbeatInterval);
        }

        public override CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new TestServiceClientContentSession(
                    name,
                    implicitPin,
                    Configuration.RetryPolicy,
                    _configuration.DataRootPath,
                    _overrideCacheName ?? Configuration.CacheName,
                    context.Logger,
                    FileSystem,
                    Configuration.Scenario,
                    this,
                    SessionTracer,
                    GetRpcConfig());
                return new CreateSessionResult<IReadOnlyContentSession>(session);
            });
        }

        public override CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(ExecutionTracer, OperationContext(context), name, () =>
            {
                var session = new TestServiceClientContentSession(
                    name,
                    implicitPin,
                    Configuration.RetryPolicy,
                    _configuration.DataRootPath,
                    _overrideCacheName ?? Configuration.CacheName,
                    context.Logger,
                    FileSystem,
                    Configuration.Scenario,
                    this,
                    SessionTracer,
                    GetRpcConfig());
                return new CreateSessionResult<IContentSession>(session);
            });
        }

        protected override void DisposeCore()
        {
            Server?.Dispose();

            base.DisposeCore();
        }
    }
}
